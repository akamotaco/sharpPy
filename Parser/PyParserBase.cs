// PyParserBase - Base class for PEG parser
// CPython 3.12 compatible - Manual implementation (not generated)
// This file provides common parsing functionality for all PEG parsers

using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.Generated;

namespace SharpPy.Generated
{
    // ============================================================
    // PyParserBase - Base parser class
    // ============================================================

    /// <summary>
    /// Base class for generated parser - provides common parsing logic
    /// CPython 3.12: Parser/pegen.c equivalent
    /// </summary>
    public abstract class PyParserBase<TResult>
    {
        protected List<GeneratedTokenInfo> _tokens;
        protected int _position = 0;  // CPython: mark
        protected string _filename;
        protected string? _source = null;  // CPython 3.12: Source code for error reporting
        protected string[]? _sourceLines = null;  // CPython 3.12: Source lines for error reporting
        protected string? _pendingSyntaxError = null;
        protected int _pendingErrorPosition = -1;
        protected bool _callInvalidRules = true;
        // CPython 3.12: Memo cache uses (position, rule_name) tuple keys
        protected Dictionary<(int, string), GeneratedAstNode> _memoCache = new();

        // CPython Parser fields for recursion and error tracking
        protected int _level = 0;  // Nesting depth for recursion limit
        protected GeneratedTokenInfo _knownErrToken = null;  // Error location tracking
        protected int _errorIndicator = 0;  // Error state flag
        protected const int MAX_RECURSION_DEPTH = 1000;  // Python's recursion limit

        // Position tracking for AST node creation
        // CPython 3.12: EXTRA macro captures token positions for AST nodes
        protected int _startMark = -1;  // Position at start of current rule
        protected GeneratedTokenInfo? _startToken = null;  // Token at start of current rule
        protected GeneratedTokenInfo? _lastToken = null;  // Last consumed token

        // Left-recursion handling (Warth et al. algorithm)
        protected class LREntry
        {
            public GeneratedPtr Result { get; set; }
            public int EndPos { get; set; }
            public bool IsGrowing { get; set; }
        }
        protected Dictionary<(int, string), LREntry> _lrCache = new();

        // CPython 3.12: ResultTokenWithMetadata for f-string conversions/formats
        // Must inherit from GeneratedPtr to be used as optional rule result
        protected class ResultTokenWithMetadata : GeneratedPtr
        {
            public GeneratedTokenInfo Token { get; set; }
            public object Metadata { get; set; }
        }

        // CPython 3.12: Token-based memoization
        // Each Token owns its Memo list - no global cache needed
        // MemoEntry is defined in GeneratedTokenInfo (Tokenizer.cs)

        protected PyParserBase(List<GeneratedTokenInfo> tokens, string filename)
        {
            // CPython 3.12: Filter out COMMENT, NL, TYPE_COMMENT tokens before parsing
            _tokens = tokens.Where(t =>
                t.Type != PyToken.Type.COMMENT &&
                t.Type != PyToken.Type.NL &&
                t.Type != PyToken.Type.TYPE_COMMENT).ToList();
            _filename = filename;
        }

        // CPython 3.12: Constructor with source code for error reporting
        protected PyParserBase(List<GeneratedTokenInfo> tokens, string filename, string source)
            : this(tokens, filename)
        {
            _source = source;
            // CPython 3.12: Use universal newlines (like Python's str.splitlines)
            _sourceLines = source.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        }

        protected GeneratedTokenInfo CurrentToken
        {
            get => _position < _tokens.Count ? _tokens[_position] : null;
        }

        public abstract TResult Parse();

        // CPython 3.12: _get_keyword_or_name_type - Must be implemented by generated parser
        protected abstract int GetKeywordOrNameType(string name, int nameLen);

        // CPython 3.12: Get source line for error reporting (like CPython's GetSourceLine)
        protected string? GetSourceLine(int lineNumber)
        {
            if (_sourceLines == null || lineNumber <= 0 || lineNumber > _sourceLines.Length)
            {
                return null;
            }
            return _sourceLines[lineNumber - 1];  // Convert 1-based to 0-based index
        }

        public virtual GeneratedModule ParseFile()
        {
            // Override in generated parser
            throw new NotImplementedException("ParseFile must be overridden");
        }

        protected GeneratedTokenInfo ExpectToken(PyToken.Type type)
        {
            var token = CurrentToken;
            if (token == null) return null;

            // CPython 3.12: If token is NAME, check if it's a keyword
            // This implements initialize_token + _get_keyword_or_name_type logic
            int tokenTypeInt = (int)token.Type;
            if (token.Type == PyToken.Type.NAME)
            {
                tokenTypeInt = GetKeywordOrNameType(token.Value, token.Value.Length);
            }

            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[ExpectToken] type={(int)type}, tokenTypeInt={tokenTypeInt}, match={tokenTypeInt == (int)type}");
            #endif
            if (tokenTypeInt == (int)type)
            {
                _lastToken = token;  // Track last consumed token
                _position++;
                return token;
            }
            return null;
        }

        protected GeneratedTokenInfo Expect(PyToken.Type type, string value)
        {
            var token = CurrentToken;
            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[Expect] type={(int)type}, value='{value}', token={(token != null ? $"{(int)token.Type}:'{token.Value}'" : "null")}, match={token != null && token.Type == type && token.Value == value}");
            #endif
            if (token != null && token.Type == type && token.Value == value)
            {
                _lastToken = token;  // Track last consumed token
                _position++;
                return token;
            }
            return null;
        }

        protected GeneratedTokenInfo ExpectName()
        {
            var token = CurrentToken;
            if (token != null && token.Type == PyToken.Type.NAME)
            {
                _lastToken = token;  // Track last consumed token
                _position++;
                return token;
            }
            return null;
        }

        /// <summary>
        /// CPython 3.12: _PyPegen_name_token - Expect NAME token and convert to AST Name node
        /// Combines _PyPegen_expect_token(p, NAME) + _PyPegen_name_from_token(p, t)
        /// Returns GeneratedExpr (AST node), not GeneratedTokenInfo (token)
        /// </summary>
        protected GeneratedExpr ExpectNameExpr()
        {
            var token = ExpectName();
            return token != null ? NameToken(token) : null;
        }

        /// <summary>
        /// CPython 3.12: ExpectSoftKeyword
        /// Soft keywords: _, case, match, type
        /// These are NAME tokens that are treated as keywords only in specific contexts
        /// </summary>
        protected GeneratedTokenInfo ExpectSoftKeyword(string keyword)
        {
            var token = CurrentToken;
            // CPython: t->type != NAME → return NULL
            if (token == null || token.Type != PyToken.Type.NAME)
            {
                return null;
            }
            // CPython: strcmp(keyword, the_token) == 0
            if (token.Value == keyword)
            {
                _lastToken = token;  // Track last consumed token
                _position++;
                return token;
            }
            return null;
        }

        /// <summary>
        /// CPython 3.12: ExpectForcedToken
        /// Token *ExpectForcedToken(Parser *p, int type, const char* expected)
        /// Forced token must match or raise syntax error immediately
        /// </summary>
        protected GeneratedTokenInfo ExpectForcedToken(PyToken.Type type, string expected)
        {
            if (_pendingSyntaxError != null)
            {
                return null;
            }

            var token = CurrentToken;
            if (token == null || token.Type != type || token.Value != expected)
            {
                // CPython: RAISE_SYNTAX_ERROR_KNOWN_LOCATION(t, "expected '%s'", expected)
                _pendingSyntaxError = $"expected '{expected}'";
                _pendingErrorPosition = _position;
                return null;
            }
            _lastToken = token;  // Track last consumed token
            _position++;
            return token;
        }

        /// <summary>
        /// CPython 3.12: ExpectForcedResult
        /// void*ExpectForcedResult(Parser *p, void* result, const char* expected)
        /// Forced result must be non-null or raise syntax error immediately
        /// </summary>
        protected T ExpectForcedResult<T>(T result, string expected) where T : class
        {
            if (_pendingSyntaxError != null)
            {
                return null;
            }

            if (result == null)
            {
                // CPython: RAISE_SYNTAX_ERROR("expected (%s)", expected)
                _pendingSyntaxError = $"expected ({expected})";
                _pendingErrorPosition = _position;
                return null;
            }
            return result;
        }

        /// <summary>
        /// Handle left-recursive rules using memoization
        /// CPython 3.12: Implements Warth et al. 'Packrat Parsers Can Support Left Recursion'
        /// Algorithm: SEED (FAIL) → BASE CASE → GROW → TERMINATE
        ///
        /// CRITICAL FIX: During growth loop, recursive calls to the same rule at the same position
        /// MUST immediately return the cached SEED value to prevent infinite recursion.
        /// This is the key difference from normal memoization!
        /// </summary>
        protected GeneratedPtr TryLeftRecursive(string ruleName, Func<GeneratedPtr> ruleFunc)
        {
            // Check recursion depth
            if (_level >= MAX_RECURSION_DEPTH)
            {
                throw new StackOverflowException($"Maximum recursion depth exceeded in rule {ruleName}");
            }

            // CPython 3.12: Check memoization first (like IsMemoized)
            var key = (_position, ruleName);
            if (_lrCache.TryGetValue(key, out var lrEntry))
            {
                #if DEBUG_PARSE_LOG
                Console.WriteLine($"[LR] {ruleName}: Memo HIT at pos={_position}, IsGrowing={lrEntry.IsGrowing}, returning cached result, newPos={lrEntry.EndPos}");
                #endif
                // CRITICAL: During growth, return the SEED immediately to prevent infinite recursion
                // This allows recursive alternatives to fail and base case to succeed
                _position = lrEntry.EndPos;
                return lrEntry.Result;
            }

            _level++;
            int _mark = _position;
            int _resmark = _position;
            GeneratedPtr _res = null;

            try
            {
                // CPython 3.12: Growth loop
                while (true)
                {
                    // CRITICAL: Update memo with current result BEFORE parsing
                    // Mark as "growing" so recursive calls know to return this seed immediately
                    // Use _resmark (previous iteration's end position), not _position
                    _lrCache[key] = new LREntry { Result = _res, EndPos = _resmark, IsGrowing = true };

                    #if DEBUG_PARSE_LOG
                    Console.WriteLine($"[LR] {ruleName}: Loop iteration, _mark={_mark}, _resmark={_resmark}, seeding cache with result={((_res == null) ? "null" : "non-null")}");
                    #endif

                    // Reset position and try to parse (like primary_raw)
                    _position = _mark;
                    var _raw = ruleFunc();

                    // Check for progress
                    if (_raw == null || _position <= _resmark)
                    {
                        #if DEBUG_PARSE_LOG
                        Console.WriteLine($"[LR] {ruleName}: No progress, terminating. _raw={((_raw == null) ? "null" : "non-null")}, pos={_position}, _resmark={_resmark}");
                        #endif
                        break;
                    }

                    // Made progress - update and continue
                    #if DEBUG_PARSE_LOG
                    Console.WriteLine($"[LR] {ruleName}: Progress made, _resmark {_resmark} -> {_position}");
                    #endif
                    _resmark = _position;
                    _res = _raw;
                }

                // Restore final position and mark as no longer growing
                _position = _resmark;
                _lrCache[key] = new LREntry { Result = _res, EndPos = _resmark, IsGrowing = false };

                #if DEBUG_PARSE_LOG
                Console.WriteLine($"[LR] {ruleName}: Returning _res at pos={_position}, final cache updated");
                #endif
                return _res;
            }
            finally
            {
                _level--;
            }
        }

        /// <summary>
        /// Handle memoized (non-left-recursive) rules with token-based caching
        /// CPython 3.12: Implements IsMemoized() + UpdateMemo() pattern
        /// Pattern:
        ///   1. Get current token: Token *t = p->tokens[p->mark]
        ///   2. Check cache: for (Memo *m = t->memo; m != NULL; m = m->next)
        ///   3. If hit: p->mark = m->mark; return m->node
        ///   4. If miss: parse, then update token's memo list
        /// </summary>
        protected GeneratedPtr TryMemoized(string ruleName, Func<GeneratedPtr> ruleFunc)
        {
            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[MEMO] {ruleName} at pos={_position}");
            #endif
            // STEP 1: Get current token (CPython: Token *t = p->tokens[p->mark])
            if (_position >= _tokens.Count)
            {
                #if DEBUG_PARSE_LOG
                Console.WriteLine($"[MEMO] {ruleName}: Beyond token count, parsing directly");
                #endif
                // ENDMARKER or beyond - don't memoize, just parse
                return ruleFunc();
            }

            var token = _tokens[_position];
            int startMark = _position;

            // STEP 2: CHECK CACHE - IsMemoized(p, type, &res)
            // CPython 3.12: Cache key must include call_invalid_rules state for expression rules
            if (token.Memo != null)
            {
                // CPython: for (Memo *m = t->memo; m != NULL; m = m->next)
                // CPython: Cache key is m->type (rule type), NOT affected by call_invalid_rules
                var cached = token.Memo.FirstOrDefault(m => m.RuleType == ruleName);
                if (cached != null)
                {
                    #if DEBUG_PARSE_LOG
                    Console.WriteLine($"[MEMO] {ruleName}: Cache HIT at pos={_position}, returning cached result (null={cached.Node == null}), newPos={cached.Mark}");
                    #endif
                    // Cache HIT - restore mark and return cached result
                    // CPython: p->mark = m->mark; *(void**)(pres) = m->node; return 1;
                    _position = cached.Mark;
                    return cached.Node as GeneratedPtr;
                }
            }
            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[MEMO] {ruleName}: Cache MISS at pos={_position}, parsing...");
            #endif

            // STEP 3: Cache MISS - parse the rule
            var result = ruleFunc();
            int endMark = _position;
            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[MEMO] {ruleName}: Parse completed, result={(result == null ? "null" : "not-null")}, pos={startMark}->{endMark}");
            #endif

            // STEP 4: UPDATE CACHE - UpdateMemo(p, mark, type, node)
            if (token.Memo == null)
            {
                token.Memo = new List<MemoEntry>();
            }

            // CPython: Search for existing entry and update, or insert new
            // CPython: Cache key is just rule name, independent of call_invalid_rules
            var existing = token.Memo.FirstOrDefault(m => m.RuleType == ruleName);
            if (existing != null)
            {
                #if DEBUG_PARSE_LOG
                Console.WriteLine($"[MEMO] {ruleName}: Updating existing cache entry");
                #endif
                // Update existing entry (shouldn't happen in normal flow, but CPython does this)
                existing.Node = result;
                existing.Mark = endMark;
            }
            else
            {
                #if DEBUG_PARSE_LOG
                Console.WriteLine($"[MEMO] {ruleName}: Adding new cache entry");
                #endif
                // Insert new memo entry
                // CPython: InsertMemo() adds to front of linked list
                // CPython: Cache key is just rule name, independent of call_invalid_rules
                token.Memo.Add(new MemoEntry
                {
                    RuleType = ruleName,
                    Node = result,
                    Mark = endMark  // Position AFTER parsing (can be same as start if failed)
                });
            }

            return result;
        }

        /// <summary>
        /// CPython 3.12: IsMemoized - Check if rule result is memoized
        /// Used by left-recursive wrapper for growth loop
        /// </summary>
        protected bool TryGetMemoized(string ruleName, out GeneratedPtr result)
        {
            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[MEMO-GET] {ruleName} at pos={_position}");
            #endif
            var key = (_position, ruleName);
            if (_lrCache.TryGetValue(key, out var entry))
            {
                #if DEBUG_PARSE_LOG
                Console.WriteLine($"[MEMO-GET] {ruleName} HIT: result={(entry.Result != null ? "non-null" : "null")}, endPos={entry.EndPos}");
                #endif
                _position = entry.EndPos;
                result = entry.Result;
                return true;
            }
            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[MEMO-GET] {ruleName} MISS");
            #endif
            result = null;
            return false;
        }

        /// <summary>
        /// CPython 3.12: UpdateMemo - Update memoization for left-recursive growth
        /// </summary>
        protected void UpdateMemoized(string ruleName, int mark, GeneratedPtr result, int endPos)
        {
            #if DEBUG_PARSE_LOG
            Console.WriteLine($"[MEMO-UPDATE] {ruleName} at mark={mark}: result={(result != null ? "non-null" : "null")}, endPos={endPos}");
            #endif
            var key = (mark, ruleName);
            _lrCache[key] = new LREntry { Result = result, EndPos = endPos, IsGrowing = false };
        }

        /// <summary>
        /// Convert NAME token to AST Name expression
        /// CPython 3.12: Used in grammar actions
        /// </summary>
        protected GeneratedName NameToken(GeneratedTokenInfo token)
        {
            if (token == null) return null;
            var name = new GeneratedName();
            name.Id = token.Value ?? "";
            name.Ctx = GeneratedLoad.Instance;  // Default context
            name.LineNo = token.Line;
            name.ColOffset = token.Column;
            name.EndLineNo = token.EndLine;
            name.EndColOffset = token.EndColumn;
            return name;
        }

        /// <summary>
        /// Convert NUMBER token to AST Constant expression
        /// CPython 3.12: Numbers are represented as Constant nodes
        /// </summary>
        protected GeneratedConstant NumberToken(GeneratedTokenInfo token)
        {
            if (token == null) return null;
            var constant = new GeneratedConstant();
            // CPython 3.12: Parse number value (decimal, hex, octal, binary, float)
            string value = token.Value;

            // Check for complex numbers (j suffix) - TODO: implement PyComplex
            if (value.EndsWith("j", StringComparison.OrdinalIgnoreCase) || value.EndsWith("J"))
            {
                // For now, treat as comment/unsupported
                throw new System.NotImplementedException("Complex numbers not yet supported");
            }
            // Check for floating point
            else if (value.Contains(".") || value.Contains("e", StringComparison.OrdinalIgnoreCase))
            {
                constant.Value = new GeneratedPyConstantFloat(double.Parse(value));
            }
            // Check for hexadecimal (0x or 0X)
            else if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                constant.Value = new GeneratedPyConstantInt(Convert.ToInt64(value, 16));
            }
            // Check for octal (0o or 0O)
            else if (value.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            {
                constant.Value = new GeneratedPyConstantInt(Convert.ToInt64(value.Substring(2), 8));
            }
            // Check for binary (0b or 0B)
            else if (value.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                constant.Value = new GeneratedPyConstantInt(Convert.ToInt64(value.Substring(2), 2));
            }
            // Decimal integer
            else
            {
                constant.Value = new GeneratedPyConstantInt(long.Parse(value));
            }

            constant.LineNo = token.Line;
            constant.ColOffset = token.Column;
            constant.EndLineNo = token.EndLine;
            constant.EndColOffset = token.EndColumn;
            return constant;
        }

        /// <summary>
        /// Convert STRING token to token wrapper for ConstantFromString
        /// CPython 3.12: Token is passed to string_parser.c for decoding
        /// Note: Returns GeneratedTokenInfo directly to trigger DecodeStringLiteral
        /// </summary>
        protected GeneratedTokenInfo StringToken(GeneratedTokenInfo token)
        {
            // Return token as-is so ConstantFromString can decode it
            return token;
        }

        /// <summary>
        /// CPython 3.12: Raise syntax error at known token location
        /// Uses PySyntaxErrorException with location info for parsing errors
        /// </summary>
        protected void RaiseErrorKnownLocation(GeneratedTokenInfo token, string message)
        {
            if (token == null)
            {
                _errorIndicator = 1;
                throw new PySyntaxErrorException(message);
            }

            // Mark error and throw exception with location
            _errorIndicator = 1;
            _knownErrToken = token;
            var locationMsg = $"  File \"{_filename}\", line {token.Line}\n    {message}";
            throw new PySyntaxErrorException(locationMsg);
        }

        // ============================================================
        // Invalid Rules Error Handling (CPython 3.12)
        // ============================================================

        /// <summary>
        /// CPython 3.12: RAISE_SYNTAX_ERROR - Simple error message
        /// </summary>
        protected GeneratedPtr RaiseSyntaxError(string message)
        {
            _errorIndicator = 1;
            throw new PySyntaxErrorException($"SyntaxError: {message}");
        }

        /// <summary>
        /// CPython 3.12: RAISE_SYNTAX_ERROR_KNOWN_LOCATION - Error at specific token
        /// </summary>
        protected GeneratedPtr RaiseSyntaxErrorKnownLocation(GeneratedPtr node, string format, params object[] args)
        {
            _errorIndicator = 1;
            string message = string.Format(format, args);

            // Try to get location from node if it's a token
            if (node is GeneratedTokenInfo token)
            {
                throw new PySyntaxErrorException($"  File \"{_filename}\", line {token.Line}\n    SyntaxError: {message}");
            }

            throw new PySyntaxErrorException($"SyntaxError: {message}");
        }

        /// <summary>
        /// CPython 3.12: RAISE_SYNTAX_ERROR_KNOWN_RANGE - Error spanning from start to end token
        /// </summary>
        protected GeneratedPtr RaiseSyntaxErrorKnownRange(GeneratedPtr startNode, GeneratedPtr endNode, string format, params object[] args)
        {
            _errorIndicator = 1;
            string message = string.Format(format, args);

            // Try to get location from start node
            if (startNode is GeneratedTokenInfo startToken)
            {
                throw new PySyntaxErrorException($"  File \"{_filename}\", line {startToken.Line}\n    SyntaxError: {message}");
            }

            throw new PySyntaxErrorException($"SyntaxError: {message}");
        }

        /// <summary>
        /// CPython 3.12: RAISE_SYNTAX_ERROR_STARTING_FROM - Error starting from specific token
        /// </summary>
        protected GeneratedPtr RaiseSyntaxErrorStartingFrom(GeneratedPtr node, string format, params object[] args)
        {
            return RaiseSyntaxErrorKnownLocation(node, format, args);
        }

        /// <summary>
        /// CPython 3.12: RAISE_INDENTATION_ERROR - Indentation-specific error
        /// </summary>
        protected GeneratedPtr RaiseIndentationError(string format, params object[] args)
        {
            _errorIndicator = 1;
            string message = string.Format(format, args);
            throw new PySyntaxErrorException($"IndentationError: {message}");
        }

        /// <summary>
        /// CPython 3.12: RAISE_SYNTAX_ERROR_ON_NEXT_TOKEN - Error on the next token
        /// </summary>
        protected GeneratedPtr RaiseSyntaxErrorOnNextToken(string message)
        {
            _errorIndicator = 1;
            var token = CurrentToken;
            if (token != null)
            {
                throw new PySyntaxErrorException($"  File \"{_filename}\", line {token.Line}\n    SyntaxError: {message}");
            }
            throw new PySyntaxErrorException($"SyntaxError: {message}");
        }

        /// <summary>
        /// CPython 3.12: RAISE_ERROR_KNOWN_LOCATION - Generic error at known location
        /// </summary>
        protected GeneratedPtr RaiseErrorKnownLocation(Type exceptionType, int lineno, int colOffset, int endLineno, int endColOffset, string format, params object[] args)
        {
            _errorIndicator = 1;
            string message = string.Format(format, args);
            throw new PySyntaxErrorException($"  File \"{_filename}\", line {lineno}\n    {message}");
        }

        /// <summary>
        /// CPython 3.12: RAISE_SYNTAX_ERROR_INVALID_TARGET - Invalid assignment/del target error
        /// Used for checking if expressions can be used as assignment targets
        /// </summary>
        protected GeneratedPtr RaiseSyntaxErrorInvalidTarget(string targetType, GeneratedPtr node)
        {
            _errorIndicator = 1;
            string message = $"cannot {targetType.ToLower()}";

            if (node is GeneratedTokenInfo token)
            {
                throw new PySyntaxErrorException($"  File \"{_filename}\", line {token.Line}\n    SyntaxError: {message}");
            }

            throw new PySyntaxErrorException($"SyntaxError: {message}");
        }

        /// <summary>
        /// CPython: PyErr_Occurred() - Check if an error has occurred
        /// Used in f-string parsing to check for decoding errors
        /// </summary>
        protected bool PyErr_Occurred()
        {
            return _errorIndicator != 0 || _pendingSyntaxError != null;
        }

        /// <summary>
        /// CPython: CHECK_NULL_ALLOWED macro - Allow null values in specific contexts
        /// Used in grammar actions where null is a valid result
        /// </summary>
        protected T CheckNullAllowed<T>(T value) where T : class
        {
            return value;  // Always allow null in C#
        }

        // ============================================================
        // PEG Parser Helper Methods
        // ============================================================

        /// <summary>
        /// Mark current position for backtracking
        /// CPython 3.12: int mark = p->mark
        /// </summary>
        protected int Mark()
        {
            return _position;
        }

        /// <summary>
        /// Reset position to marked position
        /// CPython 3.12: p->mark = mark
        /// </summary>
        protected void Reset(int mark)
        {
            _position = mark;
        }

        // ============================================================
        // Position Tracking for AST Node Creation
        // ============================================================

        /// <summary>
        /// Capture start position before parsing an alternative
        /// CPython 3.12: EXTRA macro - captures start position from first token
        /// </summary>
        protected void CaptureStart()
        {
            _startMark = _position;
            _startToken = CurrentToken;
        }

        /// <summary>
        /// Get start line for AST node
        /// CPython 3.12: EXTRA macro - lineno from start token
        /// </summary>
        protected int GetStartLine()
        {
            return _startToken?.Line ?? 1;
        }

        /// <summary>
        /// Get start column for AST node
        /// CPython 3.12: EXTRA macro - col_offset from start token
        /// </summary>
        protected int GetStartColumn()
        {
            return _startToken?.Column ?? 0;
        }

        /// <summary>
        /// Get end line for AST node
        /// CPython 3.12: EXTRA macro - end_lineno from last token
        /// </summary>
        protected int GetEndLine()
        {
            return _lastToken?.EndLine ?? _startToken?.EndLine ?? 1;
        }

        /// <summary>
        /// Get end column for AST node
        /// CPython 3.12: EXTRA macro - end_col_offset from last token
        /// </summary>
        protected int GetEndColumn()
        {
            return _lastToken?.EndColumn ?? _startToken?.EndColumn ?? 0;
        }

        // ============================================================
        // EXTRA Macro Implementation (CPython 3.12)
        // ============================================================

        /// <summary>
        /// CPython 3.12: EXTRA macro - Expands to position parameters for AST nodes
        /// C: #define EXTRA _start_lineno, _start_col_offset, _end_lineno, _end_col_offset, p->arena
        /// C#: Individual properties that can be used in grammar action code
        /// Usage in python_cs.gram: PyAst.Pass(EXTRA) → PyAst.Pass(_start_lineno, _start_col_offset, _end_lineno, _end_col_offset)
        /// </summary>
        protected int _start_lineno => GetStartLine();
        protected int _start_col_offset => GetStartColumn();
        protected int _end_lineno => GetEndLine();
        protected int _end_col_offset => GetEndColumn();

        // ============================================================
        // CHECK and CheckVersion Macros (CPython 3.12)
        // ============================================================

        /// <summary>
        /// CPython 3.12: CHECK macro - Null check for parser results
        /// C: #define CHECK(type, result) ((type) CHECK_CALL(p, result))
        /// Usage: CHECK(GeneratedStmt, result) - returns result or null if failed
        /// </summary>
        protected T? Check<T>(T? result) where T : class
        {
            return result; // In C#, null propagation is automatic
        }

        /// <summary>
        /// CPython 3.12: CHECK_VERSION macro - Version-gated feature check
        /// C: #define CHECK_VERSION(type, version, msg, node) \
        ///     ((type) INVALID_VERSION_CHECK(p, version, msg, node))
        /// Usage: CheckVersion(GeneratedStmt, 6, "feature", node)
        /// </summary>
        protected T? CheckVersion<T>(int version, string message, T? node) where T : class
        {
            // Python 3.12 uses version 6 (PY_MINOR_VERSION = 12, internal version = 6)
            // SharpPy targets Python 3.12, so all features are enabled
            // In CPython, this would raise RAISE_SYNTAX_ERROR_STARTING_FROM for older versions
            const int CURRENT_VERSION = 6; // Python 3.12

            if (version > CURRENT_VERSION)
            {
                // Feature requires a newer Python version
                throw new SyntaxErrorException($"{message} requires Python 3.{version + 6} or newer");
            }

            return node;
        }

        /// <summary>
        /// Expect an operator token
        /// CPython 3.12: Operators use OP token type with specific values
        /// </summary>
        protected GeneratedTokenInfo ExpectOp(string op)
        {
            // Map operator string to token type
            var tokenType = MapOperatorToTokenType(op);
            return Expect(tokenType, op);  // Expect already tracks _lastToken
        }

        /// <summary>
        /// Expect a keyword token
        /// CPython 3.12: Keywords are NAME tokens with specific values
        /// </summary>
        protected GeneratedTokenInfo ExpectKeyword(string keyword)
        {
            var token = CurrentToken;
            if (token == null || token.Type != PyToken.Type.NAME || token.Value != keyword)
            {
                return null;
            }
            _lastToken = token;  // Track last consumed token
            _position++;
            return token;
        }

        /// <summary>
        /// Map operator string to token type
        /// </summary>
        private PyToken.Type MapOperatorToTokenType(string op)
        {
            // Use PyToken.Literals list to find matching token type
            foreach (var (name, type) in PyToken.Literals)
            {
                if (name == op)
                {
                    return type;
                }
            }
            // Default to OP if not found
            return PyToken.Type.OP;
        }

        /// <summary>
        /// Parse optional element [item]
        /// CPython 3.12: Returns item or null
        /// </summary>
        protected GeneratedPtr ParseOptional(Func<GeneratedPtr> parser)
        {
            int mark = Mark();
            var result = parser();
            if (result == null)
            {
                Reset(mark);
            }
            return result;
        }

        /// <summary>
        /// Parse zero or more elements item*
        /// CPython 3.12: Returns sequence or empty
        /// </summary>
        protected GeneratedSeq ParseZeroOrMore(Func<GeneratedPtr> parser)
        {
            var seq = new GeneratedSeq();
            while (true)
            {
                int mark = Mark();
                var item = parser();
                if (item == null)
                {
                    Reset(mark);
                    break;
                }
                seq.Add(item);
            }
            return seq;
        }

        /// <summary>
        /// Parse one or more elements item+
        /// CPython 3.12: Returns sequence or null if no matches
        /// </summary>
        protected GeneratedSeq ParseOneOrMore(Func<GeneratedPtr> parser)
        {
            var first = parser();
            if (first == null)
            {
                return null;
            }
            var seq = new GeneratedSeq();
            seq.Add(first);
            while (true)
            {
                int mark = Mark();
                var item = parser();
                if (item == null)
                {
                    Reset(mark);
                    break;
                }
                seq.Add(item);
            }
            return seq;
        }

        /// <summary>
        /// Positive lookahead &item
        /// CPython 3.12: Check if item matches without consuming
        /// </summary>
        protected GeneratedPtr PositiveLookahead(Func<GeneratedPtr> parser)
        {
            int mark = Mark();
            var result = parser();
            Reset(mark);
            // Return a dummy success value if matched, null if not
            return result != null ? DummyResponse : null;
        }

        /// <summary>
        /// Negative lookahead !item
        /// CPython 3.12: Check if item doesn't match without consuming
        /// </summary>
        protected GeneratedPtr NegativeLookahead(Func<GeneratedPtr> parser)
        {
            int mark = Mark();
            var result = parser();
            Reset(mark);
            // Return success if NOT matched, null if matched
            return result == null ? DummyResponse : null;
        }

        /// <summary>
        /// Gather with separator sep.item+
        /// CPython 3.12: item (sep item)*
        /// </summary>
        protected GeneratedSeq ParseGatherPlus(Func<GeneratedPtr> sepParser, Func<GeneratedPtr> itemParser)
        {
            var first = itemParser();
            if (first == null)
            {
                return null;
            }
            var seq = new GeneratedSeq();
            seq.Add(first);
            while (true)
            {
                int mark = Mark();
                var sep = sepParser();
                if (sep == null)
                {
                    Reset(mark);
                    break;
                }
                var item = itemParser();
                if (item == null)
                {
                    Reset(mark);
                    break;
                }
                seq.Add(item);
            }
            return seq;
        }

        /// <summary>
        /// Gather with separator sep.item*
        /// CPython 3.12: [item (sep item)*]
        /// </summary>
        protected GeneratedSeq ParseGatherStar(Func<GeneratedPtr> sepParser, Func<GeneratedPtr> itemParser)
        {
            var seq = new GeneratedSeq();
            var first = itemParser();
            if (first == null)
            {
                return seq; // Empty sequence
            }
            seq.Add(first);
            while (true)
            {
                int mark = Mark();
                var sep = sepParser();
                if (sep == null)
                {
                    Reset(mark);
                    break;
                }
                var item = itemParser();
                if (item == null)
                {
                    Reset(mark);
                    break;
                }
                seq.Add(item);
            }
            return seq;
        }

        /// <summary>
        /// Parse a group (alternatives within parentheses)
        /// CPython 3.12: (alt1 | alt2 | ...)
        /// TODO: This is a placeholder - groups should be inlined or generated properly
        /// </summary>
        protected GeneratedPtr ParseGroup()
        {
            // TODO: Implement inline group parsing
            // For now, return null to indicate parsing failure
            return null;
        }

        /// <summary>
        /// Dummy expression for lookahead and alternatives without captures
        /// CPython 3.12: Lookahead returns int (boolean), but C# needs object
        /// This concrete class can be used anywhere GeneratedPtr or GeneratedExpr is needed
        /// </summary>
        private class DummyExpr : GeneratedExpr { }

        /// <summary>
        /// Shared dummy response instance
        /// Used for: 1) Lookahead success indicator, 2) Alternatives without named captures
        /// </summary>
        protected static readonly GeneratedPtr DummyResponse = new DummyExpr();
    }
}
