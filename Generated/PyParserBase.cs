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

        // CPython 3.12: PegInterpreter for grammar-based parsing
        // Concrete type is defined in GeneratedPyParser via property
        protected abstract object? InterpreterObject { get; }

        // Context management for CPython 3.12 compatibility
        protected readonly Stack<ParserContext> _contextStack = new();
        protected int _indentLevel = 0;

        // Constructor
        protected PyParserBase(List<GeneratedTokenInfo> tokens, string filename = "<string>")
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _filename = filename;
            _position = 0;

            // Initialize with module context
            _contextStack.Push(new ParserContext(ContextType.Module, 0, 0, "<module>"));
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

        // Assignment parsing template - CPython 3.12 chained assignment and augmented assignment support
        // Grammar:
        //   | a[asdl_expr_seq*]=(z=star_targets '=' { z })+ b=(yield_expr | star_expressions) !'='
        //   | a=single_target b=augassign ~ c=(yield_expr | star_expressions)
        protected GeneratedStmt? ParseAssignment()
        {
            var mark = Mark();
            var parser = this as GeneratedPyParser;

            // CPython 3.12: Try augmented assignment FIRST (single_target augassign value)
            // This matches: i += 1, x *= 2, etc.
            var augassignMark = Mark();
            var augTarget = parser?.ParseExpression();

            if (augTarget != null)
            {
                // Check for augmented assignment operators: +=, -=, *=, /=, etc.
                string? augOp = TryMatchAugAssignOp();
                if (augOp != null)
                {
                    // Matched augmented assignment operator
                    Advance(); // consume the operator

                    // Parse the value (right side)
                    var augValue = parser?.ParseExpression();
                    if (augValue != null)
                    {
                        // CPython 3.12: Set Store context on target
                        SetExprContext(augTarget, "Store");

                        // Create AugAssign statement
                        return _PyAST_AugAssign(augTarget, augOp, augValue);
                    }
                }
            }

            // Augmented assignment failed, try regular assignment
            Reset(mark);
            var targets = new List<object>();

            // Parse one or more "target =" patterns
            while (true)
            {
                var targetMark = Mark();
                var target = parser?.ParseExpression();

                if (target == null)
                {
                    Reset(targetMark);
                    break;
                }

                // Check for '=' after target (exact match, not augmented)
                if (CurrentToken?.Type == GeneratedTokenType.OP && CurrentToken?.Value == "=")
                {
                    targets.Add(target);
                    Advance(); // consume '='

                    // Check if next token is NOT '=' (to avoid matching == comparison)
                    if (CurrentToken?.Type == GeneratedTokenType.OP && CurrentToken?.Value == "=")
                    {
                        // This is '==' comparison, not assignment - rollback
                        Reset(targetMark);
                        targets.RemoveAt(targets.Count - 1);
                        break;
                    }
                }
                else
                {
                    // No '=' found, not an assignment
                    Reset(targetMark);
                    break;
                }
            }

            // Must have at least one target
            if (targets.Count == 0)
            {
                Reset(mark);
                return null;
            }

            // Parse the value (right side of assignment)
            var value = parser?.ParseExpression();
            if (value == null)
            {
                Reset(mark);
                return null;
            }

            // Check that we don't have another '=' (to avoid a = b = c = syntax errors)
            if (CurrentToken?.Type == GeneratedTokenType.OP && CurrentToken?.Value == "=")
            {
                // Next is '=', this might be part of a longer chain - let it be parsed
                // Actually, we should have captured all of them in the loop above
                // If we're here, it means there's a syntax error or the loop logic needs review
            }

            // CPython 3.12: Set Store context on all assignment targets
            SetExprContextRecursive(targets, "Store");

            // Create chained assignment statement
            return _PyAST_Assign(targets, value);
        }

        /// <summary>
        /// CPython 3.12: Check if current token is an augmented assignment operator
        /// Returns the operator name if matched, null otherwise
        /// Grammar: augassign: '+=' | '-=' | '*=' | '@=' | '/=' | '%=' | '&=' | '|=' | '^=' | '<<=' | '>>=' | '**=' | '//='
        /// </summary>
        private string? TryMatchAugAssignOp()
        {
            if (CurrentToken?.Type != GeneratedTokenType.OP)
                return null;

            var op = CurrentToken.Value;

            // CPython 3.12: All augmented assignment operators
            switch (op)
            {
                case "+=": return "Add";
                case "-=": return "Sub";
                case "*=": return "Mult";
                case "@=": return "MatMult";
                case "/=": return "Div";
                case "%=": return "Mod";
                case "&=": return "BitAnd";
                case "|=": return "BitOr";
                case "^=": return "BitXor";
                case "<<=": return "LShift";
                case ">>=": return "RShift";
                case "**=": return "Pow";
                case "//=": return "FloorDiv";
                default: return null;
            }
        }

        /// <summary>
        /// CPython 3.12: Create AugAssign statement
        /// _PyAST_AugAssign(target, op, value, EXTRA)
        /// </summary>
        protected GeneratedStmt _PyAST_AugAssign(object target, string op, object value)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "aug_assign";  // Match existing converter code

            // Create augassign data structure matching existing format
            var augAssignData = new
            {
                Target = target,
                Op = new { kind = op },  // Wrap operator in anonymous object with 'kind' field
                Value = value
            };
            stmt.Value = augAssignData;

            return stmt;
        }

        /// <summary>
        /// CPython 3.12: Set expression context recursively (Store, Load, Del)
        /// Equivalent to _PyPegen_set_expr_context in CPython's pegen
        /// </summary>
        protected void SetExprContextRecursive(List<object> exprs, string context)
        {
            foreach (var expr in exprs)
            {
                SetExprContext(expr, context);
            }
        }

        /// <summary>
        /// CPython 3.12: Set expression context on a single expression
        /// </summary>
        protected void SetExprContext(object expr, string context)
        {
            if (expr is GeneratedExpr genExpr)
            {
                genExpr.Context = context;

                // Handle Expression wrapper - unwrap and set context on inner expression
                if (genExpr.ExpressionType == "Expression" && genExpr.Value is object innerExpr)
                {
                    SetExprContext(innerExpr, context);
                    return;
                }

                // Recursively set context for nested expressions
                if (genExpr.Value is object valueObj)
                {
                    // Handle Name expression - this is the actual target
                    if (genExpr.ExpressionType == "Name")
                    {
                        // Name expression itself already has context set above
                        return;
                    }

                    // Handle List/Tuple elements
                    var elemsProperty = valueObj.GetType().GetProperty("elts");
                    if (elemsProperty != null)
                    {
                        var elements = elemsProperty.GetValue(valueObj);
                        if (elements is System.Collections.IEnumerable enumerable)
                        {
                            foreach (var elem in enumerable)
                            {
                                if (elem != null)
                                {
                                    SetExprContext(elem, context);
                                }
                            }
                        }
                    }

                    // Handle Starred value
                    var valueProperty = valueObj.GetType().GetProperty("value");
                    if (valueProperty != null && genExpr.ExpressionType == "Starred")
                    {
                        var starredValue = valueProperty.GetValue(valueObj);
                        if (starredValue != null)
                        {
                            SetExprContext(starredValue, context);
                        }
                    }
                }
            }
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
        /// Check if a value is a Python keyword
        /// CPython 3.12: Keywords cannot be used as identifiers
        /// </summary>
        protected bool IsKeyword(string value)
        {
            // CPython 3.12 keyword list
            var keywords = new System.Collections.Generic.HashSet<string>
            {
                "and", "as", "assert", "async", "await", "break", "case", "class", "continue",
                "def", "del", "elif", "else", "except", "False", "finally", "for", "from",
                "global", "if", "import", "in", "is", "lambda", "match", "None", "nonlocal",
                "not", "or", "pass", "raise", "return", "True", "try", "type", "while", "with", "yield"
            };
            return keywords.Contains(value);
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
        /// Parse a block of statements (INDENT statements DEDENT)
        /// </summary>
        protected List<object>? ParseBlock()
        {
            Console.WriteLine($"[DEBUG] ParseBlock: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'");

            var statements = new List<object>();

            // Skip NEWLINE before INDENT
            if (CurrentToken?.Type == GeneratedTokenType.NEWLINE)
            {
                Advance();
            }

            // Expect INDENT
            if (!ExpectToken(GeneratedTokenType.INDENT))
            {
                Console.WriteLine($"[DEBUG] ParseBlock: Failed to find INDENT at position {_position}");
                return null;
            }

            // Parse statements until DEDENT
            while (_position < _tokens.Count &&
                   CurrentToken?.Type != GeneratedTokenType.DEDENT &&
                   CurrentToken?.Type != GeneratedTokenType.ENDMARKER)
            {
                Console.WriteLine($"[DEBUG] ParseBlock: Loop iteration at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'");

                var startPos = _position;
                var parser = this as GeneratedPyParser;
                var stmt = parser?.ParseStatement();

                Console.WriteLine($"[DEBUG] ParseBlock: After ParseStatement, position {startPos} -> {_position}, stmt = {stmt?.GetType()?.Name}");

                if (stmt != null)
                {
                    if (stmt is List<object> stmtList)
                        statements.AddRange(stmtList);
                    else
                        statements.Add(stmt);
                }
                else
                {
                    // Skip tokens that couldn't be parsed to avoid infinite loop
                    if (CurrentToken?.Type == GeneratedTokenType.NEWLINE || CurrentToken?.Type == GeneratedTokenType.NL)
                    {
                        Advance();
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] ParseBlock: Skipping unparsed token at position {_position}: {CurrentToken?.Type} '{CurrentToken?.Value}'");
                        Advance();
                    }
                }

                // Safety check: if we didn't advance, break to avoid infinite loop
                if (_position == startPos && stmt == null)
                {
                    Console.WriteLine($"[DEBUG] ParseBlock: No advancement, breaking at position {_position}");
                    break;
                }

                // Check if we hit DEDENT after statement parsing
                if (CurrentToken?.Type == GeneratedTokenType.DEDENT)
                {
                    Console.WriteLine($"[DEBUG] ParseBlock: Found DEDENT at position {_position}, breaking");
                    break;
                }
            }

            // Expect DEDENT to close the block
            if (CurrentToken?.Type == GeneratedTokenType.DEDENT)
            {
                Advance();
            }

            Console.WriteLine($"[DEBUG] ParseBlock: Returning result with {statements.Count} statements at position {_position}");
            return statements;
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
            Console.WriteLine($"[DEBUG] _PyAST_FunctionDef: arguments parameter is null: {arguments == null}");
            if (arguments != null)
            {
                Console.WriteLine($"[DEBUG] _PyAST_FunctionDef: arguments type: {arguments.GetType().Name}");
                Console.WriteLine($"[DEBUG] _PyAST_FunctionDef: arguments value: {arguments}");
            }

            var funcDef = new GeneratedStmt();
            funcDef.StatementType = "function_def";

            var finalArguments = arguments ?? _PyPegen_empty_arguments();
            Console.WriteLine($"[DEBUG] _PyAST_FunctionDef: Using {(arguments == null ? "empty" : "provided")} arguments");

            var funcInfo = new GeneratedFunctionDef
            {
                Name = name,
                Arguments = finalArguments,
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
        /// _PyAST_AsyncFunctionDef - Create async function definition AST node
        /// </summary>
        protected GeneratedStmt _PyAST_AsyncFunctionDef(string name, object arguments, object body,
            object decorator_list = null, object returns = null, string type_comment = null,
            object type_params = null)
        {
            Console.WriteLine($"[DEBUG] _PyAST_AsyncFunctionDef: Creating async function '{name}' with body type: {body?.GetType().Name}");

            var funcDef = new GeneratedStmt();
            funcDef.StatementType = "async_function_def";

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
            Console.WriteLine($"[DEBUG] _PyAST_AsyncFunctionDef: Async function definition created successfully");
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
        /// _PyPegen_seq_flatten - Flatten statement sequences (CPython 3.12 compatible)
        /// CPython 3.12: NEVER flatten - preserve nested structure exactly as parsed
        /// Only used for simple_stmts sequences (semicolon-separated statements)
        /// </summary>
        protected List<object> _PyPegen_seq_flatten(List<object> sequences)
        {
            // CRITICAL: CPython 3.12 does NOT flatten compound statement bodies
            // Return sequences as-is to preserve nested structure
            var result = new List<object>();
            foreach (var seq in sequences)
            {
                if (seq != null)
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
        /// _PyAST_Assign - Create assignment statement (single target)
        /// </summary>
        protected GeneratedStmt _PyAST_Assign(object target, object value)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "assignment";
            stmt.Value = new { Target = target, Value = value };
            return stmt;
        }

        /// <summary>
        /// _PyAST_Assign - Create assignment statement (multiple targets for chained assignment)
        /// CPython 3.12: a = b = c = value
        /// </summary>
        protected GeneratedStmt _PyAST_Assign(List<object> targets, object value)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "assignment";
            stmt.Value = new { Targets = targets, Value = value };
            return stmt;
        }

        protected GeneratedStmt _PyAST_AnnAssign(object target, object annotation, object value = null)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "annassign";
            stmt.Value = new { target = target, annotation = annotation, value = value };
            return stmt;
        }

        /// <summary>
        /// _PyAST_AnnAssign - Create annotated assignment statement
        /// </summary>
        protected GeneratedStmt _PyAST_AnnAssign(object target, object annotation, object value = null, int simple = 1)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "ann_assign";
            stmt.Value = new { Target = target, Annotation = annotation, Value = value, Simple = simple };
            return stmt;
        }

        /// <summary>
        /// _PyAST_AugAssign - Create augmented assignment statement (+=, -=, etc.)
        /// </summary>
        protected GeneratedStmt _PyAST_AugAssign(object target, object op, object value)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "aug_assign";
            stmt.Value = new { Target = target, Op = op, Value = value };
            return stmt;
        }

        /// <summary>
        /// _PyPegen_augoperator - Create augmented assignment operator
        /// </summary>
        protected object _PyPegen_augoperator(object operatorType)
        {
            return new { Type = "operator", Value = operatorType };
        }

        /// <summary>
        /// _PyPegen_set_expr_context - Set expression context (Load, Store, Del)
        /// </summary>
        protected object _PyPegen_set_expr_context(object expr, object context)
        {
            if (expr is GeneratedExpr genExpr)
            {
                genExpr.Context = context?.ToString();
                return genExpr;
            }
            return expr;
        }

        /// <summary>
        /// _PyAST_Name - Create name expression (CPython 3.12 compatible)
        /// CPython: Name(id='x', ctx=Load())
        /// </summary>
        protected GeneratedExpr _PyAST_Name(string id, object context = null)
        {
            var expr = new GeneratedExpr();
            expr.ExpressionType = "Name";  // CPython 3.12: Capital 'N'
            expr.Value = new { id = id };  // CPython 3.12: anonymous object with 'id' field
            expr.Context = context?.ToString() ?? "Load";
            return expr;
        }

        /// <summary>
        /// _PyAST_If - Create if statement
        /// </summary>
        protected GeneratedStmt _PyAST_If(object test, object body, object orelse = null)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "if";
            stmt.Value = new { Test = test, Body = body, Orelse = orelse ?? new List<object>() };
            return stmt;
        }

        /// <summary>
        /// _PyAST_While - Create while statement
        /// </summary>
        protected GeneratedStmt _PyAST_While(object test, object body, object orelse = null)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "while";
            stmt.Value = new { Test = test, Body = body, Orelse = orelse ?? new List<object>() };
            return stmt;
        }

        /// <summary>
        /// _PyAST_For - Create for statement
        /// </summary>
        /// <summary>
        /// CPython 3.12: _PyAST_For(target, iter, body, orelse, type_comment, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// SharpPy: simplified version (lineno/col_offset/arena handled separately)
        /// </summary>
        protected GeneratedStmt _PyAST_For(object target, object iter, object body, object orelse = null, object type_comment = null)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "for";
            stmt.Value = new { Target = target, Iter = iter, Body = body, Orelse = orelse ?? new List<object>(), TypeComment = type_comment };
            return stmt;
        }

        /// <summary>
        /// CPython 3.12: _PyAST_AsyncFor - async for statement
        /// </summary>
        protected GeneratedStmt _PyAST_AsyncFor(object target, object iter, object body, object orelse = null, object type_comment = null)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "asyncfor";
            stmt.Value = new { Target = target, Iter = iter, Body = body, Orelse = orelse ?? new List<object>(), TypeComment = type_comment };
            return stmt;
        }

        /// <summary>
        /// _PyAST_Break - Create break statement
        /// </summary>
        protected GeneratedStmt _PyAST_Break()
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "break";
            stmt.Value = null;
            return stmt;
        }

        /// <summary>
        /// _PyAST_Continue - Create continue statement
        /// </summary>
        protected GeneratedStmt _PyAST_Continue()
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "continue";
            stmt.Value = null;
            return stmt;
        }

        /// <summary>
        /// _PyAST_Match - Create match statement (Python 3.10+)
        /// </summary>
        protected GeneratedStmt _PyAST_Match(object subject, object cases)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "match";
            stmt.Value = new { Subject = subject, Cases = cases };
            return stmt;
        }

        /// <summary>
        /// _PyAST_Global - Create global statement
        /// </summary>
        protected GeneratedStmt _PyAST_Global(object names)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "global";
            stmt.Value = new { Names = names };
            return stmt;
        }

        /// <summary>
        /// _PyAST_Nonlocal - Create nonlocal statement
        /// </summary>
        protected GeneratedStmt _PyAST_Nonlocal(object names)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "nonlocal";
            stmt.Value = new { Names = names };
            return stmt;
        }

        /// <summary>
        /// _PyAST_Import - Create import statement
        /// </summary>
        protected GeneratedStmt _PyAST_Import(object names)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "import";
            stmt.Value = new { Names = names };
            return stmt;
        }

        /// <summary>
        /// _PyAST_ImportFrom - Create from import statement
        /// </summary>
        protected GeneratedStmt _PyAST_ImportFrom(object module, object names, object level = null)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "import_from";
            stmt.Value = new { Module = module, Names = names, Level = level ?? 0 };
            return stmt;
        }

        /// <summary>
        /// _PyAST_ClassDef - Create class definition
        /// </summary>
        protected GeneratedStmt _PyAST_ClassDef(string name, object bases, object keywords, object body,
            object decorator_list = null, object type_params = null)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "class_def";

            var classDef = new GeneratedClassDef
            {
                Name = name,
                Body = body,
                Bases = bases ?? new List<object>(),
                Keywords = keywords ?? new List<object>(),
                DecoratorList = decorator_list ?? new List<object>(),
                TypeParams = type_params
            };

            stmt.Value = classDef;
            return stmt;
        }

        /// <summary>
        /// _PyPegen_interactive_exit - Handle interactive mode exit
        /// </summary>
        protected object _PyPegen_interactive_exit()
        {
            // Return null to signal end of interactive input
            return null;
        }

        /// <summary>
        /// CHECK macro equivalent - Runtime type checking and conversion
        /// </summary>
        protected T CHECK<T>(T value, string expectedType = null) where T : class
        {
            if (value == null)
            {
                Console.WriteLine($"[DEBUG] CHECK: null value for type {typeof(T).Name}");
                return null;
            }

            if (expectedType != null)
            {
                Console.WriteLine($"[DEBUG] CHECK: {value.GetType().Name} -> {expectedType}");
            }

            return value;
        }

        /// <summary>
        /// CHECK_VERSION macro equivalent - Version checking for features
        /// </summary>
        protected T CHECK_VERSION<T>(T value, int minVersion, string featureName) where T : class
        {
            // Always allow for Python 3.12 compatibility
            Console.WriteLine($"[DEBUG] CHECK_VERSION: {featureName} (min version: {minVersion}) - ALLOWED");
            return value;
        }

        /// <summary>
        /// NEW_TYPE_COMMENT - Create type comment
        /// </summary>
        protected string NEW_TYPE_COMMENT(string comment)
        {
            return comment;
        }

        /// <summary>
        /// EXTRA macro equivalent - Create source location info
        /// </summary>
        protected object EXTRA => new {
            Line = CurrentToken?.Line ?? 0,
            Column = CurrentToken?.Column ?? 0,
            Filename = _filename
        };

        // ===== Additional CPython 3.12 Helper Functions =====

        /// <summary>
        /// ParseClassArguments - Parse class arguments including base classes and keyword arguments like metaclass=
        /// Note: This is a simple version that handles basic name-based arguments
        /// </summary>
        protected (List<object> bases, List<object> keywords) ParseClassArguments()
        {
            var bases = new List<object>();
            var keywords = new List<object>();

            Console.WriteLine($"[DEBUG] ParseClassArguments: Starting at position {_position}, token: {CurrentToken?.Type}:{CurrentToken?.Value}");

            while (CurrentToken != null && !(CurrentToken.Type == GeneratedTokenType.OP && CurrentToken.Value == ")"))
            {
                // Check if this is a keyword argument (name = value)
                if (CurrentToken.Type == GeneratedTokenType.NAME)
                {
                    var nameValue = CurrentToken.Value;
                    var nextToken = _position + 1 < _tokens.Count ? _tokens[_position + 1] : null;

                    if (nextToken?.Type == GeneratedTokenType.OP && nextToken.Value == "=")
                    {
                        // This is a keyword argument like metaclass=Meta
                        Console.WriteLine($"[DEBUG] ParseClassArguments: Found keyword argument: {nameValue}");
                        Advance(); // consume keyword name
                        Advance(); // consume '='

                        // For simple cases, we expect another NAME token for the value
                        if (CurrentToken?.Type == GeneratedTokenType.NAME)
                        {
                            var valueExpr = new { type = "name", value = CurrentToken.Value };
                            var keyword = new {
                                arg = nameValue,
                                value = valueExpr
                            };
                            keywords.Add(keyword);
                            Console.WriteLine($"[DEBUG] ParseClassArguments: Added keyword: {nameValue} = {CurrentToken.Value}");
                            Advance(); // consume value
                        }
                        else
                        {
                            Console.WriteLine($"[DEBUG] ParseClassArguments: Expected NAME for keyword value, got {CurrentToken?.Type}:{CurrentToken?.Value}");
                            break;
                        }
                    }
                    else
                    {
                        // This is a positional argument (base class)
                        var baseExpr = new { type = "name", value = nameValue };
                        bases.Add(baseExpr);
                        Console.WriteLine($"[DEBUG] ParseClassArguments: Added base class: {nameValue}");
                        Advance(); // consume name
                    }
                }
                else
                {
                    Console.WriteLine($"[DEBUG] ParseClassArguments: Unexpected token type: {CurrentToken?.Type}:{CurrentToken?.Value}");
                    break;
                }

                // Check for comma separator
                if (CurrentToken?.Type == GeneratedTokenType.OP && CurrentToken.Value == ",")
                {
                    Advance(); // consume ','
                    // Skip whitespace/newlines if any
                    while (CurrentToken?.Type == GeneratedTokenType.NEWLINE || CurrentToken?.Type == GeneratedTokenType.NL)
                    {
                        Advance();
                    }
                }
                else
                {
                    break; // No more arguments
                }
            }

            Console.WriteLine($"[DEBUG] ParseClassArguments: Completed with {bases.Count} bases and {keywords.Count} keywords");
            return (bases, keywords);
        }

        /// <summary>
        /// Check if operator is an augmented assignment operator
        /// </summary>
        protected bool IsAugmentedAssignmentOperator(string op)
        {
            return op switch
            {
                "+=" => true,
                "-=" => true,
                "*=" => true,
                "/=" => true,
                "//=" => true,
                "%=" => true,
                "**=" => true,
                "&=" => true,
                "|=" => true,
                "^=" => true,
                "<<=" => true,
                ">>=" => true,
                "@=" => true,
                _ => false
            };
        }

        /// <summary>
        /// Parse augmented assignment statement
        /// </summary>
        protected GeneratedStmt ParseAugmentedAssignment(object target, string augOp, object value)
        {
            Console.WriteLine($"[DEBUG] ParseAugmentedAssignment: target={target}, op={augOp}, value={value}");

            // Convert the operator to the corresponding binary operator
            var binaryOp = augOp switch
            {
                "+=" => "Add",
                "-=" => "Sub",
                "*=" => "Mult",
                "/=" => "Div",
                "//=" => "FloorDiv",
                "%=" => "Mod",
                "**=" => "Pow",
                "&=" => "BitAnd",
                "|=" => "BitOr",
                "^=" => "BitXor",
                "<<=" => "LShift",
                ">>=" => "RShift",
                "@=" => "MatMult",
                _ => throw new InvalidOperationException($"Unknown augmented assignment operator: {augOp}")
            };

            return new GeneratedStmt
            {
                StatementType = "augassign",
                Value = new { Target = target, Op = binaryOp, Value = value }
            };
        }

        /// <summary>
        /// _PyPegen_seq_insert_in_front - Insert item at front of sequence
        /// </summary>
        protected List<object> _PyPegen_seq_insert_in_front(object item, List<object> sequence)
        {
            var result = new List<object>();
            if (item != null)
            {
                result.Add(item);
            }
            if (sequence != null)
            {
                result.AddRange(sequence);
            }
            return result;
        }

        /// <summary>
        /// _PyPegen_seq_extract_starred_exprs - Extract starred expressions from sequence
        /// </summary>
        protected List<object> _PyPegen_seq_extract_starred_exprs(List<object> sequence)
        {
            var result = new List<object>();
            if (sequence != null)
            {
                foreach (var item in sequence)
                {
                    if (item is GeneratedExpr expr && expr.ExpressionType == "starred")
                    {
                        result.Add(item);
                    }
                    else if (item?.ToString()?.Contains("starred") == true)
                    {
                        result.Add(item);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// _PyPegen_seq_delete_starred_exprs - Remove starred expressions from sequence
        /// </summary>
        protected List<object> _PyPegen_seq_delete_starred_exprs(List<object> sequence)
        {
            var result = new List<object>();
            if (sequence != null)
            {
                foreach (var item in sequence)
                {
                    if (item is GeneratedExpr expr && expr.ExpressionType == "starred")
                    {
                        continue; // Skip starred expressions
                    }
                    else if (item?.ToString()?.Contains("starred") == true)
                    {
                        continue; // Skip starred expressions
                    }
                    result.Add(item);
                }
            }
            return result;
        }

        /// <summary>
        /// _PyPegen_join_sequences - Join two sequences
        /// </summary>
        protected List<object> _PyPegen_join_sequences(List<object> sequence1, List<object> sequence2)
        {
            var result = new List<object>();
            if (sequence1 != null)
            {
                result.AddRange(sequence1);
            }
            if (sequence2 != null)
            {
                result.AddRange(sequence2);
            }
            return result;
        }

        /// <summary>
        /// _PyPegen_join_names_with_dot - Join names with dot separator
        /// </summary>
        protected string _PyPegen_join_names_with_dot(string name1, string name2)
        {
            if (string.IsNullOrEmpty(name1))
                return name2 ?? "";
            if (string.IsNullOrEmpty(name2))
                return name1;
            return $"{name1}.{name2}";
        }

        /// <summary>
        /// _PyPegen_seq_count_dots - Count dots in import sequence
        /// </summary>
        protected int _PyPegen_seq_count_dots(List<object> sequence)
        {
            int count = 0;
            if (sequence != null)
            {
                foreach (var item in sequence)
                {
                    if (item?.ToString() == ".")
                    {
                        count++;
                    }
                    else if (item is string str && str.Contains("."))
                    {
                        count += str.Count(c => c == '.');
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// _PyPegen_keyword_or_starred - Create keyword or starred wrapper
        /// </summary>
        protected object _PyPegen_keyword_or_starred(object item, int isKeyword)
        {
            return new {
                Type = isKeyword == 1 ? "keyword" : "starred",
                Value = item,
                IsKeyword = isKeyword == 1
            };
        }

        /// <summary>
        /// _PyPegen_joined_str - Create joined string for f-strings
        /// </summary>
        protected GeneratedExpr _PyPegen_joined_str(object start, List<object> middle, object end)
        {
            var expr = new GeneratedExpr();
            expr.ExpressionType = "joined_str";
            expr.Value = new {
                Start = start,
                Middle = middle ?? new List<object>(),
                End = end
            };
            return expr;
        }

        /// <summary>
        /// _PyPegen_concatenate_strings - Concatenate string literals and f-strings
        /// </summary>
        protected GeneratedExpr _PyPegen_concatenate_strings(List<object> strings)
        {
            if (strings == null || strings.Count == 0)
            {
                var emptyExpr = new GeneratedExpr();
                emptyExpr.ExpressionType = "constant";
                emptyExpr.Value = "";
                return emptyExpr;
            }

            if (strings.Count == 1)
            {
                var single = strings[0];
                if (single is GeneratedExpr expr)
                {
                    return expr;
                }
            }

            // Multiple strings - create concatenated expression
            var concatExpr = new GeneratedExpr();
            concatExpr.ExpressionType = "concatenated_str";
            concatExpr.Value = strings;
            return concatExpr;
        }

        /// <summary>
        /// _PyPegen_constant_from_token - Create constant from token
        /// </summary>
        protected GeneratedExpr _PyPegen_constant_from_token(object token)
        {
            var expr = new GeneratedExpr();
            expr.ExpressionType = "constant";

            if (token is GeneratedTokenInfo genToken)
            {
                expr.Value = genToken.Value;
            }
            else
            {
                expr.Value = token?.ToString() ?? "";
            }

            return expr;
        }

        /// <summary>
        /// _PyPegen_decoded_constant_from_token - Create decoded constant from token
        /// </summary>
        protected GeneratedExpr _PyPegen_decoded_constant_from_token(object token)
        {
            var expr = new GeneratedExpr();
            expr.ExpressionType = "constant";

            if (token is GeneratedTokenInfo genToken)
            {
                // Decode escaped sequences in f-string format specs
                var value = genToken.Value;
                if (!string.IsNullOrEmpty(value))
                {
                    // Basic decoding - could be enhanced
                    value = value.Replace("\\n", "\n")
                                 .Replace("\\t", "\t")
                                 .Replace("\\r", "\r")
                                 .Replace("\\\\", "\\");
                }
                expr.Value = value;
            }
            else
            {
                expr.Value = token?.ToString() ?? "";
            }

            return expr;
        }

        /// <summary>
        /// _PyPegen_check_fstring_conversion - Check f-string conversion specifier
        /// </summary>
        protected object _PyPegen_check_fstring_conversion(object convToken, object conv)
        {
            var convStr = conv?.ToString();

            // Valid conversion characters: s, r, a
            if (convStr != "s" && convStr != "r" && convStr != "a")
            {
                Console.WriteLine($"[DEBUG] _PyPegen_check_fstring_conversion: Invalid conversion '{convStr}', should be 's', 'r', or 'a'");
                // In a full implementation, this might raise a syntax error
            }

            return new {
                ConversionToken = convToken,
                Conversion = convStr
            };
        }

        /// <summary>
        /// _PyPegen_setup_full_format_spec - Setup full format specification
        /// </summary>
        protected object _PyPegen_setup_full_format_spec(object colon, List<object> spec)
        {
            return new {
                Colon = colon,
                FormatSpec = spec ?? new List<object>()
            };
        }

        /// <summary>
        /// _PyAST_FormattedValue - Create formatted value AST node for f-strings
        /// </summary>
        protected GeneratedExpr _PyAST_FormattedValue(object value, object conversion = null, object formatSpec = null)
        {
            var expr = new GeneratedExpr();
            expr.ExpressionType = "formatted_value";
            expr.Value = new {
                Value = value,
                Conversion = conversion,
                FormatSpec = formatSpec
            };
            return expr;
        }

        /// <summary>
        /// _PyAST_JoinedStr - Create joined string AST node for f-strings
        /// </summary>
        protected GeneratedExpr _PyAST_JoinedStr(List<object> values)
        {
            var expr = new GeneratedExpr();
            expr.ExpressionType = "joined_str";
            expr.Value = values ?? new List<object>();
            return expr;
        }

        /// <summary>
        /// _PyAST_Await - Create await expression AST node
        /// </summary>
        protected GeneratedExpr _PyAST_Await(object value)
        {
            var expr = new GeneratedExpr();
            expr.ExpressionType = "await";
            expr.Value = value;
            return expr;
        }

        /// <summary>
        /// _PyAST_TypeVar - Create TypeVar expression AST node for PEP 695
        /// </summary>
        protected GeneratedExpr _PyAST_TypeVar(string name, object bound = null)
        {
            var expr = new GeneratedExpr();
            expr.ExpressionType = "type_var";
            expr.Value = new { name = name, bound = bound };
            return expr;
        }

        /// <summary>
        /// _PyAST_TypeVarTuple - Create TypeVarTuple expression AST node for PEP 695
        /// </summary>
        protected GeneratedExpr _PyAST_TypeVarTuple(string name)
        {
            var expr = new GeneratedExpr();
            expr.ExpressionType = "type_var_tuple";
            expr.Value = new { name = name };
            return expr;
        }

        /// <summary>
        /// _PyAST_ParamSpec - Create ParamSpec expression AST node for PEP 695
        /// </summary>
        protected GeneratedExpr _PyAST_ParamSpec(string name)
        {
            var expr = new GeneratedExpr();
            expr.ExpressionType = "param_spec";
            expr.Value = new { name = name };
            return expr;
        }

        /// <summary>
        /// _PyAST_Try - Create try statement AST node
        /// </summary>
        protected GeneratedStmt _PyAST_Try(object body, object handlers = null, object orelse = null, object finalbody = null)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "try";
            stmt.Value = new {
                body = body,
                handlers = handlers,
                orelse = orelse,
                finalbody = finalbody
            };
            return stmt;
        }

        /// <summary>
        /// _PyAST_TryStar - Create try statement AST node with except* handlers (PEP 654)
        /// </summary>
        protected GeneratedStmt _PyAST_TryStar(object body, object handlers = null, object orelse = null, object finalbody = null)
        {
            var stmt = new GeneratedStmt();
            stmt.StatementType = "try_star";  // Different type to distinguish except* handling
            stmt.Value = new {
                body = body,
                handlers = handlers,
                orelse = orelse,
                finalbody = finalbody
            };
            return stmt;
        }

        /// <summary>
        /// _PyAST_ExceptHandler - Create except handler AST node (supports both except and except*)
        /// </summary>
        protected object _PyAST_ExceptHandler(object type, string name, object body, bool is_star = false)
        {
            return new {
                type = type,
                name = name,
                body = body,
                is_star = is_star  // PEP 654: except* support
            };
        }

        /// <summary>
        /// _PyPegen_checked_future_import - Check future import validity
        /// </summary>
        protected GeneratedStmt _PyPegen_checked_future_import(string featureName, object alias, int level)
        {
            // For now, allow all future imports
            Console.WriteLine($"[DEBUG] _PyPegen_checked_future_import: {featureName} (level: {level})");

            var stmt = new GeneratedStmt();
            stmt.StatementType = "import_from";
            stmt.Value = new {
                Module = "__future__",
                Name = featureName,
                Alias = alias,
                Level = level
            };
            return stmt;
        }

        /// <summary>
        /// CHECK_NULL_ALLOWED - Check for null with allowance
        /// </summary>
        protected T CHECK_NULL_ALLOWED<T>(T value) where T : class
        {
            // Allow null values in this context
            return value;
        }

        /// <summary>
        /// _PyPegen_get_last_comprehension_item - Get last item from comprehension
        /// </summary>
        protected object _PyPegen_get_last_comprehension_item(object comprehension)
        {
            if (comprehension is List<object> list && list.Count > 0)
            {
                return list.Last();
            }
            return comprehension;
        }

        /// <summary>
        /// PyPegen_last_item - Get last item from sequence
        /// </summary>
        protected T PyPegen_last_item<T>(List<T> sequence) where T : class
        {
            if (sequence != null && sequence.Count > 0)
            {
                return sequence.Last();
            }
            return null;
        }

        // ===== Error Handling System (CPython 3.12 Style) =====

        /// <summary>
        /// RAISE_SYNTAX_ERROR - Raise syntax error with message
        /// </summary>
        protected object RAISE_SYNTAX_ERROR(string message)
        {
            var token = CurrentToken;
            var location = new {
                Line = token?.Line ?? 0,
                Column = token?.Column ?? 0,
                Filename = _filename
            };

            Console.WriteLine($"[SYNTAX ERROR] {message} at {_filename}:{location.Line}:{location.Column}");

            // For now, throw an exception to stop parsing
            // In a full implementation, this would be accumulated for better error reporting
            throw new SyntaxErrorException(message, _filename, location.Line, location.Column);
        }

        /// <summary>
        /// RAISE_SYNTAX_ERROR_KNOWN_RANGE - Raise syntax error with known range
        /// </summary>
        protected object RAISE_SYNTAX_ERROR_KNOWN_RANGE(object startToken, object endToken, string message)
        {
            var location = GetLocationFromToken(startToken);
            var endLocation = GetLocationFromToken(endToken);

            Console.WriteLine($"[SYNTAX ERROR RANGE] {message} at {_filename}:{location.Line}:{location.Column}-{endLocation.Line}:{endLocation.Column}");

            throw new SyntaxErrorException(message, _filename, location.Line, location.Column, endLocation.Line, endLocation.Column);
        }

        /// <summary>
        /// RAISE_SYNTAX_ERROR_STARTING_FROM - Raise syntax error starting from specific token
        /// </summary>
        protected object RAISE_SYNTAX_ERROR_STARTING_FROM(object startToken, string message)
        {
            var location = GetLocationFromToken(startToken);

            Console.WriteLine($"[SYNTAX ERROR FROM] {message} starting from {_filename}:{location.Line}:{location.Column}");

            throw new SyntaxErrorException(message, _filename, location.Line, location.Column);
        }

        /// <summary>
        /// RAISE_INDENTATION_ERROR - Raise indentation error
        /// </summary>
        protected object RAISE_INDENTATION_ERROR(string message)
        {
            var token = CurrentToken;
            var location = new {
                Line = token?.Line ?? 0,
                Column = token?.Column ?? 0,
                Filename = _filename
            };

            Console.WriteLine($"[INDENTATION ERROR] {message} at {_filename}:{location.Line}:{location.Column}");

            throw new IndentationErrorException(message, _filename, location.Line, location.Column);
        }

        /// <summary>
        /// Get location information from a token object
        /// </summary>
        private dynamic GetLocationFromToken(object token)
        {
            if (token == null)
            {
                return new { Line = 0, Column = 0 };
            }

            // Handle GeneratedTokenInfo
            if (token is GeneratedTokenInfo genToken)
            {
                return new { Line = genToken.Line, Column = genToken.Column };
            }

            // Handle current token fallback
            var current = CurrentToken;
            return new { Line = current?.Line ?? 0, Column = current?.Column ?? 0 };
        }

        // ===== Context Management Methods (CPython 3.12 Style) =====

        /// <summary>
        /// Push a new context onto the context stack
        /// </summary>
        protected void PushContext(ContextType type, string? name = null)
        {
            var newLevel = _contextStack.Count > 0 ? _contextStack.Peek().NestingLevel + 1 : 0;
            var context = new ParserContext(type, newLevel, _position, name);
            _contextStack.Push(context);

            Console.WriteLine($"[CONTEXT] Pushed {context}");
        }

        /// <summary>
        /// Pop the current context from the context stack
        /// </summary>
        protected ParserContext? PopContext()
        {
            if (_contextStack.Count > 1) // Keep module context
            {
                var context = _contextStack.Pop();
                Console.WriteLine($"[CONTEXT] Popped {context}");
                return context;
            }
            return null;
        }

        /// <summary>
        /// Check if we're currently in a specific context type
        /// </summary>
        protected bool IsInContext(ContextType type)
        {
            return _contextStack.Any(ctx => ctx.Type == type);
        }

        /// <summary>
        /// Get the current top context
        /// </summary>
        protected ParserContext? GetCurrentContext()
        {
            return _contextStack.Count > 0 ? _contextStack.Peek() : null;
        }

        /// <summary>
        /// Validate if a statement is allowed in the current context
        /// </summary>
        protected void ValidateStatementContext(string statementType)
        {
            switch (statementType)
            {
                case "return":
                    if (!IsInContext(ContextType.Function) && !IsInContext(ContextType.Lambda))
                    {
                        RAISE_SYNTAX_ERROR("'return' outside function");
                    }
                    break;

                case "yield":
                    if (!IsInContext(ContextType.Function))
                    {
                        RAISE_SYNTAX_ERROR("'yield' outside function");
                    }
                    break;

                case "break":
                case "continue":
                    if (!IsInContext(ContextType.Loop))
                    {
                        var msg = statementType == "break" ? "'break' outside loop" : "'continue' not properly in loop";
                        RAISE_SYNTAX_ERROR(msg);
                    }
                    break;

                case "await":
                    if (!IsInContext(ContextType.Async))
                    {
                        RAISE_SYNTAX_ERROR("'await' outside async function");
                    }
                    break;
            }
        }

        /// <summary>
        /// Debug method to print current context stack
        /// </summary>
        protected void PrintContextStack()
        {
            Console.WriteLine($"[CONTEXT-STACK] Current stack ({_contextStack.Count} levels):");
            foreach (var ctx in _contextStack.Reverse())
            {
                Console.WriteLine($"[CONTEXT-STACK]   {ctx}");
            }
        }

        // ===== Missing PEG Parser Functions for Function Parameters =====

        /// <summary>
        /// Create an argument AST node from name and optional annotation
        /// </summary>
        protected GeneratedExpr _PyAST_arg(string name, object? annotation = null, string? type_comment = null)
        {
            var expr = new GeneratedExpr();
            expr.ExpressionType = "arg";
            expr.Value = new {
                arg = name,
                annotation = annotation,
                type_comment = type_comment
            };
            return expr;
        }

        /// <summary>
        /// Helper function to add type comment to argument
        /// </summary>
        protected GeneratedExpr _PyPegen_add_type_comment_to_arg(object p, GeneratedExpr arg, object? type_comment)
        {
            if (type_comment != null && arg.Value is object argValue)
            {
                // Create new arg with type comment
                var newArg = new GeneratedExpr();
                newArg.ExpressionType = "arg";

                dynamic dynArgValue = argValue;
                newArg.Value = new {
                    arg = dynArgValue.arg,
                    annotation = dynArgValue.annotation,
                    type_comment = type_comment
                };
                return newArg;
            }
            return arg;
        }

        /// <summary>
        /// Create arguments structure from parameter lists
        /// CPython compatible function for parsing function parameters
        /// </summary>
        protected object _PyPegen_make_arguments(object p,
            object? posonlyargs = null,
            object? posonly_defaults = null,
            object? args = null,
            object? defaults = null,
            object? star_etc = null)
        {
            var result = new Dictionary<string, object>();

            // Convert parameter lists to proper format
            result["posonlyargs"] = ConvertArgsList(posonlyargs) ?? new List<object>();
            result["args"] = ConvertArgsList(args) ?? new List<object>();
            result["vararg"] = null;
            result["kwonlyargs"] = new List<object>();
            result["kw_defaults"] = new List<object>();
            result["kwarg"] = null;
            result["defaults"] = ConvertDefaultsList(defaults) ?? new List<object>();

            // Handle star_etc (varargs, kwargs, kwonly args)
            if (star_etc != null)
            {
                // star_etc contains varargs, kwonly args, and kwargs
                Console.WriteLine($"[DEBUG] _PyPegen_make_arguments: Processing star_etc: {star_etc.GetType().Name}");

                if (star_etc is Dictionary<string, object> starDict)
                {
                    if (starDict.ContainsKey("vararg") && starDict["vararg"] != null)
                    {
                        result["vararg"] = starDict["vararg"];
                        Console.WriteLine($"[DEBUG] _PyPegen_make_arguments: Set vararg from star_etc");
                    }
                    if (starDict.ContainsKey("kwarg") && starDict["kwarg"] != null)
                    {
                        result["kwarg"] = starDict["kwarg"];
                        Console.WriteLine($"[DEBUG] _PyPegen_make_arguments: Set kwarg from star_etc");
                    }
                    if (starDict.ContainsKey("kwonlyargs"))
                    {
                        result["kwonlyargs"] = starDict["kwonlyargs"];
                    }
                }
            }

            Console.WriteLine($"[DEBUG] _PyPegen_make_arguments: Created arguments with {((List<object>)result["args"]).Count} regular args");
            return result;
        }

        /// <summary>
        /// CPython compatible function for creating star_etc structure (varargs/kwargs)
        /// </summary>
        protected object _PyPegen_star_etc(object p, object? vararg, object? kwonlyargs, object? kwarg)
        {
            var result = new Dictionary<string, object>();
            result["vararg"] = vararg;
            result["kwonlyargs"] = kwonlyargs ?? new List<object>();
            result["kwarg"] = kwarg;

            Console.WriteLine($"[DEBUG] _PyPegen_star_etc: Created with vararg={vararg != null}, kwarg={kwarg != null}");
            return result;
        }

        /// <summary>
        /// CPython compatible function for creating slash_with_default structure
        /// </summary>
        protected object _PyPegen_slash_with_default(object p, object? noDefaults, object? withDefaults)
        {
            var result = new Dictionary<string, object>();
            result["no_defaults"] = noDefaults ?? new List<object>();
            result["with_defaults"] = withDefaults ?? new List<object>();

            Console.WriteLine($"[DEBUG] _PyPegen_slash_with_default: Created");
            return result;
        }

        /// <summary>
        /// Convert argument sequence to list format
        /// </summary>
        private List<object> ConvertArgsList(object? argSeq)
        {
            if (argSeq == null) return new List<object>();

            if (argSeq is List<object> list)
            {
                Console.WriteLine($"[DEBUG] ConvertArgsList: Input is List<object> with {list.Count} items");
                return list;
            }

            if (argSeq is IEnumerable<object> enumerable)
            {
                var result = enumerable.ToList();
                Console.WriteLine($"[DEBUG] ConvertArgsList: Converted IEnumerable to List with {result.Count} items");
                return result;
            }

            // Single argument case
            var singleArgList = new List<object> { argSeq };
            Console.WriteLine($"[DEBUG] ConvertArgsList: Created single-arg list");
            return singleArgList;
        }

        /// <summary>
        /// Convert defaults sequence to list format
        /// </summary>
        private List<object> ConvertDefaultsList(object? defaults)
        {
            if (defaults == null) return new List<object>();

            if (defaults is List<object> list) return list;
            if (defaults is IEnumerable<object> enumerable) return enumerable.ToList();

            return new List<object> { defaults };
        }

        /// <summary>
        /// Helper method to create augmented assignment operator objects
        /// </summary>
        protected object CreateAugOperator(string operatorType)
        {
            return new { kind = operatorType };
        }

        // ========================================
        // Dummy methods for missing grammar rules
        // TODO: Generate these from grammar or implement properly
        // ========================================

        protected object? InvalidIfStmt() => null;
        protected object? InvalidWhileStmt() => null;
        protected object? InvalidForStmt() => null;
        protected object? InvalidForTarget() => null;
        protected object? Block() => null;
        protected object? ElifStmt() => null;
        protected object? Async() => null;
        protected object? NamedExpression() => null;
        protected object? StarExpressions() => null;
        protected object? StarTargets() => null;

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

    /// <summary>
    /// Syntax error exception for CPython 3.12 compatible error handling
    /// </summary>
    public class SyntaxErrorException : Exception
    {
        public string Filename { get; }
        public int Line { get; }
        public int Column { get; }
        public int? EndLine { get; }
        public int? EndColumn { get; }

        public SyntaxErrorException(string message, string filename, int line, int column)
            : base(message)
        {
            Filename = filename;
            Line = line;
            Column = column;
        }

        public SyntaxErrorException(string message, string filename, int line, int column, int endLine, int endColumn)
            : base(message)
        {
            Filename = filename;
            Line = line;
            Column = column;
            EndLine = endLine;
            EndColumn = endColumn;
        }

        public override string ToString()
        {
            if (EndLine.HasValue && EndColumn.HasValue)
            {
                return $"SyntaxError: {Message} ({Filename}:{Line}:{Column}-{EndLine}:{EndColumn})";
            }
            return $"SyntaxError: {Message} ({Filename}:{Line}:{Column})";
        }
    }

    /// <summary>
    /// Indentation error exception for CPython 3.12 compatible error handling
    /// </summary>
    public class IndentationErrorException : SyntaxErrorException
    {
        public IndentationErrorException(string message, string filename, int line, int column)
            : base(message, filename, line, column)
        {
        }

        public override string ToString()
        {
            return $"IndentationError: {Message} ({Filename}:{Line}:{Column})";
        }
    }
}