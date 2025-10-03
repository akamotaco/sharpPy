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

        // CPython 3.12: Track active left-recursive parsing to prevent infinite loops
        private readonly HashSet<(int position, string ruleName)> _activeLeftRecursive = new();

        protected readonly string _filename;

        // CPython 3.12: Error tracking for RAISE_SYNTAX_ERROR
        // Instead of throwing immediately, we store the error and throw only if no alternative succeeds
        protected string? _pendingSyntaxError = null;
        protected int _pendingErrorPosition = -1;

        // CPython 3.12: Flag to control whether invalid_* rules should be called
        // Set to false in *_without_invalid rules to prevent invalid rule execution
        protected bool _callInvalidRules = true;

        // CPython 3.12: PegInterpreter for grammar-based parsing
        // Concrete type is defined in GeneratedPyParser via property
        protected abstract object? InterpreterObject { get; }

        // Context management for CPython 3.12 compatibility
        protected readonly Stack<ParserContext> _contextStack = new();
        protected int _indentLevel = 0;

        // CPython 3.12: BoolOp and CmpOp constants for grammar actions
        protected static readonly BoolOpType Or = BoolOpType.Or;
        protected static readonly BoolOpType And = BoolOpType.And;
        protected static readonly CmpOpType Eq = CmpOpType.Eq;
        protected static readonly CmpOpType NotEq = CmpOpType.NotEq;
        protected static readonly CmpOpType Lt = CmpOpType.Lt;
        protected static readonly CmpOpType LtE = CmpOpType.LtE;
        protected static readonly CmpOpType Gt = CmpOpType.Gt;
        protected static readonly CmpOpType GtE = CmpOpType.GtE;
        protected static readonly CmpOpType Is = CmpOpType.Is;
        protected static readonly CmpOpType IsNot = CmpOpType.IsNot;
        protected static readonly CmpOpType In = CmpOpType.In;
        protected static readonly CmpOpType NotIn = CmpOpType.NotIn;

        // CPython 3.12: ExprContext constants
        protected static readonly ExprContext Load = ExprContext.Load;
        protected static readonly ExprContext Store = ExprContext.Store;
        protected static readonly ExprContext Del = ExprContext.Del;

        // CPython 3.12: UnaryOp constants
        protected static readonly UnaryOpType Invert = UnaryOpType.Invert;
        protected static readonly UnaryOpType Not = UnaryOpType.Not;
        protected static readonly UnaryOpType UAdd = UnaryOpType.UAdd;
        protected static readonly UnaryOpType USub = UnaryOpType.USub;

        // CPython 3.12: BinOp constants
        protected static readonly BinOpType Add = BinOpType.Add;
        protected static readonly BinOpType Sub = BinOpType.Sub;
        protected static readonly BinOpType Mult = BinOpType.Mult;
        protected static readonly BinOpType MatMult = BinOpType.MatMult;
        protected static readonly BinOpType Div = BinOpType.Div;
        protected static readonly BinOpType Mod = BinOpType.Mod;
        protected static readonly BinOpType Pow = BinOpType.Pow;
        protected static readonly BinOpType LShift = BinOpType.LShift;
        protected static readonly BinOpType RShift = BinOpType.RShift;
        protected static readonly BinOpType BitOr = BinOpType.BitOr;
        protected static readonly BinOpType BitXor = BinOpType.BitXor;
        protected static readonly BinOpType BitAnd = BinOpType.BitAnd;
        protected static readonly BinOpType FloorDiv = BinOpType.FloorDiv;

        // CPython 3.12: Python singleton constants
        protected readonly object? Py_None = null;
        protected readonly bool Py_True = true;
        protected readonly bool Py_False = false;
        protected readonly string Py_Ellipsis = "...";

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

        // Expect method moved to generated PyParser.cs
        // Each generated parser has its own Expect implementation


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

        /// <summary>
        /// CPython 3.12: Simple memoization wrapper for non-left-recursive rules marked with (memo)
        /// Unlike TryLeftRecursive, this only caches results without Warth algorithm
        /// Prevents infinite recursion by tracking active parsing
        /// </summary>
        protected T? TryMemoized<T>(string ruleName, Func<T?> parseMethod) where T : class
        {
            var key = (_position, ruleName);

            // Check if already cached
            if (_memoCache.TryGetValue(key, out var cached))
            {
                return cached as T;
            }

            // Check for recursive call at same position (would cause infinite loop)
            if (_activeLeftRecursive.Contains(key))
            {
                // Recursive call detected - return null to break recursion
                // This handles right-recursion in rules like factor: '+' factor | power
                return null;
            }

            // Mark as active
            _activeLeftRecursive.Add(key);

            try
            {
                // Not cached - parse and cache result
                var result = parseMethod();
                SetMemo(ruleName, result);
                return result;
            }
            finally
            {
                // Remove from active set
                _activeLeftRecursive.Remove(key);
            }
        }

        // Left recursion support - based on Warth et al. "Packrat parsers can support left recursion"
        protected T? TryLeftRecursive<T>(string ruleName, Func<T?> parseMethod) where T : class
        {
            var key = (_position, ruleName);
            var startPos = _position;

            Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} at position {startPos}");

            // Warth algorithm: Use a special marker to distinguish "being processed" from "already completed"
            // We need to know if this is:
            // 1. First call (no memo) → start expansion
            // 2. Recursive call during seed/expansion (memo exists, active) → return memo value
            // 3. Call after completion (memo exists, not active) → return cached result

            // Check if we're actively processing this position+rule
            bool isActive = _activeLeftRecursive.Contains(key);

            if (_memoCache.TryGetValue(key, out var existing))
            {
                // Memo exists - either being processed or completed
                Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} found in memo: {(existing != null ? "result" : "null")}, active={isActive}");

                if (isActive)
                {
                    // Recursive call during active processing - return current memo to build on it
                    Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} recursive call, returning memo");
                    return existing as T;
                }
                else
                {
                    // Previous completed parse - return cached result
                    Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} returning cached result");
                    return existing as T;
                }
            }

            // First time at this position - start left-recursive expansion
            Console.WriteLine($"[DEBUG] TryLeftRecursive: {ruleName} starting new left-recursive parse");
            _activeLeftRecursive.Add(key);

            try
            {
                // Step 1: Seed with failure
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
            finally
            {
                // Clean up: remove from active set
                _activeLeftRecursive.Remove(key);
            }
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
            var statements = parser?.Statements();

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
                var stmtSeq = parser?.Statement(); // Use generated parser's method - returns GeneratedStmtSeq
                if (stmtSeq != null)
                {
                    // ParseStatement always returns GeneratedStmtSeq
                    statementList.AddRange(stmtSeq);
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

        // Removed Statements() - using generated parser's implementation instead

        // Generic comparison parsing template - will be replaced by generated ParseComparison
        protected object? ParseComparisonTemplate()
        {
            // This is a temporary placeholder - the generated parser will have ParseComparison
            return null;
        }

        // Common parsing methods - no need to generate these dynamically






        // Removed Statement() - using generated parser's implementation instead

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
            var expr = (object?)null; // Expression(); // Will use generated method
            if (expr != null && expr is GeneratedExpr genExpr)
            {
                // Return expression statement
                return new GeneratedExprStmt { Value = genExpr };
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
            var augTarget = parser?.Expression();

            if (augTarget != null)
            {
                // Check for augmented assignment operators: +=, -=, *=, /=, etc.
                string? augOp = TryMatchAugAssignOp();
                if (augOp != null)
                {
                    // Matched augmented assignment operator
                    Advance(); // consume the operator

                    // Parse the value (right side)
                    var augValue = parser?.Expression();
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
                var target = parser?.Expression();

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
            var value = parser?.Expression();
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
        /// _PyAST_AugAssign(target, op, value, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedAugAssignStmt _PyAST_AugAssign(object target, string op, object value, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedAugAssignStmt
            {
                Target = ASTHelpers.ExtractExpr(target),
                Op = op,
                Value = ASTHelpers.ExtractExpr(value),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
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

                // Handle Name expression - leaf node, no recursion needed
                if (genExpr is GeneratedNameExpr)
                {
                    return;
                }

                // Handle List elements
                if (genExpr is GeneratedListExpr listExpr)
                {
                    foreach (var elem in listExpr.Elements)
                    {
                        SetExprContext(elem, context);
                    }
                    return;
                }

                // Handle Tuple elements
                if (genExpr is GeneratedTupleExpr tupleExpr)
                {
                    foreach (var elem in tupleExpr.Elements)
                    {
                        SetExprContext(elem, context);
                    }
                    return;
                }

                // Handle Starred value
                if (genExpr is GeneratedStarredExpr starredExpr)
                {
                    SetExprContext(starredExpr.Value, context);
                    return;
                }

                // Handle Attribute
                if (genExpr is GeneratedAttributeExpr attrExpr)
                {
                    SetExprContext(attrExpr.Value, context);
                    return;
                }

                // Handle Subscript
                if (genExpr is GeneratedSubscriptExpr subscriptExpr)
                {
                    SetExprContext(subscriptExpr.Value, context);
                    return;
                }
            }
        }

        // If statement parsing template
        protected object? ParseIfStatement()
        {
            if (CurrentToken?.Type.ToString() != "NAME" || CurrentToken?.Value != "if") return null;

            Advance(); // consume 'if'

            var condition = (object?)null; // Expression(); // Will use generated method
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
            var bodyStmt = parser?.Statement();
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
        protected GeneratedTokenInfo? ExpectToken(GeneratedTokenType tokenType, string value = null)
        {
            // CPython 3.12: ExpectToken matches token type (and optionally value)
            // Returns the token if matched, null otherwise
            if (CurrentToken?.Type == tokenType)
            {
                if (value == null || CurrentToken.Value == value)
                {
                    var token = CurrentToken;
                    Advance();
                    return token;
                }
            }
            return null;
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
            if (ExpectToken(GeneratedTokenType.INDENT) == null)
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
                var stmt = parser?.Statement();

                Console.WriteLine($"[DEBUG] ParseBlock: After ParseStatement, position {startPos} -> {_position}, stmt = {stmt?.GetType()?.Name}");

                if (stmt != null)
                {
                    // ParseStatement returns GeneratedStmtSeq
                    statements.AddRange(stmt);
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
        /// CPython 3.12: _PyAST_FunctionDef(name, arguments, body, decorator_list, returns, type_comment, type_params, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedStmt _PyAST_FunctionDef(object name = null, object? arguments = null, object body = null,
            object decorator_list = null, object returns = null, object type_comment = null,
            object? type_params = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            // Extract string from name (could be GeneratedToken with Name.id)
            string nameStr = ASTHelpers.ExtractStringValue(name);

            Console.WriteLine($"[DEBUG] _PyAST_FunctionDef: Creating function '{nameStr}' with body type: {body?.GetType().Name}");
            Console.WriteLine($"[DEBUG] _PyAST_FunctionDef: arguments parameter is null: {arguments == null}");
            if (arguments != null)
            {
                Console.WriteLine($"[DEBUG] _PyAST_FunctionDef: arguments type: {arguments.GetType().Name}");
                Console.WriteLine($"[DEBUG] _PyAST_FunctionDef: arguments value: {arguments}");
            }

            var finalArguments = (arguments as GeneratedArguments) ?? _PyPegen_empty_arguments();
            Console.WriteLine($"[DEBUG] _PyAST_FunctionDef: Using {(arguments == null ? "empty" : "provided")} arguments");

            var funcDef = new GeneratedFunctionDefStmt
            {
                Name = nameStr,
                Arguments = finalArguments,
                Body = ASTHelpers.ExtractStmtSeq(body),
                DecoratorList = ASTHelpers.ExtractExprSeq(decorator_list),
                Returns = returns as GeneratedExpr,
                TypeComment = ASTHelpers.ExtractStringValue(type_comment),
                TypeParams = (type_params as GeneratedTypeParamSeq),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };

            Console.WriteLine($"[DEBUG] _PyAST_FunctionDef: Function definition created successfully");
            return funcDef;
        }

        /// <summary>
        /// _PyAST_AsyncFunctionDef - Create async function definition AST node
        /// CPython 3.12: _PyAST_AsyncFunctionDef(name, arguments, body, decorator_list, returns, type_comment, type_params, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedAsyncFunctionDefStmt _PyAST_AsyncFunctionDef(string name, object? arguments = null, object body = null,
            object decorator_list = null, object returns = null, string type_comment = null,
            object? type_params = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            Console.WriteLine($"[DEBUG] _PyAST_AsyncFunctionDef: Creating async function '{name}' with body type: {body?.GetType().Name}");

            var funcDef = new GeneratedAsyncFunctionDefStmt
            {
                Name = name,
                Arguments = (arguments as GeneratedArguments) ?? _PyPegen_empty_arguments(),
                Body = ASTHelpers.ExtractStmtSeq(body),
                DecoratorList = ASTHelpers.ExtractExprSeq(decorator_list),
                Returns = returns as GeneratedExpr,
                TypeComment = type_comment,
                TypeParams = (type_params as GeneratedTypeParamSeq),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };

            Console.WriteLine($"[DEBUG] _PyAST_AsyncFunctionDef: Async function definition created successfully");
            return funcDef;
        }

        /// <summary>
        /// _PyPegen_make_module - Create module from statements
        /// </summary>
        protected GeneratedModule _PyPegen_make_module(GeneratedStmtSeq statements)
        {
            var module = new GeneratedModule();

            if (statements != null)
            {
                module.Body = statements;
            }
            else
            {
                module.Body = new GeneratedStmtSeq();
            }

            return module;
        }


        /// <summary>
        /// _PyPegen_seq_flatten - Flatten statement sequences (CPython 3.12 compatible)
        /// CPython 3.12: Flatten list of sequences into single sequence
        /// Used for statement+ where statement returns asdl_stmt_seq*
        /// Example: [[stmt1, stmt2], [stmt3], [stmt4]] → [stmt1, stmt2, stmt3, stmt4]
        /// </summary>
        protected GeneratedStmtSeq _PyPegen_seq_flatten(List<GeneratedStmtSeq> sequences)
        {
            var result = new GeneratedStmtSeq();
            foreach (var seq in sequences)
            {
                if (seq != null)
                {
                    result.AddRange(seq);
                }
            }
            return result;
        }


        /// <summary>
        /// Extract Call.args from an expression (if it's a Call)
        /// CPython pattern: ((expr_ty) b)->v.Call.args
        /// </summary>
        protected GeneratedExprSeq? ExtractCallArgs(object? expr)
        {
            if (expr == null) return null;

            // If it's already a Call expression with args
            if (expr is GeneratedCallExpr callExpr)
            {
                return callExpr.Args;
            }

            return null;
        }

        /// <summary>
        /// Extract Call.keywords from an expression (if it's a Call)
        /// CPython pattern: ((expr_ty) b)->v.Call.keywords
        /// </summary>
        protected GeneratedKeywordSeq? ExtractCallKeywords(object? expr)
        {
            if (expr == null) return null;

            if (expr is GeneratedCallExpr callExpr)
            {
                return callExpr.Keywords;
            }

            return null;
        }

        /// <summary>
        /// Convert GeneratedSeq to GeneratedAliasSeq for type safety
        /// </summary>
        protected GeneratedAliasSeq? AsAliasSeq(object? seq)
        {
            if (seq == null) return null;
            if (seq is GeneratedAliasSeq aliasSeq) return aliasSeq;
            if (seq is GeneratedSeq genSeq)
            {
                var result = new GeneratedAliasSeq();
                foreach (var item in genSeq)
                {
                    if (item is GeneratedAlias alias)
                        result.Add(alias);
                }
                return result;
            }
            return null;
        }

        /// <summary>
        /// Convert GeneratedSeq to GeneratedKeywordSeq for type safety
        /// </summary>
        protected GeneratedKeywordSeq? AsKeywordSeq(object? seq)
        {
            if (seq == null) return null;
            if (seq is GeneratedKeywordSeq kwSeq) return kwSeq;
            if (seq is List<object> list)
            {
                var result = new GeneratedKeywordSeq();
                foreach (var item in list)
                {
                    if (item is GeneratedKeyword kw)
                        result.Add(kw);
                }
                return result;
            }
            return null;
        }

        /// <summary>
        /// Convert GeneratedSeq to GeneratedComprehensionSeq for type safety
        /// </summary>
        protected GeneratedComprehensionSeq? AsComprehensionSeq(object? seq)
        {
            if (seq == null) return null;
            if (seq is GeneratedComprehensionSeq compSeq) return compSeq;
            if (seq is GeneratedSeq genSeq)
            {
                var result = new GeneratedComprehensionSeq();
                foreach (var item in genSeq)
                {
                    if (item is GeneratedComprehension comp)
                        result.Add(comp);
                }
                return result;
            }
            return null;
        }

        /// <summary>
        /// Convert GeneratedSeq or List<object?> to GeneratedWithItemSeq for type safety
        /// </summary>
        protected GeneratedWithItemSeq? AsWithItemSeq(object? seq)
        {
            if (seq == null) return null;
            if (seq is GeneratedWithItemSeq withSeq) return withSeq;
            if (seq is List<object?> list)
            {
                var result = new GeneratedWithItemSeq();
                foreach (var item in list)
                {
                    if (item is GeneratedWithItem withItem)
                        result.Add(withItem);
                }
                return result;
            }
            return null;
        }

        /// <summary>
        /// Convert GeneratedStmtSeq or List<object?> to GeneratedExceptHandlerSeq for type safety
        /// </summary>
        protected GeneratedExceptHandlerSeq? AsExceptHandlerSeq(object? seq)
        {
            if (seq == null) return null;
            if (seq is GeneratedExceptHandlerSeq handlerSeq) return handlerSeq;
            if (seq is List<object?> list)
            {
                var result = new GeneratedExceptHandlerSeq();
                foreach (var item in list)
                {
                    if (item is GeneratedExceptHandler handler)
                        result.Add(handler);
                }
                return result;
            }
            return null;
        }

        /// <summary>
        /// _PyPegen_seq_insert_in_front - Insert element at front of sequence
        /// CPython 3.12: asdl_seq *_PyPegen_seq_insert_in_front(Parser *p, void *a, asdl_seq *seq)
        /// </summary>
        protected GeneratedExprSeq _PyPegen_seq_insert_in_front(GeneratedExpr a, GeneratedExprSeq seq)
        {
            var result = new GeneratedExprSeq();

            // Insert 'a' at front
            if (a != null)
            {
                result.Add(a);
            }

            // Add existing sequence elements
            if (seq != null)
            {
                result.AddRange(seq);
            }

            return result;
        }

        /// <summary>
        /// _PyPegen_seq_insert_in_front - Overload for generic AST node sequences
        /// CPython 3.12: asdl_seq* can hold patterns, expressions, statements, etc.
        /// Now that GeneratedPattern inherits from GeneratedAstNode, this works for all types
        /// </summary>
        protected GeneratedSeq _PyPegen_seq_insert_in_front(GeneratedAstNode a, GeneratedSeq seq)
        {
            var result = new GeneratedSeq();

            // Insert 'a' at front
            if (a != null)
            {
                result.Add(a);
            }

            // Add existing sequence elements
            if (seq != null)
            {
                result.AddRange(seq);
            }

            return result;
        }

        /// <summary>
        /// _PyPegen_seq_insert_in_front - Overload for object sequences (from gather/loop operations)
        /// </summary>
        protected GeneratedExprSeq _PyPegen_seq_insert_in_front(GeneratedAstNode item, List<object?> sequence)
        {
            var result = new GeneratedExprSeq();

            // Insert item at front
            if (item is GeneratedExpr expr)
            {
                result.Add(expr);
            }

            // Add existing sequence elements
            if (sequence != null)
            {
                foreach (var elem in sequence)
                {
                    if (elem is GeneratedExpr e)
                    {
                        result.Add(e);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// _PyPegen_seq_append_to_end - Append element to end of sequence
        /// CPython 3.12: asdl_seq *_PyPegen_seq_append_to_end(Parser *p, asdl_seq *seq, void *element)
        /// </summary>
        protected GeneratedExprSeq _PyPegen_seq_append_to_end(GeneratedExprSeq seq, GeneratedExpr element)
        {
            var result = new GeneratedExprSeq();

            // Add existing sequence elements
            if (seq != null)
            {
                result.AddRange(seq);
            }

            // Append element at end
            if (element != null)
            {
                result.Add(element);
            }

            return result;
        }

        /// <summary>
        /// _PyPegen_singleton_seq - Create a sequence with a single element (Stmt)
        /// CPython 3.12: asdl_seq *_PyPegen_singleton_seq(Parser *p, void *element)
        /// </summary>
        protected GeneratedStmtSeq _PyPegen_singleton_seq(GeneratedStmt element)
        {
            return new GeneratedStmtSeq { element };
        }

        /// <summary>
        /// _PyPegen_singleton_seq - Create a sequence with a single element (Expr)
        /// </summary>
        protected GeneratedExprSeq _PyPegen_singleton_seq(GeneratedExpr element)
        {
            return new GeneratedExprSeq { element };
        }

        /// <summary>
        /// _PyPegen_singleton_seq - Create a sequence with a single element (Alias)
        /// </summary>
        protected GeneratedAliasSeq _PyPegen_singleton_seq(GeneratedAlias element)
        {
            return new GeneratedAliasSeq { element };
        }

        /// <summary>
        /// _PyPegen_singleton_seq - Overload for SlashWithDefault (used in invalid_* rules)
        /// CPython 3.12: These helper types are only used for error detection
        /// Return a generic sequence since the actual value is never used (RAISE_SYNTAX_ERROR)
        /// </summary>
        protected GeneratedAstNodeSeq _PyPegen_singleton_seq(GeneratedSlashWithDefault element)
        {
            var result = new GeneratedAstNodeSeq();
            if (element != null)
            {
                result.Add(element);
            }
            return result;
        }

        /// <summary>
        /// _PyPegen_singleton_seq - Overload for StarEtc (used in invalid_* rules)
        /// CPython 3.12: These helper types are only used for error detection
        /// Return a generic sequence since the actual value is never used (RAISE_SYNTAX_ERROR)
        /// </summary>
        protected GeneratedAstNodeSeq _PyPegen_singleton_seq(GeneratedStarEtc element)
        {
            var result = new GeneratedAstNodeSeq();
            if (element != null)
            {
                result.Add(element);
            }
            return result;
        }

        /// <summary>
        /// _PyPegen_alias_for_star - Create alias for 'import *'
        /// CPython 3.12: alias_ty _PyPegen_alias_for_star(Parser *p, EXTRA)
        /// </summary>
        protected GeneratedAlias _PyPegen_alias_for_star(int lineno, int col_offset, int end_lineno, int end_col_offset)
        {
            return new GeneratedAlias
            {
                Name = "*",
                AsName = null,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyPegen_get_cmpops - Extract comparison operators from CmpopExprPair sequence
        /// CPython 3.12: asdl_int_seq *_PyPegen_get_cmpops(Parser *p, asdl_seq *seq)
        /// </summary>
        protected List<CmpOpType> _PyPegen_get_cmpops(object? seq)
        {
            var result = new List<CmpOpType>();

            if (seq is List<(CmpOpType op, GeneratedExpr expr)> pairList)
            {
                foreach (var pair in pairList)
                {
                    result.Add(pair.op);
                }
            }
            else if (seq is List<object> objList)
            {
                foreach (var obj in objList)
                {
                    if (obj is ValueTuple<CmpOpType, GeneratedExpr> pair)
                    {
                        result.Add(pair.Item1);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// _PyPegen_get_exprs - Extract expressions from CmpopExprPair sequence
        /// CPython 3.12: asdl_expr_seq *_PyPegen_get_exprs(Parser *p, asdl_seq *seq)
        /// </summary>
        protected GeneratedExprSeq _PyPegen_get_exprs(object? seq)
        {
            var result = new GeneratedExprSeq();

            if (seq is List<(CmpOpType op, GeneratedExpr expr)> pairList)
            {
                foreach (var pair in pairList)
                {
                    result.Add(pair.expr);
                }
            }
            else if (seq is List<object> objList)
            {
                foreach (var obj in objList)
                {
                    if (obj is ValueTuple<CmpOpType, GeneratedExpr> pair)
                    {
                        result.Add(pair.Item2);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// _PyPegen_map_names_to_ids - Map name expressions to their IDs
        /// CPython 3.12: asdl_expr_seq *_PyPegen_map_names_to_ids(Parser *p, asdl_expr_seq *seq)
        /// </summary>
        protected GeneratedExprSeq? _PyPegen_map_names_to_ids(object? seq)
        {
            if (seq == null) return null;

            var result = new GeneratedExprSeq();

            if (seq is GeneratedExprSeq exprSeq)
            {
                foreach (var expr in exprSeq)
                {
                    if (expr is GeneratedNameExpr nameExpr)
                    {
                        // Just keep the name expression as-is for now
                        // CPython converts Name to Constant, but we'll handle this differently
                        result.Add(expr);
                    }
                    else
                    {
                        result.Add(expr);
                    }
                }
            }

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// _PyPegen_empty_arguments - Create empty argument list
        /// </summary>
        protected GeneratedArguments _PyPegen_empty_arguments()
        {
            return new GeneratedArguments();
        }

        /// <summary>
        /// _PyPegen_dummy_name - Create a dummy name for invalid expressions
        /// CPython 3.12: expr_ty _PyPegen_dummy_name(Parser *p, ...)
        /// Used for error recovery in invalid expressions
        /// </summary>
        protected GeneratedNameExpr _PyPegen_dummy_name()
        {
            return new GeneratedNameExpr
            {
                Id = "<invalid>",
                Context = "Load"
            };
        }

        /// <summary>
        /// _PyPegen_get_keys - Extract keys from key-value pairs
        /// CPython 3.12: asdl_expr_seq *_PyPegen_get_keys(Parser *p, asdl_seq *seq)
        /// </summary>
        protected GeneratedExprSeq _PyPegen_get_keys(object? seq)
        {
            var result = new GeneratedExprSeq();

            if (seq is List<(GeneratedExpr key, GeneratedExpr value)> pairList)
            {
                foreach (var pair in pairList)
                {
                    result.Add(pair.key);
                }
            }
            else if (seq is List<object> objList)
            {
                foreach (var obj in objList)
                {
                    if (obj is ValueTuple<GeneratedExpr, GeneratedExpr> pair)
                    {
                        result.Add(pair.Item1);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// _PyPegen_get_values - Extract values from key-value pairs
        /// CPython 3.12: asdl_expr_seq *_PyPegen_get_values(Parser *p, asdl_seq *seq)
        /// </summary>
        protected GeneratedExprSeq _PyPegen_get_values(object? seq)
        {
            var result = new GeneratedExprSeq();

            if (seq is List<(GeneratedExpr key, GeneratedExpr value)> pairList)
            {
                foreach (var pair in pairList)
                {
                    result.Add(pair.value);
                }
            }
            else if (seq is List<object> objList)
            {
                foreach (var obj in objList)
                {
                    if (obj is ValueTuple<GeneratedExpr, GeneratedExpr> pair)
                    {
                        result.Add(pair.Item2);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// _PyPegen_seq_extract_starred_exprs - Extract starred expressions from sequence
        /// CPython 3.12: asdl_expr_seq *_PyPegen_seq_extract_starred_exprs(Parser *p, asdl_expr_seq *seq)
        /// </summary>
        protected GeneratedExprSeq? _PyPegen_seq_extract_starred_exprs(object? seq)
        {
            if (seq == null) return null;

            var result = new GeneratedExprSeq();

            if (seq is GeneratedExprSeq exprSeq)
            {
                foreach (var expr in exprSeq)
                {
                    if (expr is GeneratedStarredExpr starredExpr && starredExpr.Value != null)
                    {
                        result.Add(starredExpr.Value);
                    }
                }
            }

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// _PyPegen_seq_delete_starred_exprs - Remove starred expressions from sequence
        /// CPython 3.12: asdl_expr_seq *_PyPegen_seq_delete_starred_exprs(Parser *p, asdl_expr_seq *seq)
        /// </summary>
        protected GeneratedExprSeq? _PyPegen_seq_delete_starred_exprs(object? seq)
        {
            if (seq == null) return null;

            var result = new GeneratedExprSeq();

            if (seq is GeneratedExprSeq exprSeq)
            {
                foreach (var expr in exprSeq)
                {
                    if (expr is not GeneratedStarredExpr)
                    {
                        result.Add(expr);
                    }
                }
            }

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// _PyAST_Pass - Create pass statement
        /// </summary>
        protected GeneratedPassStmt _PyAST_Pass(int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedPassStmt
            {
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Return - Create return statement
        /// </summary>
        protected GeneratedReturnStmt _PyAST_Return(object value, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedReturnStmt
            {
                Value = value as GeneratedExpr,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Expr - Create expression statement
        /// </summary>
        protected GeneratedExprStmt _PyAST_Expr(object value, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedExprStmt
            {
                Value = ASTHelpers.ExtractExpr(value),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Constant - Create constant expression
        /// CPython 3.12: _PyAST_Constant(value, kind, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedConstantExpr _PyAST_Constant(object value, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            // CPython 3.12: Convert value to PyObject
            PyObject pyValue = value switch
            {
                PyObject pyObj => pyObj,
                string str => new PyString(str),
                int i => new PyInt(i),
                long l => new PyInt((int)l),
                double d => new PyFloat(d),
                float f => new PyFloat(f),
                bool b => b ? PyBool.True : PyBool.False,
                null => PyNone.Instance,
                _ => new PyString(value.ToString() ?? "")
            };

            return new GeneratedConstantExpr
            {
                Value = pyValue,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Assign - Create assignment statement (single target)
        /// CPython 3.12: _PyAST_Assign(targets, value, type_comment, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedAssignStmt _PyAST_Assign(object target, object value, object? type_comment = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            // CPython expects targets (plural), so wrap single target in a list
            var targets = new GeneratedExprSeq();
            if (target is GeneratedExpr expr)
                targets.Add(expr);

            // Extract type comment string from token (NEW_TYPE_COMMENT macro)
            string? tcStr = null;
            if (type_comment != null)
            {
                tcStr = ASTHelpers.ExtractStringValue(type_comment);
            }

            return new GeneratedAssignStmt
            {
                Targets = targets,
                Value = ASTHelpers.ExtractExpr(value),
                TypeComment = tcStr,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Assign - Create assignment statement (multiple targets for chained assignment)
        /// CPython 3.12: a = b = c = value
        /// </summary>
        protected GeneratedAssignStmt _PyAST_Assign(List<object> targets, object value, string? type_comment = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            var targetSeq = new GeneratedExprSeq();
            foreach (var target in targets)
            {
                if (target is GeneratedExpr expr)
                    targetSeq.Add(expr);
            }

            return new GeneratedAssignStmt
            {
                Targets = targetSeq,
                Value = ASTHelpers.ExtractExpr(value),
                TypeComment = type_comment,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_AnnAssign - Create annotated assignment statement (simple version)
        /// CPython 3.12: _PyAST_AnnAssign(target, annotation, value, simple, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedAnnAssignStmt _PyAST_AnnAssign(object target, object annotation, object value = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedAnnAssignStmt
            {
                Target = ASTHelpers.ExtractExpr(target),
                Annotation = ASTHelpers.ExtractExpr(annotation),
                Value = value != null ? ASTHelpers.ExtractExpr(value) : null,
                Simple = true,  // Default to simple=1
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_AnnAssign - Create annotated assignment statement (with simple flag)
        /// CPython 3.12: _PyAST_AnnAssign(target, annotation, value, simple, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedAnnAssignStmt _PyAST_AnnAssign(object target, object annotation, object value = null, int simple = 1, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedAnnAssignStmt
            {
                Target = ASTHelpers.ExtractExpr(target),
                Annotation = ASTHelpers.ExtractExpr(annotation),
                Value = value != null ? ASTHelpers.ExtractExpr(value) : null,
                Simple = simple != 0,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_AugAssign - Create augmented assignment statement (+=, -=, etc.)
        /// CPython 3.12: _PyAST_AugAssign(target, op, value, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedAugAssignStmt _PyAST_AugAssign(object target, object op, object value, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            // Extract operator string from object
            string opStr = op?.ToString() ?? "";

            return new GeneratedAugAssignStmt
            {
                Target = ASTHelpers.ExtractExpr(target),
                Op = opStr,
                Value = ASTHelpers.ExtractExpr(value),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
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
        protected GeneratedExpr _PyPegen_set_expr_context(GeneratedExpr expr, ExprContext context)
        {
            if (expr != null)
            {
                expr.Context = context.ToString();
            }
            return expr;
        }

        /// <summary>
        /// _PyPegen_set_expr_context - Set expression context for token (overload for NAME tokens)
        /// CPython 3.12: Automatically converts NAME token to Name expression
        /// </summary>
        protected GeneratedExpr _PyPegen_set_expr_context(GeneratedTokenInfo token, ExprContext context)
        {
            if (token != null && token.Type == GeneratedTokenType.NAME)
            {
                var nameExpr = new GeneratedNameExpr
                {
                    Id = token.Value,
                    Context = context.ToString(),
                    LineNo = token.Line,
                    ColOffset = token.Column,
                    EndLineNo = token.Line,  // Single token: same line
                    EndColOffset = token.Column + token.Value.Length
                };
                return nameExpr;
            }
            return null;
        }

        /// <summary>
        /// _PyAST_Name - Create name expression (CPython 3.12 compatible)
        /// CPython: Name(id='x', ctx=Load())
        /// CPython 3.12: _PyAST_Name(id, ctx, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedNameExpr _PyAST_Name(string id, object context = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedNameExpr
            {
                Id = id,
                Context = context?.ToString() ?? "Load",
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_If - Create if statement
        /// CPython 3.12: _PyAST_If(test, body, orelse, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedIfStmt _PyAST_If(object test, object body, object orelse = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedIfStmt
            {
                Test = ASTHelpers.ExtractExpr(test),
                Body = ASTHelpers.ExtractStmtSeq(body),
                OrElse = ASTHelpers.ExtractStmtSeq(orelse),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_While - Create while statement
        /// CPython 3.12: _PyAST_While(test, body, orelse, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedWhileStmt _PyAST_While(object test, object body, object orelse = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedWhileStmt
            {
                Test = ASTHelpers.ExtractExpr(test),
                Body = ASTHelpers.ExtractStmtSeq(body),
                OrElse = ASTHelpers.ExtractStmtSeq(orelse),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_For - Create for statement
        /// CPython 3.12: _PyAST_For(target, iter, body, orelse, type_comment, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedForStmt _PyAST_For(object target, object iter, object body, object orelse = null, object type_comment = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedForStmt
            {
                Target = ASTHelpers.ExtractExpr(target),
                Iter = ASTHelpers.ExtractExpr(iter),
                Body = ASTHelpers.ExtractStmtSeq(body),
                OrElse = ASTHelpers.ExtractStmtSeq(orelse),
                TypeComment = type_comment?.ToString(),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// CPython 3.12: _PyAST_AsyncFor - async for statement
        /// </summary>
        protected GeneratedAsyncForStmt _PyAST_AsyncFor(object target, object iter, object body, object orelse = null, object type_comment = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedAsyncForStmt
            {
                Target = ASTHelpers.ExtractExpr(target),
                Iter = ASTHelpers.ExtractExpr(iter),
                Body = ASTHelpers.ExtractStmtSeq(body),
                OrElse = ASTHelpers.ExtractStmtSeq(orelse),
                TypeComment = type_comment?.ToString(),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Break - Create break statement
        /// </summary>
        protected GeneratedBreakStmt _PyAST_Break(int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedBreakStmt
            {
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Continue - Create continue statement
        /// </summary>
        protected GeneratedContinueStmt _PyAST_Continue(int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedContinueStmt
            {
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Match - Create match statement (Python 3.10+)
        /// CPython 3.12: _PyAST_Match(subject, cases, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedMatchStmt _PyAST_Match(object subject, GeneratedMatchCaseSeq? cases, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedMatchStmt
            {
                Subject = ASTHelpers.ExtractExpr(subject),
                Cases = cases ?? new GeneratedMatchCaseSeq(),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Global - Create global statement
        /// CPython 3.12: _PyAST_Global(names, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedGlobalStmt _PyAST_Global(object names = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            var namesList = new List<string>();
            if (names is List<object> list)
            {
                foreach (var name in list)
                    namesList.Add(name?.ToString() ?? "");
            }
            else if (names is List<string> strList)
            {
                namesList = strList;
            }

            return new GeneratedGlobalStmt
            {
                Names = namesList,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Nonlocal - Create nonlocal statement
        /// CPython 3.12: _PyAST_Nonlocal(names, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedNonlocalStmt _PyAST_Nonlocal(object names = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            var namesList = new List<string>();
            if (names is List<object> list)
            {
                foreach (var name in list)
                    namesList.Add(name?.ToString() ?? "");
            }
            else if (names is List<string> strList)
            {
                namesList = strList;
            }

            return new GeneratedNonlocalStmt
            {
                Names = namesList,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Import - Create import statement
        /// CPython 3.12: _PyAST_Import(names, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedImportStmt _PyAST_Import(object? names, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedImportStmt
            {
                Names = AsAliasSeq(names) ?? new GeneratedAliasSeq(),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_ImportFrom - Create from import statement
        /// CPython 3.12: _PyAST_ImportFrom(module, names, level, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedImportFromStmt _PyAST_ImportFrom(object module = null, object? names = null, object level = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            int levelInt = 0;
            if (level is int i)
                levelInt = i;
            else if (level != null)
                int.TryParse(level.ToString(), out levelInt);

            return new GeneratedImportFromStmt
            {
                Module = module?.ToString(),
                Names = AsAliasSeq(names) ?? new GeneratedAliasSeq(),
                Level = levelInt,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_ClassDef - Create class definition
        /// CPython 3.12: _PyAST_ClassDef(name, bases, keywords, body, decorator_list, type_params, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedClassDefStmt _PyAST_ClassDef(object name, object? bases, object? keywords, object body,
            object? decorator_list = null, object? type_params = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            var stmt = new GeneratedClassDefStmt
            {
                Name = ASTHelpers.ExtractStringValue(name),
                Body = ASTHelpers.ExtractStmtSeq(body),
                Bases = ASTHelpers.ExtractExprSeq(bases),
                Keywords = ASTHelpers.ExtractExprSeq(keywords),
                DecoratorList = ASTHelpers.ExtractExprSeq(decorator_list),
                TypeParams = (type_params as GeneratedTypeParamSeq),
                // CPython 3.12 EXTRA: Store position information
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
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

            return new GeneratedAugAssignStmt
            {
                Target = (GeneratedExpr)target,
                Op = binaryOp,
                Value = (GeneratedExpr)value
            };
        }

        /// <summary>
        /// _PyPegen_seq_extract_starred_exprs - Extract starred expressions from sequence
        /// </summary>
        protected GeneratedExprSeq _PyPegen_seq_extract_starred_exprs(GeneratedExprSeq sequence)
        {
            var result = new GeneratedExprSeq();
            if (sequence != null)
            {
                foreach (var item in sequence)
                {
                    if (item is GeneratedStarredExpr)
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
                    if (item is GeneratedStarredExpr)
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
        /// CPython 3.12: Counts '.' and '...' tokens in from imports
        /// </summary>
        protected int _PyPegen_seq_count_dots(List<GeneratedTokenInfo> sequence)
        {
            int count = 0;
            if (sequence != null)
            {
                foreach (var token in sequence)
                {
                    if (token?.Value == ".")
                    {
                        count++;
                    }
                    else if (token?.Value == "...")
                    {
                        count += 3; // '...' is 3 dots
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
        protected GeneratedJoinedStrExpr _PyPegen_joined_str(object start, List<object> middle, object end)
        {
            var values = new GeneratedExprSeq();
            if (start != null && start is GeneratedExpr startExpr)
                values.Add(startExpr);
            if (middle != null)
            {
                foreach (var item in middle)
                {
                    if (item is GeneratedExpr expr)
                        values.Add(expr);
                }
            }
            if (end != null && end is GeneratedExpr endExpr)
                values.Add(endExpr);

            return new GeneratedJoinedStrExpr { Values = values };
        }

        /// <summary>
        /// _PyPegen_concatenate_strings - Concatenate string literals and f-strings
        /// </summary>
        protected GeneratedExpr _PyPegen_concatenate_strings(List<object> strings)
        {
            if (strings == null || strings.Count == 0)
            {
                return new GeneratedConstantExpr { Value = new PyString("") };
            }

            if (strings.Count == 1)
            {
                var single = strings[0];
                if (single is GeneratedExpr expr)
                {
                    return expr;
                }
                // Single non-expression item, wrap as constant - CPython 3.12
                var pyVal = single switch
                {
                    PyObject py => py,
                    string s => new PyString(s),
                    _ => new PyString(single?.ToString() ?? "")
                };
                return new GeneratedConstantExpr { Value = pyVal };
            }

            // Multiple strings - create JoinedStr for concatenation
            var values = new GeneratedExprSeq();
            foreach (var item in strings)
            {
                if (item is GeneratedExpr expr)
                    values.Add(expr);
                else
                    values.Add(new GeneratedConstantExpr { Value = new PyString(item?.ToString() ?? "") });
            }
            return new GeneratedJoinedStrExpr { Values = values };
        }

        /// <summary>
        /// _PyPegen_constant_from_token - Create constant from token
        /// CPython 3.12: Convert token value to PyObject
        /// </summary>
        protected GeneratedConstantExpr _PyPegen_constant_from_token(object token)
        {
            if (token is GeneratedTokenInfo genToken)
            {
                // CPython 3.12: Parse string to appropriate PyObject type
                var strValue = genToken.Value;
                PyObject pyValue;

                if (int.TryParse(strValue, out int intVal))
                    pyValue = new PyInt(intVal);
                else if (double.TryParse(strValue, out double floatVal))
                    pyValue = new PyFloat(floatVal);
                else
                    pyValue = new PyString(strValue);

                return new GeneratedConstantExpr { Value = pyValue };
            }
            else
            {
                return new GeneratedConstantExpr { Value = new PyString(token?.ToString() ?? "") };
            }
        }

        /// <summary>
        /// _PyPegen_decoded_constant_from_token - Create decoded constant from token
        /// CPython 3.12: Convert token value to PyObject with decoding
        /// </summary>
        protected GeneratedConstantExpr _PyPegen_decoded_constant_from_token(object token)
        {
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
                return new GeneratedConstantExpr { Value = new PyString(value) };
            }
            else
            {
                return new GeneratedConstantExpr { Value = new PyString(token?.ToString() ?? "") };
            }
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
        /// CPython 3.12: _PyAST_FormattedValue(value, conversion, format_spec, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedFormattedValueExpr _PyAST_FormattedValue(object value, object conversion = null, object formatSpec = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            int conversionInt = -1;
            if (conversion != null)
            {
                if (conversion is int i) conversionInt = i;
                else if (int.TryParse(conversion.ToString(), out var parsed)) conversionInt = parsed;
            }

            return new GeneratedFormattedValueExpr
            {
                Value = ASTHelpers.ExtractExpr(value),
                Conversion = conversionInt,
                FormatSpec = formatSpec != null ? ASTHelpers.ExtractExpr(formatSpec) : null,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_JoinedStr - Create joined string AST node for f-strings
        /// CPython 3.12: _PyAST_JoinedStr(values, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedJoinedStrExpr _PyAST_JoinedStr(List<object> values, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedJoinedStrExpr
            {
                Values = ASTHelpers.ExtractExprSeq(values),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Await - Create await expression AST node
        /// CPython 3.12: _PyAST_Await(value, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedAwaitExpr _PyAST_Await(object value, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedAwaitExpr
            {
                Value = ASTHelpers.ExtractExpr(value),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_TypeVar - Create TypeVar expression AST node for PEP 695
        /// CPython 3.12: _PyAST_TypeVar(name, bound, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// TODO: Create GeneratedTypeVarExpr in GeneratedAstTypes.cs
        /// </summary>
        protected GeneratedExpr _PyAST_TypeVar(string name, object bound = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            // Temporary: Use NameExpr until we have GeneratedTypeVarExpr
            return new GeneratedNameExpr
            {
                Id = name,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_TypeVarTuple - Create TypeVarTuple expression AST node for PEP 695
        /// CPython 3.12: _PyAST_TypeVarTuple(name, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// TODO: Create GeneratedTypeVarTupleExpr in GeneratedAstTypes.cs
        /// </summary>
        protected GeneratedExpr _PyAST_TypeVarTuple(string name, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            // Temporary: Use NameExpr until we have GeneratedTypeVarTupleExpr
            return new GeneratedNameExpr
            {
                Id = name,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_ParamSpec - Create ParamSpec expression AST node for PEP 695
        /// CPython 3.12: _PyAST_ParamSpec(name, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// TODO: Create GeneratedParamSpecExpr in GeneratedAstTypes.cs
        /// </summary>
        protected GeneratedExpr _PyAST_ParamSpec(string name, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            // Temporary: Use NameExpr until we have GeneratedParamSpecExpr
            return new GeneratedNameExpr
            {
                Id = name,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Try - Create try statement AST node
        /// CPython 3.12: _PyAST_Try(body, handlers, orelse, finalbody, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedTryStmt _PyAST_Try(object body, object? handlers = null, object orelse = null, object finalbody = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedTryStmt
            {
                Body = ASTHelpers.ExtractStmtSeq(body),
                Handlers = AsExceptHandlerSeq(handlers) ?? new GeneratedExceptHandlerSeq(),
                OrElse = ASTHelpers.ExtractStmtSeq(orelse),
                FinallyBody = ASTHelpers.ExtractStmtSeq(finalbody),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_TryStar - Create try statement AST node with except* handlers (PEP 654)
        /// CPython 3.12: _PyAST_TryStar(body, handlers, orelse, finalbody, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedTryStarStmt _PyAST_TryStar(object body, object? handlers = null, object orelse = null, object finalbody = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedTryStarStmt
            {
                Body = ASTHelpers.ExtractStmtSeq(body),
                Handlers = AsExceptHandlerSeq(handlers) ?? new GeneratedExceptHandlerSeq(),
                OrElse = ASTHelpers.ExtractStmtSeq(orelse),
                FinallyBody = ASTHelpers.ExtractStmtSeq(finalbody),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
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

            // Create proper import from statement for __future__ imports
            var names = new GeneratedAliasSeq();
            names.Add(new GeneratedAlias { Name = featureName, AsName = alias?.ToString() });

            var importFrom = new GeneratedImportFromStmt
            {
                Module = "__future__",
                Names = names,
                Level = level
            };
            return importFrom;
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
        /// CPython 3.12: _PyAST_arg(arg, annotation, type_comment, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// TODO: Create GeneratedArgExpr or use a proper argument type
        /// </summary>
        protected GeneratedExpr _PyAST_arg(string name, object? annotation = null, string? type_comment = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            // Temporary: Use NameExpr with annotation stored separately
            // In the future, create a proper GeneratedArgExpr type
            var nameExpr = new GeneratedNameExpr
            {
                Id = name,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
            return nameExpr;
        }

        // ===== Missing _PyAST_* Expression Methods =====

        /// <summary>
        /// _PyAST_Attribute - Create attribute access expression
        /// CPython 3.12: _PyAST_Attribute(value, attr, ctx, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedAttributeExpr _PyAST_Attribute(object value = null, object attr = null, object context = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedAttributeExpr
            {
                Value = ASTHelpers.ExtractExpr(value),
                Attr = ASTHelpers.ExtractStringValue(attr),
                Context = context?.ToString() ?? "Load",
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }
        /// <summary>
        /// _PyAST_BoolOp - Create boolean operation expression
        /// CPython 3.12: _PyAST_BoolOp(op, values, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedBoolOpExpr _PyAST_BoolOp(BoolOpType op, object values, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedBoolOpExpr
            {
                Op = op == BoolOpType.Or ? "Or" : "And",
                Values = ASTHelpers.ExtractExprSeq(values),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Compare - Create comparison expression
        /// CPython 3.12: _PyAST_Compare(left, ops, comparators, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedCompareExpr _PyAST_Compare(object left, object ops, object comparators, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            // Convert CmpOpType list to string list
            List<string> opStrings;
            if (ops is List<CmpOpType> cmpOps)
            {
                opStrings = cmpOps.Select(op => op.ToString()).ToList();
            }
            else
            {
                opStrings = ASTHelpers.ExtractCmpOpSeq(ops);
            }

            return new GeneratedCompareExpr
            {
                Left = ASTHelpers.ExtractExpr(left),
                Ops = opStrings,
                Comparators = ASTHelpers.ExtractExprSeq(comparators),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_BinOp - Create binary operation expression
        /// CPython 3.12: _PyAST_BinOp(left, op, right, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedBinOpExpr _PyAST_BinOp(object left, object op, object right, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedBinOpExpr
            {
                Left = ASTHelpers.ExtractExpr(left),
                Op = op?.ToString() ?? "",
                Right = ASTHelpers.ExtractExpr(right),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_UnaryOp - Create unary operation expression
        /// CPython 3.12: _PyAST_UnaryOp(op, operand, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedUnaryOpExpr _PyAST_UnaryOp(object op, object operand, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedUnaryOpExpr
            {
                Op = op?.ToString() ?? "",
                Operand = ASTHelpers.ExtractExpr(operand),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_IfExp - Create conditional expression (ternary operator)
        /// CPython 3.12: _PyAST_IfExp(test, body, orelse, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedIfExpExpr _PyAST_IfExp(object test, object body, object orelse, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedIfExpExpr
            {
                Test = ASTHelpers.ExtractExpr(test),
                Body = ASTHelpers.ExtractExpr(body),
                OrElse = ASTHelpers.ExtractExpr(orelse),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Lambda - Create lambda expression
        /// CPython 3.12: _PyAST_Lambda(args, body, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedLambdaExpr _PyAST_Lambda(object? args = null, object body = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedLambdaExpr
            {
                Arguments = (args as GeneratedArguments),
                Body = ASTHelpers.ExtractExpr(body),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Call - Create function call expression
        /// CPython 3.12: _PyAST_Call(func, args, keywords, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedCallExpr _PyAST_Call(object func = null, object args = null, object? keywords = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedCallExpr
            {
                Func = ASTHelpers.ExtractExpr(func),
                Args = ASTHelpers.ExtractExprSeq(args),
                Keywords = AsKeywordSeq(keywords) ?? new GeneratedKeywordSeq(),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Subscript - Create subscript expression (indexing)
        /// CPython 3.12: _PyAST_Subscript(value, slice, ctx, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedSubscriptExpr _PyAST_Subscript(object value, object slice, object context = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedSubscriptExpr
            {
                Value = ASTHelpers.ExtractExpr(value),
                Slice = ASTHelpers.ExtractExpr(slice),
                Context = context?.ToString() ?? "Load",
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Slice - Create slice expression
        /// CPython 3.12: _PyAST_Slice(lower, upper, step, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedSliceExpr _PyAST_Slice(object lower = null, object upper = null, object step = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedSliceExpr
            {
                Lower = lower != null ? ASTHelpers.ExtractExpr(lower) : null,
                Upper = upper != null ? ASTHelpers.ExtractExpr(upper) : null,
                Step = step != null ? ASTHelpers.ExtractExpr(step) : null,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Starred - Create starred expression (*x)
        /// CPython 3.12: _PyAST_Starred(value, ctx, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedStarredExpr _PyAST_Starred(object value = null, object context = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedStarredExpr
            {
                Value = ASTHelpers.ExtractExpr(value),
                Context = context?.ToString() ?? "Load",
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Yield - Create yield expression
        /// CPython 3.12: _PyAST_Yield(value, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedYieldExpr _PyAST_Yield(object value = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedYieldExpr
            {
                Value = value != null ? ASTHelpers.ExtractExpr(value) : null,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_YieldFrom - Create yield from expression
        /// CPython 3.12: _PyAST_YieldFrom(value, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedYieldFromExpr _PyAST_YieldFrom(object value, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedYieldFromExpr
            {
                Value = ASTHelpers.ExtractExpr(value),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        // ===== Container Expression Constructors =====

        /// <summary>
        /// _PyAST_List - Create list expression
        /// CPython 3.12: _PyAST_List(elts, ctx, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedListExpr _PyAST_List(object elts, object context = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedListExpr
            {
                Elements = ASTHelpers.ExtractExprSeq(elts),
                Context = context?.ToString() ?? "Load",
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Tuple - Create tuple expression
        /// CPython 3.12: _PyAST_Tuple(elts, ctx, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedTupleExpr _PyAST_Tuple(object elts = null, object context = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedTupleExpr
            {
                Elements = ASTHelpers.ExtractExprSeq(elts),
                Context = context?.ToString() ?? "Load",
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Set - Create set expression
        /// CPython 3.12: _PyAST_Set(elts, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedSetExpr _PyAST_Set(object elts, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedSetExpr
            {
                Elements = ASTHelpers.ExtractExprSeq(elts),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Dict - Create dictionary expression
        /// CPython 3.12: _PyAST_Dict(keys, values, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedDictExpr _PyAST_Dict(object keys = null, object values = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedDictExpr
            {
                Keys = ASTHelpers.ExtractExprSeq(keys),
                Values = ASTHelpers.ExtractExprSeq(values),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        // ===== Comprehension Constructors =====

        /// <summary>
        /// _PyAST_ListComp - Create list comprehension
        /// CPython 3.12: _PyAST_ListComp(elt, generators, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedListCompExpr _PyAST_ListComp(object elt, object? generators, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedListCompExpr
            {
                Element = ASTHelpers.ExtractExpr(elt),
                Generators = AsComprehensionSeq(generators) ?? new GeneratedComprehensionSeq(),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_SetComp - Create set comprehension
        /// CPython 3.12: _PyAST_SetComp(elt, generators, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedSetCompExpr _PyAST_SetComp(object elt, object? generators, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedSetCompExpr
            {
                Element = ASTHelpers.ExtractExpr(elt),
                Generators = AsComprehensionSeq(generators) ?? new GeneratedComprehensionSeq(),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_DictComp - Create dictionary comprehension
        /// CPython 3.12: _PyAST_DictComp(key, value, generators, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedDictCompExpr _PyAST_DictComp(object key, object value, object? generators, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedDictCompExpr
            {
                Key = ASTHelpers.ExtractExpr(key),
                Value = ASTHelpers.ExtractExpr(value),
                Generators = AsComprehensionSeq(generators) ?? new GeneratedComprehensionSeq(),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_GeneratorExp - Create generator expression
        /// CPython 3.12: _PyAST_GeneratorExp(elt, generators, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedGeneratorExpExpr _PyAST_GeneratorExp(object elt, object? generators, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedGeneratorExpExpr
            {
                Element = ASTHelpers.ExtractExpr(elt),
                Generators = AsComprehensionSeq(generators) ?? new GeneratedComprehensionSeq(),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        // ===== Statement Constructors =====

        /// <summary>
        /// _PyAST_Raise - Create raise statement
        /// CPython 3.12: _PyAST_Raise(exc, cause, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedRaiseStmt _PyAST_Raise(object exc = null, object cause = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedRaiseStmt
            {
                Exc = exc != null ? ASTHelpers.ExtractExpr(exc) : null,
                Cause = cause != null ? ASTHelpers.ExtractExpr(cause) : null,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Assert - Create assert statement
        /// CPython 3.12: _PyAST_Assert(test, msg, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedAssertStmt _PyAST_Assert(object test, object msg = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedAssertStmt
            {
                Test = ASTHelpers.ExtractExpr(test),
                Msg = msg != null ? ASTHelpers.ExtractExpr(msg) : null,
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_Delete - Create delete statement
        /// CPython 3.12: _PyAST_Delete(targets, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedDeleteStmt _PyAST_Delete(object targets, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedDeleteStmt
            {
                Targets = ASTHelpers.ExtractExprSeq(targets),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// _PyAST_With - Create with statement
        /// CPython 3.12: _PyAST_With(items, body, type_comment, lineno, col_offset, end_lineno, end_col_offset, arena)
        /// </summary>
        protected GeneratedWithStmt _PyAST_With(object? items, object body, object type_comment = null, int lineno = 0, int col_offset = 0, int end_lineno = 0, int end_col_offset = 0)
        {
            return new GeneratedWithStmt
            {
                Items = AsWithItemSeq(items) ?? new GeneratedWithItemSeq(),
                Body = ASTHelpers.ExtractStmtSeq(body),
                TypeComment = type_comment?.ToString(),
                LineNo = lineno,
                ColOffset = col_offset,
                EndLineNo = end_lineno,
                EndColOffset = end_col_offset
            };
        }

        /// <summary>
        /// Helper function to add type comment to argument
        /// </summary>
        protected GeneratedExpr _PyPegen_add_type_comment_to_arg(object p, GeneratedExpr arg, object? type_comment)
        {
            // For now, just return the arg as-is since we're using NameExpr for args
            // In the future, when we have GeneratedArgExpr, we can store type_comment there
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

    // Helper methods for AST construction
    public static class ASTHelpers
    {
        /// <summary>
        /// Extract string value from various types (GeneratedToken, string, object)
        /// Handles CPython pattern: a->v.Name.id where 'a' is a NAME token
        /// </summary>
        public static string ExtractStringValue(object? obj)
        {
            if (obj == null)
                return "";

            // Already a string
            if (obj is string str)
                return str;

            // GeneratedToken with StringValue
            var type = obj.GetType();
            var stringValueProp = type.GetProperty("StringValue");
            if (stringValueProp != null)
            {
                var value = stringValueProp.GetValue(obj);
                if (value is string sv)
                    return sv;
            }

            // Try ToString
            return obj.ToString() ?? "";
        }

        /// <summary>
        /// Extract operator kind from token object
        /// Handles CPython pattern: b->kind where 'b' is an operator token
        /// Used in augmented assignment: _PyAST_AugAssign(a, b->kind, c, EXTRA)
        /// </summary>
        public static string ExtractOpKind(object? obj)
        {
            if (obj == null)
                return "";

            // If it's a token, extract the operator string
            var type = obj.GetType();
            var valueProp = type.GetProperty("Value");
            if (valueProp != null)
            {
                var value = valueProp.GetValue(obj);
                if (value is string sv)
                    return sv;
            }

            // Try StringValue property
            var stringValueProp = type.GetProperty("StringValue");
            if (stringValueProp != null)
            {
                var value = stringValueProp.GetValue(obj);
                if (value is string sv)
                    return sv;
            }

            // Try ToString
            return obj.ToString() ?? "";
        }

        /// <summary>
        /// Extract GeneratedStmtSeq from parser result
        /// </summary>
        public static GeneratedStmtSeq ExtractStmtSeq(object? obj)
        {
            if (obj == null)
                return new GeneratedStmtSeq();

            if (obj is GeneratedStmtSeq seq)
                return seq;

            if (obj is List<GeneratedStmt> list)
            {
                var result = new GeneratedStmtSeq();
                result.AddRange(list);
                return result;
            }

            // Single statement - wrap in sequence
            if (obj is GeneratedStmt stmt)
                return new GeneratedStmtSeq { stmt };

            return new GeneratedStmtSeq();
        }

        /// <summary>
        /// Extract GeneratedExprSeq from parser result
        /// </summary>
        public static GeneratedExprSeq ExtractExprSeq(object? obj)
        {
            if (obj == null)
                return new GeneratedExprSeq();

            if (obj is GeneratedExprSeq seq)
                return seq;

            if (obj is List<GeneratedExpr> list)
            {
                var result = new GeneratedExprSeq();
                result.AddRange(list);
                return result;
            }

            // Single expression - wrap in sequence
            if (obj is GeneratedExpr expr)
                return new GeneratedExprSeq { expr };

            return new GeneratedExprSeq();
        }

        /// <summary>
        /// Extract single GeneratedExpr from parser result
        /// </summary>
        public static GeneratedExpr ExtractExpr(object? obj)
        {
            if (obj == null)
                return null!;

            if (obj is GeneratedExpr expr)
                return expr;

            // If it's a wrapped expression, try to unwrap
            return obj as GeneratedExpr ?? null!;
        }

        /// <summary>
        /// Extract comparison operator sequence from parser result
        /// CPython 3.12: asdl_int_seq* of comparison operators
        /// </summary>
        public static List<string> ExtractCmpOpSeq(object? obj)
        {
            if (obj == null)
                return new List<string>();

            if (obj is List<string> strList)
                return strList;

            if (obj is List<object> objList)
            {
                return objList.Select(o => o?.ToString() ?? "").ToList();
            }

            // Single operator - wrap in list
            return new List<string> { obj.ToString() ?? "" };
        }
    }

    /// <summary>
    /// CPython 3.12: Boolean operator type
    /// typedef enum _boolop { And=1, Or=2 } boolop_ty;
    /// </summary>
    public enum BoolOpType
    {
        And = 1,
        Or = 2
    }

    /// <summary>
    /// CPython 3.12: Comparison operator type
    /// typedef enum _cmpop { Eq=1, NotEq=2, Lt=3, LtE=4, Gt=5, GtE=6, Is=7, IsNot=8, In=9, NotIn=10 } cmpop_ty;
    /// </summary>
    public enum CmpOpType
    {
        Eq = 1,      // ==
        NotEq = 2,   // !=
        Lt = 3,      // <
        LtE = 4,     // <=
        Gt = 5,      // >
        GtE = 6,     // >=
        Is = 7,      // is
        IsNot = 8,   // is not
        In = 9,      // in
        NotIn = 10   // not in
    }

    /// <summary>
    /// CPython 3.12: Expression context
    /// typedef enum _expr_context { Load=1, Store=2, Del=3 } expr_context_ty;
    /// </summary>
    public enum ExprContext
    {
        Load = 1,
        Store = 2,
        Del = 3
    }

    /// <summary>
    /// CPython 3.12: Unary operator type
    /// typedef enum _unaryop { Invert=1, Not=2, UAdd=3, USub=4 } unaryop_ty;
    /// </summary>
    public enum UnaryOpType
    {
        Invert = 1,  // ~
        Not = 2,     // not
        UAdd = 3,    // +
        USub = 4     // -
    }

    /// <summary>
    /// CPython 3.12: Binary operator type
    /// typedef enum _operator { Add=1, Sub=2, Mult=3, MatMult=4, Div=5, Mod=6, Pow=7, LShift=8, RShift=9, BitOr=10, BitXor=11, BitAnd=12, FloorDiv=13 } operator_ty;
    /// </summary>
    public enum BinOpType
    {
        Add = 1,       // +
        Sub = 2,       // -
        Mult = 3,      // *
        MatMult = 4,   // @
        Div = 5,       // /
        Mod = 6,       // %
        Pow = 7,       // **
        LShift = 8,    // <<
        RShift = 9,    // >>
        BitOr = 10,    // |
        BitXor = 11,   // ^
        BitAnd = 12,   // &
        FloorDiv = 13  // //
    }
}