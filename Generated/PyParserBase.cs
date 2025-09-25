using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy.Generated
{
    /// <summary>
    /// Base class for all generated PEG parsers
    /// Contains common parsing logic to reduce generated code size
    /// </summary>
    public abstract partial class PyParserBase<TModule> where TModule : GeneratedAstNode
    {
        // Common fields
        protected readonly List<GeneratedTokenInfo> _tokens;
        protected internal int _position;
        protected readonly Dictionary<(int, string), object?> _memoCache = new();
        protected readonly string _filename;

        // Constructor
        protected PyParserBase(List<GeneratedTokenInfo> tokens, string filename = "<string>")
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _filename = filename;
            _position = 0;
        }

        // Common token access
        protected GeneratedTokenInfo? CurrentToken => _position < _tokens.Count ? _tokens[_position] : null;

        // Common parsing operations
        protected void Advance()
        {
            if (_position < _tokens.Count) _position++;
        }

        protected bool Expect(string expected)
        {
            if (CurrentToken?.Type.ToString() == expected)
            {
                Advance();
                return true;
            }
            return false;
        }

        protected bool ExpectKeyword(string keyword)
        {
            if (CurrentToken?.Type.ToString() == "NAME" && CurrentToken?.Value == keyword)
            {
                Advance();
                return true;
            }
            return false;
        }

        protected bool ExpectOperator(string op)
        {
            if (CurrentToken?.Type.ToString() == "OP" && CurrentToken?.Value == op)
            {
                Advance();
                return true;
            }
            return false;
        }

        protected object? ExpectTokenType(string tokenType)
        {
            if (CurrentToken?.Type.ToString() == tokenType)
            {
                var value = CurrentToken?.Value;
                Advance();
                return value;
            }
            return null;
        }

        // Backtracking support
        protected int Mark() => _position;
        protected void Reset(int mark) => _position = mark;

        // Memoization support
        protected T? GetMemo<T>(string ruleName)
        {
            var key = (_position, ruleName);
            return _memoCache.TryGetValue(key, out var value) ? (T?)value : default;
        }

        protected void SetMemo<T>(string ruleName, T? value)
        {
            var key = (_position, ruleName);
            _memoCache[key] = value;
        }

        // Generic parsing methods
        protected List<T> ParseZeroOrMore<T>(Func<T?> parseFunc) where T : class
        {
            var results = new List<T>();
            while (true)
            {
                var startPos = _position;
                var result = parseFunc();
                if (result == null)
                {
                    _position = startPos;
                    break;
                }
                results.Add(result);
            }
            return results;
        }

        protected List<T>? ParseOneOrMore<T>(Func<T?> parseFunc) where T : class
        {
            var results = ParseZeroOrMore(parseFunc);
            return results.Count > 0 ? results : null;
        }

        protected T? ParseOptional<T>(Func<T?> parseFunc) where T : class
        {
            var startPos = _position;
            var result = parseFunc();
            if (result == null)
            {
                _position = startPos;
            }
            return result;
        }

        protected T? ParseGroup<T>(Func<T?> parseFunc) where T : class
        {
            return parseFunc();
        }

        // End-of-file check
        protected bool AtEndOfFile()
        {
            return _position >= _tokens.Count || CurrentToken?.Type.ToString() == "ENDMARKER";
        }

        // Generic file parsing template
        protected TFileModule ParseFileTemplate<TFileModule>() where TFileModule : GeneratedAstNode, new()
        {
            var statements = ParseStatements();

            if (typeof(TFileModule) == typeof(GeneratedModule))
            {
                var module = new GeneratedModule { Body = statements as GeneratedStmtSeq };
                return (TFileModule)(object)module;
            }

            return new TFileModule();
        }

        // Generic statements parsing template
        protected GeneratedStmtSeq ParseStatementsTemplate()
        {
            var statementList = new List<GeneratedStmt>();

            // Parse one or more statements
            while (_position < _tokens.Count && CurrentToken?.Type.ToString() != "ENDMARKER")
            {
                var stmtSeq = ParseStatement(); // Abstract method to be implemented
                if (stmtSeq != null)
                {
                    if (stmtSeq is GeneratedStmt singleStmt)
                    {
                        statementList.Add(singleStmt);
                    }
                    else if (stmtSeq is GeneratedStmtSeq stmtList)
                    {
                        statementList.AddRange(stmtList);
                    }
                }
                else
                {
                    // Skip problematic tokens to avoid infinite loops
                    if (CurrentToken != null) Advance();
                }
            }

            var statements = new GeneratedStmtSeq();
            statements.AddRange(statementList);
            return statements;
        }

        // Template for the actual Statements parsing (used by generated parser)
        protected GeneratedStmtSeq ParseStatements()
        {
            return ParseStatementsTemplate();
        }

        // Generic comparison parsing template
        protected object? ParseComparisonTemplate()
        {
            var left = ParseSum();
            if (left == null) return null;

            // Handle chained comparison operations
            var ops = new List<string>();
            var comparators = new List<object>();

            while (true)
            {
                var mark = Mark();
                string? op = null;

                if (CurrentToken?.Type.ToString() == "OP")
                {
                    switch (CurrentToken?.Value)
                    {
                        case "==": op = "=="; break;
                        case "!=": op = "!="; break;
                        case "<": op = "<"; break;
                        case "<=": op = "<="; break;
                        case ">": op = ">"; break;
                        case ">=": op = ">="; break;
                        case "in": op = "in"; break;
                        case "is": op = "is"; break;
                    }
                }

                if (op != null)
                {
                    Advance(); // consume operator
                    var right = ParseSum();
                    if (right != null)
                    {
                        ops.Add(op);
                        comparators.Add(right);
                        Console.WriteLine($"[DEBUG] ParseComparison: Added {op} operator to chain");
                    }
                    else
                    {
                        Reset(mark); // backtrack on failure
                        break;
                    }
                }
                else
                {
                    Reset(mark); // no more comparison operators
                    break;
                }
            }

            // Create appropriate result
            if (ops.Count == 0)
            {
                // No comparison operators found, return the original expression
                return left;
            }
            else if (ops.Count == 1)
            {
                // Single comparison - create simple compare structure
                var result = new { type = "compare", op = ops[0], left = left, right = comparators[0] };
                Console.WriteLine($"[DEBUG] ParseComparison: Created single {ops[0]} comparison");
                return result;
            }
            else
            {
                // Chained comparison - create chained compare structure
                var result = new { type = "chained_compare", left = left, ops = ops.ToArray(), comparators = comparators.ToArray() };
                Console.WriteLine($"[DEBUG] ParseComparison: Created chained comparison with {ops.Count} operators");
                return result;
            }
        }

        // Common parsing methods - no need to generate these dynamically

        // sum: sum '+' term | sum '-' term | term
        protected object? ParseSum()
        {
            Console.WriteLine($"[DEBUG] ParseSum at position {_position}");

            var result = ParseTerm();
            if (result == null) return null;

            // Handle left recursion iteratively
            while (true)
            {
                var mark = Mark();

                if (CurrentToken?.Type.ToString() == "OP" && CurrentToken?.Value == "+")
                {
                    Advance(); // consume '+'
                    var right = ParseTerm();
                    if (right != null)
                    {
                        result = new { type = "binop", op = "+", left = result, right = right };
                        Console.WriteLine($"[DEBUG] ParseSum: Created addition");
                        continue;
                    }
                    Reset(mark);
                }

                if (CurrentToken?.Type.ToString() == "OP" && CurrentToken?.Value == "-")
                {
                    Advance(); // consume '-'
                    var right = ParseTerm();
                    if (right != null)
                    {
                        result = new { type = "binop", op = "-", left = result, right = right };
                        Console.WriteLine($"[DEBUG] ParseSum: Created subtraction");
                        continue;
                    }
                    Reset(mark);
                }

                // No more operations
                break;
            }

            Console.WriteLine($"[DEBUG] ParseSum result: {result?.GetType().Name}");
            return result;
        }

        // term: term '*' primary | term '/' primary | primary
        protected object? ParseTerm()
        {
            Console.WriteLine($"[DEBUG] ParseTerm at position {_position}");

            var result = ParsePrimary();
            if (result == null) return null;

            // Handle left recursion iteratively
            while (true)
            {
                var mark = Mark();

                if (CurrentToken?.Type.ToString() == "OP" && CurrentToken?.Value == "*")
                {
                    Advance(); // consume '*'
                    var right = ParsePrimary();
                    if (right != null)
                    {
                        result = new { type = "binop", op = "*", left = result, right = right };
                        Console.WriteLine($"[DEBUG] ParseTerm: Created multiplication");
                        continue;
                    }
                    Reset(mark);
                }

                if (CurrentToken?.Type.ToString() == "OP" && CurrentToken?.Value == "/")
                {
                    Advance(); // consume '/'
                    var right = ParsePrimary();
                    if (right != null)
                    {
                        result = new { type = "binop", op = "/", left = result, right = right };
                        Console.WriteLine($"[DEBUG] ParseTerm: Created division");
                        continue;
                    }
                    Reset(mark);
                }

                // No more operations
                break;
            }

            Console.WriteLine($"[DEBUG] ParseTerm result: {result?.GetType().Name}");
            return result;
        }

        // primary: primary '.' NAME | primary '(' [arguments] ')' | atom
        protected object? ParsePrimary()
        {
            Console.WriteLine($"[DEBUG] ParsePrimary at position {_position}");

            // Check memoization for left recursion
            var memoKey = "primary";
            var memoResult = GetMemo<object>(memoKey);
            if (memoResult != null)
            {
                Console.WriteLine($"[DEBUG] Primary: Using memoized result");
                return memoResult;
            }

            // Start with atom (base case)
            var result = ParseAtom();
            if (result == null)
            {
                SetMemo<object>(memoKey, null);
                return null;
            }

            // Iteratively expand with left-recursive rules
            bool expanded = true;
            while (expanded)
            {
                expanded = false;

                // Try: primary '(' [arguments] ')' (function call)
                if (CurrentToken?.Type.ToString() == "OP" && CurrentToken?.Value == "(")
                {
                    var mark = Mark();
                    Advance(); // consume '('

                    var args = new List<object>();
                    // Parse arguments (simplified)
                    while (CurrentToken != null && !(CurrentToken.Type.ToString() == "OP" && CurrentToken.Value == ")"))
                    {
                        var arg = ParseExpression();
                        if (arg != null)
                        {
                            args.Add(arg);
                            // Skip comma if present
                            if (CurrentToken?.Type.ToString() == "OP" && CurrentToken?.Value == ",")
                            {
                                Advance();
                            }
                        }
                        else break;
                    }

                    if (CurrentToken?.Type.ToString() == "OP" && CurrentToken?.Value == ")")
                    {
                        Advance(); // consume ')'
                        result = new { type = "call", func = result, args = args };
                        expanded = true;
                        Console.WriteLine($"[DEBUG] Primary: Created function call");
                    }
                    else
                    {
                        Reset(mark); // backtrack on failure
                    }
                }

                // Try: primary '.' NAME (attribute access)
                if (!expanded && CurrentToken?.Type.ToString() == "OP" && CurrentToken?.Value == ".")
                {
                    var mark = Mark();
                    Advance(); // consume '.'
                    if (CurrentToken?.Type.ToString() == "NAME")
                    {
                        var attr = CurrentToken.Value;
                        Advance();
                        result = new { type = "attribute", value = result, attr = attr };
                        expanded = true;
                        Console.WriteLine($"[DEBUG] Primary: Created attribute access");
                    }
                    else
                    {
                        Reset(mark); // backtrack on failure
                    }
                }
            }

            SetMemo<object>(memoKey, result);
            Console.WriteLine($"[DEBUG] Primary result: {result?.GetType().Name}");
            return result;
        }

        // atom: NAME | NUMBER | STRING | '(' expression ')'
        protected object? ParseAtom()
        {
            Console.WriteLine($"[DEBUG] ParseAtom at position {_position}: {CurrentToken?.Type}={CurrentToken?.Value}");

            if (CurrentToken == null) return null;

            // NAME
            if (CurrentToken.Type.ToString() == "NAME")
            {
                var name = CurrentToken.Value;
                Advance();
                return new { type = "name", value = name };
            }

            // NUMBER
            if (CurrentToken.Type.ToString() == "NUMBER")
            {
                var number = CurrentToken.Value;
                Advance();
                return new { type = "number", value = number };
            }

            // STRING
            if (CurrentToken.Type.ToString() == "STRING")
            {
                var str = CurrentToken.Value;
                Advance();
                return new { type = "string", value = str };
            }

            // '(' expression ')'
            if (CurrentToken.Type.ToString() == "OP" && CurrentToken.Value == "(")
            {
                Advance(); // consume '('
                var expr = ParseExpression();
                if (expr != null && CurrentToken?.Type.ToString() == "OP" && CurrentToken?.Value == ")")
                {
                    Advance(); // consume ')'
                    return expr;
                }
                return null; // Malformed parenthesized expression
            }

            return null;
        }

        // expression: comparison (operations with proper precedence)
        protected object? ParseExpression()
        {
            return ParseComparisonTemplate();
        }

        // Common statement parsing template
        protected object? ParseStatement()
        {
            Console.WriteLine($"[DEBUG] ParseStatement at position {_position}");

            // Skip NEWLINE tokens
            while (CurrentToken?.Type.ToString() == "NEWLINE")
            {
                Advance();
            }

            if (CurrentToken == null) return null;

            // Check for compound statements first (if, while, for, etc.)
            if (CurrentToken.Type.ToString() == "NAME")
            {
                var tokenValue = CurrentToken.Value?.ToString();
                if (tokenValue == "if")
                {
                    var ifStmt = ParseIfStatement();
                    if (ifStmt != null) return ifStmt;
                }
                // Add more compound statements here as needed
            }

            // Try simple statements
            return ParseSimpleStatement();
        }

        // Template for simple statement parsing (assignment, expression, etc.)
        protected object? ParseSimpleStatement()
        {
            // Try assignment first (NAME '=' expr)
            var mark = Mark();
            if (CurrentToken?.Type.ToString() == "NAME")
            {
                var namePos = _position;
                Advance(); // consume NAME

                if (CurrentToken?.Type.ToString() == "OP" && CurrentToken?.Value == "=")
                {
                    Reset(namePos); // go back to start
                    return ParseAssignment();
                }

                Reset(mark); // backtrack if not assignment
            }

            // Try expression statement
            var expr = ParseExpression();
            if (expr != null)
            {
                // Return GeneratedStmt for compatibility with interpreter
                var stmt = new GeneratedStmt();
                stmt.StatementType = "expression";
                stmt.Value = expr;
                return stmt;
            }

            return null;
        }

        // Assignment parsing template
        protected object? ParseAssignment()
        {
            if (CurrentToken?.Type.ToString() != "NAME") return null;

            var targetName = CurrentToken.Value;
            Advance(); // consume NAME

            if (CurrentToken?.Type.ToString() != "OP" || CurrentToken?.Value != "=") return null;
            Advance(); // consume '='

            var value = ParseExpression();
            if (value == null) return null;

            // Return GeneratedStmt for compatibility with interpreter
            var stmt = new GeneratedStmt();
            stmt.StatementType = "assignment";
            stmt.Value = new { Target = targetName, Value = value };
            return stmt;
        }

        // If statement parsing template
        protected object? ParseIfStatement()
        {
            if (CurrentToken?.Type.ToString() != "NAME" || CurrentToken?.Value != "if") return null;

            Advance(); // consume 'if'

            var condition = ParseExpression();
            if (condition == null) return null;

            if (CurrentToken?.Type.ToString() != "OP" || CurrentToken?.Value != ":") return null;
            Advance(); // consume ':'

            // Skip NEWLINE and INDENT
            while (CurrentToken != null && (CurrentToken.Type.ToString() == "NEWLINE" || CurrentToken.Type.ToString() == "INDENT"))
            {
                Advance();
            }

            var body = new List<object>();
            var bodyStmt = ParseStatement();
            if (bodyStmt != null)
            {
                body.Add(bodyStmt);
            }

            // Skip DEDENT
            if (CurrentToken?.Type.ToString() == "DEDENT")
            {
                Advance();
            }

            return new { type = "if", condition = condition, body = body };
        }

        // Entry point
        public abstract TModule Parse();

        // Embedded PEG Interpreter and supporting classes (moved from generated parser)
        public interface IEmbeddedTokenInfo
        {
            object Type { get; }
            string Value { get; }
            int Line { get; }
            int Column { get; }
        }

        public class EmbeddedTokenInfoAdapter : IEmbeddedTokenInfo
        {
            private readonly object _token;

            public EmbeddedTokenInfoAdapter(object token)
            {
                _token = token;
            }

            public object Type => _token.GetType().GetProperty("Type")?.GetValue(_token);
            public string Value => _token.GetType().GetProperty("Value")?.GetValue(_token)?.ToString() ?? "";
            public int Line => (int)(_token.GetType().GetProperty("Line")?.GetValue(_token) ?? 0);
            public int Column => (int)(_token.GetType().GetProperty("Column")?.GetValue(_token) ?? 0);
        }

        public class EmbeddedGrammar
        {
            // Hard-coded Python grammar rules for basic assignment
            public Dictionary<string, string> Rules { get; } = new()
            {
                { "assignment", "NAME '=' expr" },
                { "expr", "NUMBER | NAME" },
                { "simple_stmt", "assignment" },
                { "stmt", "simple_stmt" }
            };
        }

        protected class EmbeddedPegInterpreter
        {
            private readonly EmbeddedGrammar _grammar;
            private readonly List<IEmbeddedTokenInfo> _tokens;
            private readonly PyParserBase<TModule> _parser;

            public EmbeddedPegInterpreter(EmbeddedGrammar grammar, List<IEmbeddedTokenInfo> tokens, PyParserBase<TModule> parser)
            {
                _grammar = grammar;
                _tokens = tokens;
                _parser = parser;
            }

            public object ParseRule(string ruleName)
            {
                switch (ruleName)
                {
                    case "file":
                        return ParseFile();
                    case "assignment":
                        return _parser.ParseAssignment();  // Use base class method
                    case "expr":
                        return ParseExpr();
                    case "simple_stmt":
                        return ParseSimpleStmt();
                    case "stmt":
                        return ParseStmt();
                    case "expression_stmt":
                        return ParseExpressionStmt();
                    case "call":
                        return ParseExpressionStmt();
                    default:
                        return null;
                }
            }

            private object ParseFile()
            {
                // Parse: statements ENDMARKER
                Console.WriteLine($"[DEBUG] ParseFile starting: {_parser._position} / {_tokens.Count}");
                var statements = new List<object>();
                while (_parser._position < _tokens.Count && _tokens[_parser._position].Type.ToString() != "ENDMARKER")
                {
                    Console.WriteLine($"[DEBUG] ParseFile loop: position={_parser._position}, token={_tokens[_parser._position].Type}:{_tokens[_parser._position].Value}");
                    // Skip NEWLINE tokens
                    if (_tokens[_parser._position].Type.ToString() == "NEWLINE")
                    {
                        _parser._position++;
                        continue;
                    }

                    var initialPos = _parser._position;
                    var stmt = ParseStmt();
                    Console.WriteLine($"[DEBUG] ParseStmt returned: {(stmt != null ? stmt.GetType().Name : "null")}");
                    if (stmt != null)
                    {
                        statements.Add(stmt);
                        // Ensure position advanced after successful parsing
                        if (_parser._position == initialPos)
                        {
                            Console.WriteLine($"[DEBUG] Warning: ParseStmt succeeded but position not advanced, forcing advance");
                            _parser._position++;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] ParseStmt failed, advancing position to prevent infinite loop");
                        _parser._position++; // Skip unknown token
                    }
                }

                Console.WriteLine($"[DEBUG] ParseFile collected {statements.Count} statements");
                var module = new GeneratedModule();
                module.Body = new GeneratedStmtSeq();
                foreach (var stmt in statements)
                {
                    if (stmt is GeneratedStmt generatedStmt)
                    {
                        Console.WriteLine($"[DEBUG] Adding statement: {generatedStmt.StatementType}");
                        module.Body.Add(generatedStmt);
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] Skipping non-GeneratedStmt: {stmt?.GetType().Name}");
                    }
                }
                return module;
            }

            private object ParseExpr()
            {
                // Use the full expression parser instead of only handling atoms
                Console.WriteLine($"[DEBUG] ParseExpr: Using full expression parser");
                return _parser.ParseExpression();
            }

            private object ParseSimpleStmt()
            {
                // Try assignment first
                var assignment = _parser.ParseAssignment();
                if (assignment != null) return assignment;

                // Try expression statement (function calls, etc.)
                var expressionStmt = ParseExpressionStmt();
                if (expressionStmt != null) return expressionStmt;

                return null;
            }

            private object ParseStmt()
            {
                return _parser.ParseStatement();
            }

            private object? ParseExpressionStmt()
            {
                Console.WriteLine($"[DEBUG] ParseExpressionStmt at position {_parser._position}");

                var expr = _parser.ParseExpression();
                if (expr != null)
                {
                    var stmt = new GeneratedStmt();
                    stmt.StatementType = "expression";
                    stmt.Value = expr;
                    Console.WriteLine($"[DEBUG] Created expression statement: {expr}");
                    return stmt;
                }
                return null;
            }
        }

        protected EmbeddedGrammar CreateEmbeddedGrammar()
        {
            return new EmbeddedGrammar();
        }
    }
}