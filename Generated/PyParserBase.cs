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

        // Left recursion support - based on Warth et al. "Packrat parsers can support left recursion"
        protected T? TryLeftRecursive<T>(string ruleName, Func<T?> parseMethod) where T : class
        {
            var key = (_position, ruleName);
            var startPos = _position;

            Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} at position {startPos}");

            // Check if we're already in a recursive call at this position for this rule
            if (_memoCache.TryGetValue(key, out var existing) && existing == null)
            {
                // We're in a recursive call - return null to break initial recursion
                Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} recursive call detected, breaking");
                return null;
            }

            // Step 1: Mark as being processed (seed with failure)
            SetMemo(ruleName, (T?)null);

            // Step 2: Try initial parse (seed parse)
            Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} attempting seed parse");
            var lastResult = parseMethod();
            if (lastResult == null)
            {
                // No seed parse succeeded, keep the null memo and return null
                Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} seed parse failed");
                return null;
            }

            var lastEndPos = _position;
            Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} seed parse succeeded, position {startPos} -> {lastEndPos}");
            SetMemo(ruleName, lastResult);

            // Step 3: Expansion loop - keep trying to get longer parses
            int loopCount = 0;
            const int MAX_EXPANSION_ATTEMPTS = 100; // Safety limit
            while (loopCount < MAX_EXPANSION_ATTEMPTS)
            {
                loopCount++;
                Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} expansion attempt {loopCount}");

                // Reset to start position for next attempt
                Reset(startPos);

                var result = parseMethod();
                var endPos = _position;

                Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} expansion result: {(result != null ? "success" : "null")}, position {startPos} -> {endPos} (last was {lastEndPos})");

                // Step 4: Termination condition - no progress made or result is null
                if (result == null)
                {
                    Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} result is null, terminating loop");
                    break;
                }

                // Critical fix: position must advance to continue expansion
                if (endPos <= lastEndPos)
                {
                    Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} no position advancement ({endPos} <= {lastEndPos}), terminating loop");
                    break;
                }

                // Progress made - update the cached result
                lastResult = result;
                lastEndPos = endPos;
                SetMemo(ruleName, lastResult);
            }

            if (loopCount >= MAX_EXPANSION_ATTEMPTS)
            {
                Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} hit safety limit, terminating");
            }

            // Reset to the final successful position
            Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} final result at position {lastEndPos}");
            Reset(lastEndPos);
            return lastResult;
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
            var parser = this as GeneratedPyParser;
            var statements = parser?.ParseStatements();

            if (typeof(TFileModule) == typeof(GeneratedModule))
            {
                var stmtSeq = new GeneratedStmtSeq();
                if (statements != null)
                {
                    foreach (var stmt in statements)
                    {
                        if (stmt is GeneratedStmt genStmt)
                            stmtSeq.Add(genStmt);
                    }
                }
                var module = new GeneratedModule { Body = stmtSeq };
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
                // Skip NEWLINE tokens before parsing statements (NL tokens don't exist)
                while (CurrentToken?.Type.ToString() == "NEWLINE")
                {
                    Advance();
                }

                // Check if we've reached the end after skipping tokens
                if (_position >= _tokens.Count || CurrentToken?.Type.ToString() == "ENDMARKER")
                    break;

                var parser = this as GeneratedPyParser;
                var stmtSeq = parser?.ParseStatement(); // Use generated parser's method
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

        // Removed ParseStatements() - using generated parser's implementation instead

        // Generic comparison parsing template - will be replaced by generated ParseComparison
        protected object? ParseComparisonTemplate()
        {
            // This is a temporary placeholder - the generated parser will have ParseComparison
            return null;
        }

        // Common parsing methods - no need to generate these dynamically






        // Removed ParseStatement() - using generated parser's implementation instead

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
            var expr = (object?)null; // ParseExpression(); // Will use generated method
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

            var parser = this as GeneratedPyParser;
            var value = parser?.ParseExpression();
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

            var condition = (object?)null; // ParseExpression(); // Will use generated method
            if (condition == null) return null;

            if (CurrentToken?.Type.ToString() != "OP" || CurrentToken?.Value != ":") return null;
            Advance(); // consume ':'

            // Skip NEWLINE and INDENT
            while (CurrentToken != null && (CurrentToken.Type.ToString() == "NEWLINE" || CurrentToken.Type.ToString() == "INDENT"))
            {
                Advance();
            }

            var body = new List<object>();
            var parser = this as GeneratedPyParser;
            var bodyStmt = parser?.ParseStatement();
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
            // Grammar rules will be dynamically generated from python.gram
            public Dictionary<string, string> Rules { get; set; } = new();

            public EmbeddedGrammar(Dictionary<string, string> rules)
            {
                Rules = rules ?? new Dictionary<string, string>();
            }
        }



        // ========================================
        // Parser Helper Methods
        // ========================================

        /// <summary>
        /// Expect a keyword token
        /// </summary>
        protected bool ExpectKeyword(string keyword)
        {
            if (CurrentToken?.Type == GeneratedTokenType.NAME && CurrentToken.Value == keyword)
            {
                Advance();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Expect a specific token type and value
        /// </summary>
        protected bool ExpectToken(GeneratedTokenType tokenType, string value = null)
        {
            if (CurrentToken?.Type == tokenType)
            {
                if (value == null || CurrentToken.Value == value)
                {
                    Advance();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Expect and return a NAME token
        /// </summary>
        protected string ExpectName()
        {
            if (CurrentToken?.Type == GeneratedTokenType.NAME)
            {
                var name = CurrentToken.Value;
                Advance();
                return name;
            }
            return null;
        }

        // ========================================
        // Python C Function Helpers (_PyAST_* and _PyPegen_*)
        // ========================================

        /// <summary>
        /// _PyAST_FunctionDef - Create function definition AST node
        /// </summary>
        protected GeneratedStmt _PyAST_FunctionDef(string name, object arguments, object body,
            object decorator_list = null, object returns = null, string type_comment = null,
            object type_params = null)
        {
            Console.WriteLine($"[DEBUG] _PyAST_FunctionDef: Creating function '{name}' with body type: {body?.GetType().Name}");

            var funcDef = new GeneratedStmt();
            funcDef.StatementType = "function_def";

            var funcInfo = new GeneratedFunctionDef
            {
                Name = name,
                Arguments = arguments ?? _PyPegen_empty_arguments(),
                Body = body,
                DecoratorList = decorator_list ?? new List<object>(),
                Returns = returns,
                TypeComment = type_comment,
                TypeParams = type_params
            };

            funcDef.Value = funcInfo;
            Console.WriteLine($"[DEBUG] _PyAST_FunctionDef: Function definition created successfully");
            return funcDef;
        }

        /// <summary>
        /// _PyPegen_make_module - Create module from statements
        /// </summary>
        protected GeneratedModule _PyPegen_make_module(object statements)
        {
            var module = new GeneratedModule();
            module.Body = new GeneratedStmtSeq();

            if (statements is List<object> stmtList)
            {
                foreach (var stmt in stmtList)
                {
                    if (stmt is GeneratedStmt genStmt)
                    {
                        module.Body.Add(genStmt);
                    }
                }
            }
            else if (statements is GeneratedStmt singleStmt)
            {
                module.Body.Add(singleStmt);
            }

            return module;
        }

        /// <summary>
        /// _PyPegen_singleton_seq - Create sequence with single item
        /// </summary>
        protected List<object> _PyPegen_singleton_seq(object item)
        {
            if (item == null) return new List<object>();
            return new List<object> { item };
        }

        /// <summary>
        /// _PyPegen_seq_flatten - Flatten sequence of sequences
        /// </summary>
        protected List<object> _PyPegen_seq_flatten(List<object> sequences)
        {
            var result = new List<object>();
            foreach (var seq in sequences)
            {
                if (seq is List<object> subList)
                {
                    result.AddRange(subList);
                }
                else if (seq != null)
                {
                    result.Add(seq);
                }
            }
            return result;
        }

        /// <summary>
        /// _PyPegen_empty_arguments - Create empty argument list
        /// </summary>
        protected object _PyPegen_empty_arguments()
        {
            return new Dictionary<string, object>
            {
                ["posonlyargs"] = new List<object>(),
                ["args"] = new List<object>(),
                ["vararg"] = null,
                ["kwonlyargs"] = new List<object>(),
                ["kw_defaults"] = new List<object>(),
                ["kwarg"] = null,
                ["defaults"] = new List<object>()
            };
        }

        /// <summary>
        /// _PyAST_Pass - Create pass statement
        /// </summary>
        protected GeneratedStmt _PyAST_Pass()
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "pass";
            stmt.Value = null;
            return stmt;
        }

        /// <summary>
        /// _PyAST_Return - Create return statement
        /// </summary>
        protected GeneratedStmt _PyAST_Return(object value)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "return";
            stmt.Value = value;
            return stmt;
        }

        /// <summary>
        /// _PyAST_Expr - Create expression statement
        /// </summary>
        protected GeneratedStmt _PyAST_Expr(object value)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "expression";
            stmt.Value = value;
            return stmt;
        }

        /// <summary>
        /// _PyAST_Constant - Create constant expression
        /// </summary>
        protected GeneratedExpr _PyAST_Constant(object value)
        {
            var expr = new GeneratedExpr();
            expr.ExpressionType = "constant";
            expr.Value = value;
            return expr;
        }

        /// <summary>
        /// _PyAST_Assign - Create assignment statement
        /// </summary>
        protected GeneratedStmt _PyAST_Assign(object target, object value)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "assignment";
            stmt.Value = new { Target = target, Value = value };
            return stmt;
        }

    }

    // ========================================
    // Strongly-typed AST Node Classes
    // ========================================

    /// <summary>
    /// Strongly-typed function definition AST node
    /// </summary>
    public class GeneratedFunctionDef
    {
        public string Name { get; set; } = "";
        public object Arguments { get; set; } = null!;
        public object Body { get; set; } = null!;
        public object DecoratorList { get; set; } = null!;
        public object Returns { get; set; } = null!;
        public string? TypeComment { get; set; }
        public object? TypeParams { get; set; }
    }

    /// <summary>
    /// Strongly-typed class definition AST node
    /// </summary>
    public class GeneratedClassDef
    {
        public string Name { get; set; } = "";
        public object Body { get; set; } = null!;
        public object Bases { get; set; } = null!;
        public object Keywords { get; set; } = null!;
        public object DecoratorList { get; set; } = null!;
        public string? TypeComment { get; set; }
        public object? TypeParams { get; set; }
    }
}