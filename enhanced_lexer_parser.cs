// enhanced_lexer_parser.cs
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    // Lexer (Tokenizer) - Enhanced with F-String and Compound Assignment Support
    public class Lexer
    {
        private string input;
        private int position;
        private char currentChar;
        private int line;
        private int column;
        private Stack<int> indentStack;
        private bool atLineStart;

        public Lexer(string input)
        {
            this.input = input;
            position = 0;
            line = 1;
            column = 1;
            indentStack = new Stack<int>();
            indentStack.Push(0); // Base indentation level
            atLineStart = true;
            currentChar = position < input.Length ? input[position] : '\0';
        }

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
                if (currentChar != ' ' && currentChar != '\t')
                    atLineStart = false;
            }

            position++;
            currentChar = position < input.Length ? input[position] : '\0';
        }

        private void SkipWhitespace()
        {
            while (currentChar != '\0' && char.IsWhiteSpace(currentChar) && currentChar != '\n')
                Advance();
        }

        private string ReadNumber()
        {
            var sb = new StringBuilder();
            bool hasDot = false;

            while (currentChar != '\0' && (char.IsDigit(currentChar) || (currentChar == '.' && !hasDot)))
            {
                if (currentChar == '.') hasDot = true;
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
                    switch (currentChar)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '\\': sb.Append('\\'); break;
                        case '\'': sb.Append('\''); break;
                        case '"': sb.Append('"'); break;
                        case '0': sb.Append('\0'); break;
                        default: sb.Append(currentChar); break;
                    }
                }
                else
                {
                    sb.Append(currentChar);
                }
                Advance();
            }

            if (currentChar == quote)
                Advance(); // Skip closing quote
            else
                throw new PythonException("SyntaxError", $"Unterminated string literal", startLine, startColumn);

            return sb.ToString();
        }

        private string ReadTripleQuotedString(string quoteType)
        {
            var sb = new StringBuilder();
            int startLine = line;
            int startColumn = column;
            
            // Skip the opening triple quotes
            for (int i = 0; i < 3; i++)
                Advance();

            // Read until we find the closing triple quotes
            while (position + 2 < input.Length)
            {
                if (currentChar == quoteType[0] && 
                    position + 1 < input.Length && input[position + 1] == quoteType[0] &&
                    position + 2 < input.Length && input[position + 2] == quoteType[0])
                {
                    // Found closing triple quotes
                    for (int i = 0; i < 3; i++)
                        Advance();
                    return sb.ToString();
                }
                
                sb.Append(currentChar);
                Advance();
            }

            // Handle end of string
            while (currentChar != '\0')
            {
                if (currentChar == quoteType[0] && 
                    position + 1 < input.Length && input[position + 1] == quoteType[0] &&
                    position + 2 < input.Length && input[position + 2] == quoteType[0])
                {
                    // Found closing triple quotes
                    for (int i = 0; i < 3; i++)
                        Advance();
                    return sb.ToString();
                }
                sb.Append(currentChar);
                Advance();
            }

            throw new PythonException("SyntaxError", $"Unterminated triple-quoted string literal", startLine, startColumn);
        }

        private string ReadTripleQuotedFString(string quoteType)
        {
            var sb = new StringBuilder();
            int startLine = line;
            int startColumn = column;
            
            // Skip the opening triple quotes
            for (int i = 0; i < 3; i++)
                Advance();

            // Read until we find the closing triple quotes
            while (position + 2 < input.Length)
            {
                if (currentChar == quoteType[0] && 
                    position + 1 < input.Length && input[position + 1] == quoteType[0] &&
                    position + 2 < input.Length && input[position + 2] == quoteType[0])
                {
                    // Found closing triple quotes
                    for (int i = 0; i < 3; i++)
                        Advance();
                    return sb.ToString();
                }
                
                sb.Append(currentChar);
                Advance();
            }

            // Handle end of string
            while (currentChar != '\0')
            {
                if (currentChar == quoteType[0] && 
                    position + 1 < input.Length && input[position + 1] == quoteType[0] &&
                    position + 2 < input.Length && input[position + 2] == quoteType[0])
                {
                    // Found closing triple quotes
                    for (int i = 0; i < 3; i++)
                        Advance();
                    return sb.ToString();
                }
                sb.Append(currentChar);
                Advance();
            }

            throw new PythonException("SyntaxError", $"Unterminated triple-quoted f-string literal", startLine, startColumn);
        }

        private string ReadFString(char quote)
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
                    switch (currentChar)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '\\': sb.Append('\\'); break;
                        case '\'': sb.Append('\''); break;
                        case '"': sb.Append('"'); break;
                        case '0': sb.Append('\0'); break;
                        default: sb.Append(currentChar); break;
                    }
                }
                else
                {
                    sb.Append(currentChar);
                }
                Advance();
            }

            if (currentChar == quote)
                Advance(); // Skip closing quote
            else
                throw new PythonException("SyntaxError", $"Unterminated f-string literal", startLine, startColumn);

            return sb.ToString();
        }

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

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();

            while (currentChar != '\0')
            {
                int tokenLine = line;
                int tokenColumn = column;

                // Handle indentation at the start of a line
                if (atLineStart && currentChar != '\n' && currentChar != '#')
                {
                    int indentLevel = 0;
                    while (currentChar == ' ' || currentChar == '\t')
                    {
                        if (currentChar == ' ')
                            indentLevel += 1;
                        else if (currentChar == '\t')
                            indentLevel += 8; // Tab = 8 spaces
                        Advance();
                    }

                    // Skip empty lines and comments
                    if (currentChar == '\n' || currentChar == '#')
                    {
                        if (currentChar == '#')
                        {
                            while (currentChar != '\0' && currentChar != '\n')
                                Advance();
                        }
                        continue;
                    }

                    // Process indentation changes
                    int currentIndent = indentStack.Peek();
                    if (indentLevel > currentIndent)
                    {
                        indentStack.Push(indentLevel);
                        tokens.Add(new Token(TokenType.INDENT, "", tokenLine, tokenColumn));
                    }
                    else if (indentLevel < currentIndent)
                    {
                        while (indentStack.Count > 1 && indentStack.Peek() > indentLevel)
                        {
                            indentStack.Pop();
                            tokens.Add(new Token(TokenType.DEDENT, "", tokenLine, tokenColumn));
                        }

                        if (indentStack.Peek() != indentLevel)
                        {
                            throw new PythonException("IndentationError", $"Unindent does not match any outer indentation level", tokenLine, tokenColumn);
                        }
                    }

                    atLineStart = false;
                }

                if (char.IsWhiteSpace(currentChar) && currentChar != '\n')
                {
                    SkipWhitespace();
                    continue;
                }

                if (currentChar == '#') // Comments
                {
                    while (currentChar != '\0' && currentChar != '\n')
                        Advance();
                    continue;
                }

                if (currentChar == '\n')
                {
                    tokens.Add(new Token(TokenType.NEWLINE, "\n", tokenLine, tokenColumn));
                    Advance();
                    continue;
                }

                if (char.IsDigit(currentChar))
                {
                    tokens.Add(new Token(TokenType.NUMBER, ReadNumber(), tokenLine, tokenColumn));
                    continue;
                }

                // Check for triple-quoted strings first (before f-strings and regular strings)
                if (position + 2 < input.Length)
                {
                    string threeChars = input.Substring(position, 3);
                    if (threeChars == "'''" || threeChars == "\"\"\"")
                    {
                        string content = ReadTripleQuotedString(threeChars.Substring(0, 1));
                        tokens.Add(new Token(TokenType.STRING, content, tokenLine, tokenColumn));
                        continue;
                    }
                }

                // Check for f-strings (but not inside triple quotes)
                if (currentChar == 'f' && position + 1 < input.Length && 
                    (input[position + 1] == '"' || input[position + 1] == '\''))
                {
                    // Check if it's a triple-quoted f-string
                    if (position + 3 < input.Length)
                    {
                        char quoteChar = input[position + 1];
                        if (input[position + 2] == quoteChar && input[position + 3] == quoteChar)
                        {
                            // This is an f-triple-quoted string
                            Advance(); // Skip 'f'
                            string content = ReadTripleQuotedFString(quoteChar.ToString());
                            tokens.Add(new Token(TokenType.FSTRING, content, tokenLine, tokenColumn));
                            continue;
                        }
                    }
                    
                    // Regular f-string
                    Advance(); // Skip 'f'
                    char quote = currentChar;
                    tokens.Add(new Token(TokenType.FSTRING, ReadFString(quote), tokenLine, tokenColumn));
                    continue;
                }

                if (currentChar == '"' || currentChar == '\'')
                {
                    char quote = currentChar;
                    tokens.Add(new Token(TokenType.STRING, ReadString(quote), tokenLine, tokenColumn));
                    continue;
                }

                if (char.IsLetter(currentChar) || currentChar == '_')
                {
                    string identifier = ReadIdentifier();
                    TokenType tokenType = identifier switch
                    {
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
                        "with" => TokenType.WITH,  // Added WITH
                        "del" => TokenType.DEL,    // Added DEL
                        "True" => TokenType.BOOLEAN,
                        "False" => TokenType.BOOLEAN,
                        "None" => TokenType.NONE,
                        _ => TokenType.IDENTIFIER
                    };
                    tokens.Add(new Token(tokenType, identifier, tokenLine, tokenColumn));
                    continue;
                }

                // Check for compound assignment operators first
                if (position + 1 < input.Length)
                {
                    string twoChar = input.Substring(position, 2);
                    
                    // Compound assignment operators
                    if (new[] { "+=", "-=", "*=", "/=", "%=" }.Contains(twoChar))
                    {
                        tokens.Add(new Token(TokenType.COMPOUND_ASSIGN, twoChar, tokenLine, tokenColumn));
                        Advance();
                        Advance();
                        continue;
                    }
                    
                    // Check for **=
                    if (position + 2 < input.Length && input.Substring(position, 3) == "**=")
                    {
                        tokens.Add(new Token(TokenType.COMPOUND_ASSIGN, "**=", tokenLine, tokenColumn));
                        Advance();
                        Advance();
                        Advance();
                        continue;
                    }
                    
                    // Other two-character operators
                    if (new[] { "==", "!=", "<=", ">=", "**", "->" }.Contains(twoChar))
                    {
                        tokens.Add(new Token(TokenType.OPERATOR, twoChar, tokenLine, tokenColumn));
                        Advance();
                        Advance();
                        continue;
                    }
                }

                // Single-character tokens
                switch (currentChar)
                {
                    case '+':
                    case '-':
                    case '*':
                    case '/':
                    case '%':
                    case '<':
                    case '>':
                        tokens.Add(new Token(TokenType.OPERATOR, currentChar.ToString(), tokenLine, tokenColumn));
                        break;
                    case '=':
                        tokens.Add(new Token(TokenType.ASSIGN, "=", tokenLine, tokenColumn));
                        break;
                    case '(':
                        tokens.Add(new Token(TokenType.LPAREN, "(", tokenLine, tokenColumn));
                        break;
                    case ')':
                        tokens.Add(new Token(TokenType.RPAREN, ")", tokenLine, tokenColumn));
                        break;
                    case '[':
                        tokens.Add(new Token(TokenType.LBRACKET, "[", tokenLine, tokenColumn));
                        break;
                    case ']':
                        tokens.Add(new Token(TokenType.RBRACKET, "]", tokenLine, tokenColumn));
                        break;
                    case '{':
                        tokens.Add(new Token(TokenType.LBRACE, "{", tokenLine, tokenColumn));
                        break;
                    case '}':
                        tokens.Add(new Token(TokenType.RBRACE, "}", tokenLine, tokenColumn));
                        break;
                    case ':':
                        tokens.Add(new Token(TokenType.COLON, ":", tokenLine, tokenColumn));
                        break;
                    case ',':
                        tokens.Add(new Token(TokenType.COMMA, ",", tokenLine, tokenColumn));
                        break;
                    case '.':
                        tokens.Add(new Token(TokenType.DOT, ".", tokenLine, tokenColumn));
                        break;
                    default:
                        throw new PythonException("SyntaxError", $"Unexpected character: {currentChar}", tokenLine, tokenColumn);
                }
                Advance();
            }

            // Add DEDENT tokens for remaining indentation levels
            while (indentStack.Count > 1)
            {
                indentStack.Pop();
                tokens.Add(new Token(TokenType.DEDENT, "", line, column));
            }

            tokens.Add(new Token(TokenType.EOF, "", line, column));
            return tokens;
        }
    }

    // Enhanced Parser with Line/Column Tracking for AST Nodes
    public partial class Parser
    {
        private List<Token> tokens;
        private int position;
        private Token currentToken;

        public Parser(List<Token> tokens)
        {
            this.tokens = tokens;
            position = 0;
            currentToken = tokens[position];
        }

        private void Advance()
        {
            position++;
            currentToken = position < tokens.Count ? tokens[position] : new Token(TokenType.EOF, "", currentToken?.Line ?? 1, currentToken?.Column ?? 1);
        }

        private void SkipNewlines()
        {
            while (currentToken.Type == TokenType.NEWLINE)
                Advance();
        }

        private void SkipNewlinesAndIndents()
        {
            while (currentToken.Type == TokenType.NEWLINE ||
                   currentToken.Type == TokenType.INDENT ||
                   currentToken.Type == TokenType.DEDENT)
                Advance();
        }

        private void Expect(TokenType tokenType)
        {
            if (currentToken.Type != tokenType)
                throw new PythonException("SyntaxError", $"Expected {tokenType}, got {currentToken.Type}", currentToken.Line, currentToken.Column);
            Advance();
        }

        public List<ASTNode> Parse()
        {
            var statements = new List<ASTNode>();
            SkipNewlines();

            while (currentToken.Type != TokenType.EOF)
            {
                if (currentToken.Type == TokenType.NEWLINE)
                {
                    Advance();
                    continue;
                }

                try
                {
                    var statement = ParseStatement();
                    statements.Add(statement);
                    SkipNewlines();
                }
                catch (PythonException)
                {
                    throw; // Re-throw PythonExceptions as-is (they already have location info)
                }
                catch (Exception ex)
                {
                    throw new PythonException("SyntaxError", $"Parse error: {ex.Message}", currentToken.Line, currentToken.Column);
                }
            }

            return statements;
        }

        private ASTNode ParseStatement()
        {
            return currentToken.Type switch
            {
                TokenType.DEF => ParseFunctionDef(),
                TokenType.CLASS => ParseClassDef(),
                TokenType.IF => ParseIf(),
                TokenType.FOR => ParseFor(),
                TokenType.WHILE => ParseWhile(),
                TokenType.TRY => ParseTry(),
                TokenType.WITH => ParseWith(),  // Added WITH statement parsing
                TokenType.DEL => ParseDel(),    // Added DEL statement parsing
                TokenType.IMPORT => ParseImport(),
                TokenType.FROM => ParseImport(), // FROM...IMPORT도 ParseImport에서 처리
                TokenType.RETURN => ParseReturn(),
                TokenType.BREAK => ParseBreak(),
                TokenType.CONTINUE => ParseContinue(),
                TokenType.RAISE => ParseRaise(),
                _ => ParseExpressionStatement()
            };
        }

        private ASTNode ParseFunctionDef()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;
            
            Expect(TokenType.DEF);
            string name = currentToken.Value;
            Expect(TokenType.IDENTIFIER);
            Expect(TokenType.LPAREN);

            SkipNewlinesAndIndents(); // Skip newlines after opening paren

            var parameters = new List<Parameter>();
            while (currentToken.Type != TokenType.RPAREN)
            {
                string paramName = currentToken.Value;
                Expect(TokenType.IDENTIFIER);

                TypeHint typeHint = null;
                if (currentToken.Type == TokenType.COLON)
                {
                    Advance(); // Skip ':'
                    typeHint = ParseTypeHint();
                }

                parameters.Add(new Parameter(paramName, typeHint));

                SkipNewlinesAndIndents(); // Skip newlines after parameter

                if (currentToken.Type == TokenType.COMMA)
                {
                    Advance(); // Skip comma
                    SkipNewlinesAndIndents(); // Skip newlines after comma

                    // Allow trailing comma
                    if (currentToken.Type == TokenType.RPAREN)
                        break;
                }
                else if (currentToken.Type != TokenType.RPAREN)
                {
                    throw new PythonException("SyntaxError", $"Expected ',' or ')' in parameter list", currentToken.Line, currentToken.Column);
                }
            }
            Expect(TokenType.RPAREN);

            TypeHint returnTypeHint = null;
            if (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "->")
            {
                Advance(); // Skip '->'
                returnTypeHint = ParseTypeHint();
            }

            Expect(TokenType.COLON);
            SkipNewlines();

            var body = ParseBlock();

            return new FunctionDefNode(name, parameters, body, returnTypeHint, line, column);
        }

        private TypeHint ParseTypeHint()
        {
            if (currentToken.Type != TokenType.IDENTIFIER)
                throw new PythonException("SyntaxError", $"Expected type hint", currentToken.Line, currentToken.Column);

            string typeName = currentToken.Value;
            Advance();

            var pythonType = typeName switch
            {
                "int" => PythonType.Int,
                "float" => PythonType.Float,
                "str" => PythonType.String,
                "bool" => PythonType.Boolean,
                "list" => PythonType.List,
                "dict" => PythonType.Dict,
                "tuple" => PythonType.Tuple,
                "None" => PythonType.None,
                _ => PythonType.Instance // For custom classes
            };

            // Check for generic types like list[int] or dict[str, int]
            if (currentToken.Type == TokenType.LBRACKET)
            {
                Advance(); // Skip '['
                SkipNewlinesAndIndents(); // Skip newlines after bracket

                var genericArgs = new List<TypeHint>();

                while (currentToken.Type != TokenType.RBRACKET)
                {
                    genericArgs.Add(ParseTypeHint());
                    SkipNewlinesAndIndents(); // Skip newlines after type

                    if (currentToken.Type == TokenType.COMMA)
                    {
                        Advance(); // Skip comma
                        SkipNewlinesAndIndents(); // Skip newlines after comma

                        // Allow trailing comma
                        if (currentToken.Type == TokenType.RBRACKET)
                            break;
                    }
                    else if (currentToken.Type != TokenType.RBRACKET)
                    {
                        throw new PythonException("SyntaxError", $"Expected ',' or ']' in generic type", currentToken.Line, currentToken.Column);
                    }
                }

                Expect(TokenType.RBRACKET);
                return new GenericTypeHint(pythonType, genericArgs);
            }

            return new SimpleTypeHint(pythonType);
        }

        private List<ASTNode> ParseBlock()
        {
            var statements = new List<ASTNode>();

            // INDENT 토큰을 기대함
            if (currentToken.Type == TokenType.INDENT)
            {
                Advance(); // Skip INDENT
                SkipNewlines();

                // DEDENT 토큰이 나올 때까지 문장들을 파싱
                while (currentToken.Type != TokenType.DEDENT &&
                       currentToken.Type != TokenType.EOF)
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
                    else
                    {
                        break;
                    }
                }

                // DEDENT 토큰 처리
                if (currentToken.Type == TokenType.DEDENT)
                {
                    Advance(); // Skip DEDENT
                }
            }
            else
            {
                // 들여쓰기가 없는 경우 - IndentationError를 발생시켜야 함
                // for, while, if, def, class 등의 다음 라인은 반드시 들여쓰기가 있어야 함
                throw new PythonException("IndentationError", "expected an indented block", currentToken.Line, currentToken.Column);
            }

            return statements;
        }

        private bool IsBlockStatement()
        {
            return currentToken.Type == TokenType.DEF ||
                   currentToken.Type == TokenType.CLASS ||
                   currentToken.Type == TokenType.IDENTIFIER ||
                   currentToken.Type == TokenType.NUMBER ||
                   currentToken.Type == TokenType.STRING ||
                   currentToken.Type == TokenType.FSTRING ||  // Added FSTRING
                   currentToken.Type == TokenType.BOOLEAN ||
                   currentToken.Type == TokenType.NONE ||
                   currentToken.Type == TokenType.LBRACKET ||
                   currentToken.Type == TokenType.LBRACE ||
                   currentToken.Type == TokenType.LPAREN ||
                   currentToken.Type == TokenType.IF ||
                   currentToken.Type == TokenType.FOR ||
                   currentToken.Type == TokenType.WHILE ||
                   currentToken.Type == TokenType.TRY ||
                   currentToken.Type == TokenType.WITH ||  // Added WITH
                   currentToken.Type == TokenType.RETURN ||
                   currentToken.Type == TokenType.BREAK ||
                   currentToken.Type == TokenType.CONTINUE ||
                   currentToken.Type == TokenType.RAISE ||
                   currentToken.Type == TokenType.IMPORT ||
                   currentToken.Type == TokenType.FROM ||
                   currentToken.Type == TokenType.NOT;
        }
    }
}