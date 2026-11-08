// PyParserBase - Base class for PEG parser
// CPython 3.12 compatible - Manual implementation (not generated)
// This file provides common parsing functionality for all PEG parsers

using System;
using System.Collections.Generic;
using System.Numerics;
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
    public abstract partial class PyParserBase<TResult>
    {
        protected List<GeneratedTokenInfo> _tokens;
        protected int _position = 0;  // CPython: mark
        protected string _filename;
        protected string? _source = null;  // CPython 3.12: Source code for error reporting
        protected string[]? _sourceLines = null;  // CPython 3.12: Source lines for error reporting
        protected string? _pendingSyntaxError = null;
        protected int _pendingErrorPosition = -1;
        protected bool _callInvalidRules = false;
        // CPython 3.12: Memo cache uses (position, rule_name) tuple keys
        protected Dictionary<(int, string), GeneratedAstNode> _memoCache = new();

        // CPython Parser fields for recursion and error tracking
        protected int _level = 0;  // Nesting depth for recursion limit
        protected GeneratedTokenInfo _knownErrToken = null;  // Error location tracking
        protected int _errorIndicator = 0;  // Error state flag
        protected const int MAX_RECURSION_DEPTH = 6000;  // Python's recursion limit

        // Position tracking for AST node creation
        // CPython 3.12: EXTRA macro captures token positions for AST nodes
        protected int _startMark = -1;  // Position at start of current rule
        protected GeneratedTokenInfo? _startToken = null;  // Token at start of current rule
        protected GeneratedTokenInfo? _lastToken = null;  // Last consumed token

        // Parser: where the tokens come from, the rule it starts with and its flags
        protected readonly Tokenizer _tok;
        private readonly CompileMode _startRule;
        private int _flags;
        private bool _parsingStarted;

        // Left-recursion handling (Warth et al. algorithm)
        protected class LREntry
        {
            public GeneratedPtr Result { get; set; }
            public int EndPos { get; set; }
            public bool IsGrowing { get; set; }
        }
        protected Dictionary<(int, string), LREntry> _lrCache = new();

        // CPython 3.12: Token-based memoization
        // Each Token owns its Memo list - no global cache needed
        // MemoEntry is defined in GeneratedTokenInfo (Tokenizer.cs)

        // _PyPegen_Parser_New: the tokens are read as the rules ask for them.
        protected PyParserBase(Tokenizer tok, string filename, string source, CompileMode startRule = CompileMode.File, int flags = 0)
        {
            _tok = tok;
            _tokens = new List<GeneratedTokenInfo>();
            _filename = filename;
            _source = source;
            _startRule = startRule;
            _flags = flags;
            // CPython 3.12: Use universal newlines (like Python's str.splitlines)
            _sourceLines = source.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        }

        // _PyPegen_fill_token and initialize_token
        private void FillNextToken()
        {
            var token = _tok.Get();
            // At the end, single input mode needs a newline.
            if (_startRule == CompileMode.Single && token.Type == PyToken.Type.ENDMARKER && _parsingStarted)
            {
                token.Type = PyToken.Type.NEWLINE;
                _parsingStarted = false;
                if (_tok.Indent != 0 && (_flags & PyCF_DONT_IMPLY_DEDENT) == 0)
                {
                    _tok.Pendin = -_tok.Indent;
                    _tok.Indent = 0;
                }
            }
            else
                _parsingStarted = true;
            _tokens.Add(token);
            if (token.Type == PyToken.Type.ERRORTOKEN)
                TokenizerError();
        }

        protected GeneratedTokenInfo CurrentToken
        {
            get
            {
                if (_position == _tokens.Count)
                    FillNextToken();
                return _tokens[_position];
            }
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
            if (token == null) return null;

            // CPython 3.12: NAME tokens must not be keywords
            // Check if this NAME token is actually a keyword
            int tokenTypeInt = (int)token.Type;
            if (token.Type == PyToken.Type.NAME)
            {
                tokenTypeInt = GetKeywordOrNameType(token.Value, token.Value.Length);
            }

            // Only accept if it's truly a NAME (not a keyword)
            if (tokenTypeInt == (int)PyToken.Type.NAME)
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
            var token = CurrentToken;
            if (token.Type != type)
                RaiseSyntaxErrorKnownLocation(token, "expected '%s'", expected);
            _lastToken = token;
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
                RaiseSyntaxError("expected (%s)", expected);
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

                    // Reset position and try to parse (like primary_raw)
                    _position = _mark;
                    var _raw = ruleFunc();

                    // Check for progress
                    if (_raw == null || _position <= _resmark)
                    {
                        break;
                    }

                    // Made progress - update and continue
                    _resmark = _position;
                    _res = _raw;
                }

                // Restore final position and mark as no longer growing
                _position = _resmark;
                _lrCache[key] = new LREntry { Result = _res, EndPos = _resmark, IsGrowing = false };

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
            var token = CurrentToken;
            int startMark = _position;

            // STEP 2: CHECK CACHE - IsMemoized(p, type, &res)
            // CPython 3.12: Cache key must include call_invalid_rules state for expression rules
            if (token.Memo != null)
            {
                // CPython: for (Memo *m = t->memo; m != NULL; m = m->next)
                // CPython: Cache key is m->type (rule type), NOT affected by call_invalid_rules
                // Performance: Eliminated LINQ - manual search
                MemoEntry cached = null;
                for (int i = 0; i < token.Memo.Count; i++)
                {
                    if (token.Memo[i].RuleType == ruleName)
                    {
                        cached = token.Memo[i];
                        break;
                    }
                }
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
            // Performance: Eliminated LINQ - manual search
            MemoEntry existing = null;
            for (int i = 0; i < token.Memo.Count; i++)
            {
                if (token.Memo[i].RuleType == ruleName)
                {
                    existing = token.Memo[i];
                    break;
                }
            }
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
            var constant = new GeneratedConstant { LineNo = token.Line, ColOffset = token.Column, EndLineNo = token.EndLine, EndColOffset = token.EndColumn };
            // CPython 3.12: Parse number value (decimal, hex, octal, binary, float)
            string value = token.Value;

            // PEP 515: Remove underscores from numeric literals (Python 3.6+)
            // Underscores are allowed in all numeric literals for readability
            string cleanValue = value.Replace("_", "");

            // CPython 3.12: Parser/action_helpers.c:785-793 (_PyPegen_ensure_imaginary)
            // Complex numbers: imaginary part only (e.g., 3j, 4.5j)
            // The parser treats "j" suffix as imaginary number literal
            if (cleanValue.EndsWith("j", StringComparison.OrdinalIgnoreCase))
            {
                // Remove 'j' or 'J' suffix
                string imagPart = cleanValue.Substring(0, cleanValue.Length - 1);
                double imagValue;
                if (string.IsNullOrEmpty(imagPart))
                {
                    // Just 'j' or 'J' means 1j
                    imagValue = 1.0;
                }
                else
                {
                    imagValue = PyFloat.Strtod(imagPart);
                }
                // Create complex with real=0, imag=parsed value
                constant.Value = new GeneratedPyConstantComplex(0.0, imagValue);
            }
            // CPython 3.12: Python/ast.c:4865-4950 (parsenumber function)
            // CPython 3.12: Objects/longobject.c - arbitrary precision integers with BigInteger
            // Check integer bases BEFORE float check (0x1fbe contains 'e' but is hex, not float!)
            // Check for hexadecimal (0x or 0X)
            else if (cleanValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                // BigInteger.Parse with hexadecimal format
                // Note: BigInteger.Parse with NumberStyles.HexNumber does NOT accept "0x" prefix
                // CPython 3.12: Python hex literals are always positive (unsigned)
                // Prepend "0" to ensure positive parsing (avoid MSB sign interpretation)
                var hexDigits = cleanValue.Substring(2);
                constant.Value = new GeneratedPyConstantInt(BigInteger.Parse("0" + hexDigits, System.Globalization.NumberStyles.HexNumber));
            }
            // Check for octal (0o or 0O)
            else if (cleanValue.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            {
                // Convert from octal string to BigInteger
                constant.Value = new GeneratedPyConstantInt(ConvertFromBase(cleanValue.Substring(2), 8));
            }
            // Check for binary (0b or 0B)
            else if (cleanValue.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                // Convert from binary string to BigInteger
                constant.Value = new GeneratedPyConstantInt(ConvertFromBase(cleanValue.Substring(2), 2));
            }
            // Check for floating point (MUST come after hex/octal/binary checks!)
            else if (cleanValue.Contains(".") || cleanValue.Contains("e", StringComparison.OrdinalIgnoreCase))
            {
                constant.Value = new GeneratedPyConstantFloat(PyFloat.Strtod(cleanValue));
            }
            // Decimal integer
            else
            {
                // CPython 3.12: Parser/action_helpers.c:805-820 (_PyPegen_number_token)
                // Arbitrary precision integer support with BigInteger
                int limit = PyVM.Instance.IntMaxStrDigits;
                // The parser's native integer fast path accepts arbitrarily
                // many zeroes without invoking decimal BigInteger conversion.
                if (limit != 0 && cleanValue.Length > limit && cleanValue.AsSpan().IndexOfAnyExcept('0') >= 0)
                {
                    string message = $"Exceeds the limit ({limit} digits) for integer string conversion: value has {cleanValue.Length} digits; use sys.set_int_max_str_digits() to increase the limit - Consider hexadecimal for huge integer literals to avoid decimal conversion limits.";
                    throw PySyntaxError.IntegerLiteralLimit(message, _filename, token.Line, GetSourceLine(token.Line));
                }
                constant.Value = new GeneratedPyConstantInt(BigInteger.Parse(cleanValue));
            }

            constant.LineNo = token.Line;
            constant.ColOffset = token.Column;
            constant.EndLineNo = token.EndLine;
            constant.EndColOffset = token.EndColumn;
            return constant;
        }

        /// <summary>
        /// Convert string in arbitrary base (2-36) to BigInteger
        /// CPython 3.12: Objects/longobject.c:2230-2420 (PyLong_FromString)
        /// </summary>
        private static BigInteger ConvertFromBase(string digits, int baseValue)
        {
            BigInteger result = 0;
            BigInteger baseMultiplier = 1;

            // Process digits from right to left
            for (int i = digits.Length - 1; i >= 0; i--)
            {
                char c = digits[i];
                int digitValue;

                if (c >= '0' && c <= '9')
                    digitValue = c - '0';
                else if (c >= 'a' && c <= 'z')
                    digitValue = c - 'a' + 10;
                else if (c >= 'A' && c <= 'Z')
                    digitValue = c - 'A' + 10;
                else
                    throw new FormatException($"Invalid digit '{c}' for base {baseValue}");

                if (digitValue >= baseValue)
                    throw new FormatException($"Digit '{c}' out of range for base {baseValue}");

                result += digitValue * baseMultiplier;
                baseMultiplier *= baseValue;
            }

            return result;
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
        // Parser/pegen_errors.c
        // ============================================================

        private static string Format(string format, object[] args)
        {
            int next = 0;
            return System.Text.RegularExpressions.Regex.Replace(format, @"%(?:\.\d+)?[sUcRd]",
                match => next < args.Length ? args[next++]?.ToString() ?? "" : match.Value);
        }

        private static (int Line, int Col, int EndLine, int EndCol) Where(GeneratedPtr node) => node switch
        {
            GeneratedTokenInfo token => (token.Line, token.Column, token.EndLine, token.EndColumn),
            GeneratedAstNode ast => (ast.LineNo, ast.ColOffset, ast.EndLineNo, ast.EndColOffset),
            _ => (1, 0, 1, 0),
        };

        private const int CURRENT_POS = -5;
        private const int PyCF_DONT_IMPLY_DEDENT = 0x200;
        private const int CO_FUTURE_BARRY_AS_BDFL = 0x400000;
        private const int PyCF_ALLOW_INCOMPLETE_INPUT = 0x4000;

        private GeneratedTokenInfo LastFilledToken => _tokens[_tokens.Count - 1];

        // _PyPegen_byte_offset_to_character_offset
        private static int ByteOffsetToCharacterOffset(string line, int colOffset) =>
            (int)PyErrorDisplay.ByteOffsetToCharacterOffset(line, colOffset);

        // _PyErr_ProgramDecodedTextObject
        private static string ProgramText(string filename, int lineno)
        {
            try
            {
                if (lineno < 1 || !System.IO.File.Exists(filename))
                    return null;
                // err_programtext: universal newlines, and the line with its newline only when
                // the file has one there.
                string text = Tokenizer.DecodeFile(System.IO.File.ReadAllBytes(filename), filename)
                    .Replace("\r\n", "\n").Replace('\r', '\n');
                var lines = text.Split('\n');
                if (lineno < lines.Length)
                    return lines[lineno - 1] + "\n";
                if (lineno == lines.Length && lines[lineno - 1].Length > 0)
                    return lines[lineno - 1];
            }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or PythonException)
            {
            }
            return null;
        }

        // get_error_line_from_tokenizer_buffers
        private string ErrorLineFromTokenizerBuffers(int lineno)
        {
            string buffer = _tok.Buffer;
            int start = 0;
            for (int i = 0; i < lineno - 1; i++)
            {
                int newLine = buffer.IndexOf('\n', start);
                if (newLine < 0 || newLine + 1 > _tok.Inp)
                    break;
                start = newLine + 1;
            }
            int end = buffer.IndexOf('\n', start);
            return buffer.Substring(start, (end < 0 ? buffer.Length : end) - start);
        }

        // _PyPegen_raise_error_known_location
        protected Exception KnownLocation(PySyntaxError error, int lineno, int colOffset, int endLineno, int endColOffset, string message)
        {
            if (endLineno == CURRENT_POS)
                endLineno = _tok.Lineno;
            if (endColOffset == CURRENT_POS)
                endColOffset = _tok.ByteLength(_tok.LineStart, _tok.Cur);

            string errorLine = _startRule == CompileMode.File ? ProgramText(_filename, lineno) : null;
            if (errorLine == null)
            {
                if (_tok.Lineno <= lineno && _tok.Inp > _tok.Buf)
                    errorLine = _tok.Buffer.Substring(_tok.LineStart, _tok.Inp - _tok.LineStart);
                else
                    errorLine = ErrorLineFromTokenizerBuffers(lineno);
            }
            // PyUnicode_DecodeUTF8(..., "replace"): undecodable source bytes.
            var replaced = new System.Text.StringBuilder(errorLine.Length);
            foreach (char unit in errorLine)
                replaced.Append(unit >= 0xDC80 && unit <= 0xDCFF ? '�' : unit);
            errorLine = replaced.ToString();

            int colNumber = ByteOffsetToCharacterOffset(errorLine, colOffset);
            int endColNumber = endColOffset > 0 ? ByteOffsetToCharacterOffset(errorLine, endColOffset) : endColOffset;
            return PySyntaxError.Located(error, message, _filename, lineno, colNumber, endLineno, endColNumber, PyString.From(errorLine));
        }

        // RAISE_ERROR_KNOWN_LOCATION
        private Exception ErrorKnownLocation(PySyntaxError error, int lineno, int colOffset, int endLineno, int endColOffset, string message) =>
            KnownLocation(error, lineno, colOffset == CURRENT_POS ? CURRENT_POS : colOffset + 1, endLineno,
                endColOffset == CURRENT_POS ? CURRENT_POS : endColOffset + 1, message);

        // _PyPegen_raise_error
        private Exception RaiseError(PySyntaxError error, bool useMark, string message)
        {
            if (_tokens.Count == 0)
                return KnownLocation(error, 0, 0, 0, -1, message);
            if (useMark && _position == _tokens.Count)
                FillNextToken();
            var t = _knownErrToken ?? _tokens[useMark ? _position : _tokens.Count - 1];
            int colOffset;
            int endColOffset = -1;
            if (t.Column == -1)
                colOffset = _tok.Cur == _tok.Buf ? 0 : _tok.ByteLength(_tok.LineStart, _tok.Cur);
            else
                colOffset = t.Column + 1;
            if (t.EndColumn != -1)
                endColOffset = t.EndColumn + 1;
            return KnownLocation(error, t.Line, colOffset, t.EndLine, endColOffset, message);
        }

        // _Pypegen_raise_decode_error
        private Exception RaiseDecodeError(string message) => RaiseError(new PySyntaxError(message), false, message);

        // RAISE_SYNTAX_ERROR
        public GeneratedPtr RaiseSyntaxError(string format, params object[] args)
        {
            string message = Format(format, args);
            throw RaiseError(new PySyntaxError(message), false, message);
        }

        // RAISE_SYNTAX_ERROR_KNOWN_LOCATION
        public GeneratedPtr RaiseSyntaxErrorKnownLocation(GeneratedPtr node, string format, params object[] args)
        {
            var at = Where(node);
            string message = Format(format, args);
            throw ErrorKnownLocation(new PySyntaxError(message), at.Line, at.Col, at.EndLine, at.EndCol, message);
        }

        // RAISE_SYNTAX_ERROR_KNOWN_RANGE
        public GeneratedPtr RaiseSyntaxErrorKnownRange(GeneratedPtr startNode, GeneratedPtr endNode, string format, params object[] args)
        {
            if (args.Length == 0 && format.Contains("%s") && startNode is GeneratedTokenInfo named)
                args = new object[] { named.Value };
            var a = Where(startNode);
            var b = Where(endNode);
            string message = Format(format, args);
            throw ErrorKnownLocation(new PySyntaxError(message), a.Line, a.Col, b.EndLine, b.EndCol, message);
        }

        // RAISE_SYNTAX_ERROR_STARTING_FROM
        protected GeneratedPtr RaiseSyntaxErrorStartingFrom(GeneratedPtr node, string format, params object[] args)
        {
            var a = Where(node);
            string message = Format(format, args);
            throw ErrorKnownLocation(new PySyntaxError(message), a.Line, a.Col, CURRENT_POS, CURRENT_POS, message);
        }

        // RAISE_INDENTATION_ERROR
        protected GeneratedPtr RaiseIndentationError(string format, params object[] args)
        {
            string message = Format(format, args);
            throw RaiseError(new PyIndentationError(message), false, message);
        }

        // RAISE_ERROR_KNOWN_LOCATION
        protected GeneratedPtr RaiseErrorKnownLocation(Type exceptionType, int lineno, int colOffset, int endLineno, int endColOffset, string format, params object[] args)
        {
            string message = Format(format, args);
            throw ErrorKnownLocation(new PySyntaxError(message), lineno, colOffset, endLineno, endColOffset, message);
        }

        // RAISE_SYNTAX_ERROR_ON_NEXT_TOKEN
        protected GeneratedPtr RaiseSyntaxErrorOnNextToken(string message) =>
            throw RaiseError(new PySyntaxError(message), true, message);

        // RAISE_SYNTAX_ERROR_INVALID_TARGET
        protected GeneratedPtr RaiseSyntaxErrorInvalidTarget(string targetType, GeneratedPtr node)
        {
            var invalid = GetInvalidTarget(node as GeneratedExpr, targetType);
            if (invalid == null)
                return null;
            var a = Where(invalid);
            string message = targetType == "use in for loop"
                ? $"cannot assign to {PyParserRuntime.GetExprName(invalid)}"
                : $"cannot {targetType} {PyParserRuntime.GetExprName(invalid)}";
            throw ErrorKnownLocation(new PySyntaxError(message), a.Line, a.Col, a.EndLine, a.EndCol, message);
        }

        // _PyPegen_get_invalid_target
        private static GeneratedExpr GetInvalidTarget(GeneratedExpr e, string targetType)
        {
            if (e == null)
                return null;
            GeneratedExpr Visit(GeneratedSeq elts)
            {
                for (int i = 0; i < (elts?.Count ?? 0); i++)
                {
                    var child = GetInvalidTarget((GeneratedExpr)elts[i], targetType);
                    if (child != null)
                        return child;
                }
                return null;
            }
            switch (e)
            {
                case GeneratedList list:
                    return Visit(list.Elts);
                case GeneratedTuple tuple:
                    return Visit(tuple.Elts);
                case GeneratedStarred starred:
                    return targetType == "delete" ? e : GetInvalidTarget(starred.Value, targetType);
                case GeneratedCompare compare:
                    if (targetType == "use in for loop" && compare.Ops[0] is GeneratedIn)
                        return GetInvalidTarget(compare.Left, targetType);
                    return e;
                case GeneratedName or GeneratedSubscript or GeneratedAttribute:
                    return null;
                default:
                    return e;
            }
        }

        // raise_unclosed_parentheses_error
        private Exception UnclosedParenthesesError()
        {
            int errorLineno = _tok.ParenLinenoStack[_tok.Level - 1];
            int errorCol = _tok.ParenColStack[_tok.Level - 1];
            string message = $"'{_tok.ParenStack[_tok.Level - 1]}' was never closed";
            return ErrorKnownLocation(new PySyntaxError(message), errorLineno, errorCol, errorLineno, -1, message);
        }

        // _Pypegen_tokenizer_error
        private void TokenizerError()
        {
            if (_tok.Error != null)
                throw _tok.Error;

            string message;
            PySyntaxError error;
            int colOffset = -1;
            switch (_tok.Done)
            {
                case Tokenizer.State.E_EOF:
                    if (_tok.Level != 0)
                        throw UnclosedParenthesesError();
                    RaiseSyntaxError("unexpected EOF while parsing");
                    return;
                case Tokenizer.State.E_DEDENT:
                    RaiseIndentationError("unindent does not match any outer indentation level");
                    return;
                case Tokenizer.State.E_TABSPACE:
                    message = "inconsistent use of tabs and spaces in indentation";
                    error = new PyTabError(message);
                    break;
                case Tokenizer.State.E_TOODEEP:
                    message = "too many levels of indentation";
                    error = new PyIndentationError(message);
                    break;
                case Tokenizer.State.E_LINECONT:
                    colOffset = _tok.ByteLength(_tok.Buf, _tok.Cur) - 1;
                    message = "unexpected character after line continuation character";
                    error = new PySyntaxError(message);
                    break;
                default:
                    message = "unknown parsing error";
                    error = new PySyntaxError(message);
                    break;
            }
            throw ErrorKnownLocation(error, _tok.Lineno, colOffset >= 0 ? colOffset : 0, _tok.Lineno, -1, message);
        }

        // _is_end_of_source
        private bool IsEndOfSource => _tok.Done is Tokenizer.State.E_EOF or Tokenizer.State.E_EOFS or Tokenizer.State.E_EOLS;

        // bad_single_statement
        private bool BadSingleStatement()
        {
            string buffer = _tok.Buffer;
            int cur = _tok.Cur;
            char At(int index) => index < buffer.Length ? buffer[index] : '\0';
            char c = At(cur);
            while (true)
            {
                while (c == ' ' || c == '\t' || c == '\n' || c == '\f')
                    c = At(++cur);
                if (c == '\0')
                    return false;
                if (c != '#')
                    return true;
                while (c != '\0' && c != '\n')
                    c = At(++cur);
            }
        }

        // _PyPegen_run_parser
        protected GeneratedPtr RunParser(Func<GeneratedPtr> rule)
        {
            bool firstPass = true;
            try
            {
                _callInvalidRules = false;
                var result = rule();
                if (result != null)
                {
                    if (_startRule == CompileMode.Single && BadSingleStatement())
                        RaiseSyntaxError("multiple statements found while compiling a single statement");
                    return result;
                }
                firstPass = false;
                if ((_flags & PyCF_ALLOW_INCOMPLETE_INPUT) != 0 && IsEndOfSource)
                    RaiseSyntaxError("incomplete input");
                var lastToken = LastFilledToken;

                // reset_parser_state_for_error_pass
                foreach (var token in _tokens)
                    token.Memo = null;
                _lrCache.Clear();
                _memoCache.Clear();
                _position = 0;
                _callInvalidRules = true;
                _tok.ReportWarnings = false;

                rule();
                throw SetSyntaxError(lastToken);
            }
            catch (PythonException error) when (error.PyException is PySyntaxError && !_tokenizedToTheEnd)
            {
                if (firstPass && (_flags & PyCF_ALLOW_INCOMPLETE_INPUT) != 0 && IsEndOfSource)
                {
                    _tokenizedToTheEnd = true;
                    RaiseSyntaxError("incomplete input");
                }
                // Tokenizer errors take priority over those of the parser, if it got no further.
                if (_tok.Done == Tokenizer.State.E_OK)
                    throw TokenizeFullSourceToCheckForErrors(error);
                throw;
            }
        }

        private bool _tokenizedToTheEnd;

        // _PyPegen_tokenize_full_source_to_check_for_errors
        private Exception TokenizeFullSourceToCheckForErrors(PythonException error)
        {
            _tokenizedToTheEnd = true;
            var currentToken = _knownErrToken ?? LastFilledToken;
            int currentErrLine = currentToken.Line;
            _tok.Error = null;
            Exception found = null;
            while (true)
            {
                var token = _tok.Get();
                if (token.Type == PyToken.Type.ERRORTOKEN)
                {
                    if (_tok.Error != null)
                        found = _tok.Error;
                    else if (_tok.Level != 0 && currentErrLine > _tok.ParenLinenoStack[_tok.Level - 1])
                        found = UnclosedParenthesesError();
                    break;
                }
                if (token.Type == PyToken.Type.ENDMARKER)
                    break;
            }
            // An error in the expression part of an f-string is not swallowed by what comes after it.
            return found != null && _tok.ModeStackIndex <= 0 ? found : error;
        }

        // _Pypegen_set_syntax_error, with no error set
        private Exception SetSyntaxError(GeneratedTokenInfo lastToken)
        {
            // Parser encountered EOF (End of File) unexpectedtly
            if (lastToken.Type == PyToken.Type.ERRORTOKEN && _tok.Done == Tokenizer.State.E_EOF)
            {
                if (_tok.Level != 0)
                    return UnclosedParenthesesError();
                RaiseSyntaxError("unexpected EOF while parsing");
            }
            // Indentation error in the tokenizer
            if (lastToken.Type == PyToken.Type.INDENT)
                RaiseIndentationError("unexpected indent");
            if (lastToken.Type == PyToken.Type.DEDENT)
                RaiseIndentationError("unexpected unindent");
            // Unknown error (generic case): the last token of the first pass, so that the second
            // pass reaching further does not move a generic error.
            var at = Where(lastToken);
            const string message = "invalid syntax";
            var generic = (PythonException)ErrorKnownLocation(new PySyntaxError(message), at.Line, at.Col, at.EndLine, at.EndCol, message);
            return TokenizeFullSourceToCheckForErrors(generic);
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
        /// <summary>
        /// EXTRA of an action: _start_* from the token where this rule began, _end_* from
        /// _PyPegen_get_last_nonnwhitespace_token. Nested rules have run by now, so it is
        /// asked for here and not kept from before them.
        /// </summary>
        protected void CaptureAction(int ruleStart)
        {
            if (ruleStart < _tokens.Count)
                _startToken = _tokens[ruleStart];
            for (int m = _position - 1; m >= 0; m--)
            {
                var token = _tokens[m];
                if (token.Type != PyToken.Type.ENDMARKER && token.Type != PyToken.Type.NEWLINE &&
                    token.Type != PyToken.Type.INDENT && token.Type != PyToken.Type.DEDENT)
                {
                    _lastToken = token;
                    break;
                }
            }
        }

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
            // SharpPy targets Python 3.12, so all features up to 3.12 are enabled
            // version parameter is the Python minor version (e.g., 10 for Python 3.10, 12 for Python 3.12)
            // In CPython, this would raise RAISE_SYNTAX_ERROR_STARTING_FROM for older versions
            const int CURRENT_VERSION = 12; // Python 3.12

            if (version > CURRENT_VERSION)
            {
                // Feature requires a newer Python version
                throw new SyntaxErrorException($"{message} requires Python 3.{version} or newer");
            }

            return node;
        }

        /// <summary>
        /// Expect an operator token
        /// CPython 3.12: Use exact token type (PLUS, LPAR, etc.) for efficient matching
        /// NOTE: This method is no longer used. ParserGenerator now generates ExpectToken calls directly.
        /// Kept for backward compatibility or manual parser code if needed.
        /// </summary>
        /*
        protected GeneratedTokenInfo ExpectOp(string op)
        {
            // Use exact token type for efficient type-only matching
            var tokenType = PyToken.GetOpType(op, exactType: true);
            return ExpectToken(tokenType);  // Type-only check, no value comparison needed
        }
        */

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
        /// Parse optional element [item]
        /// CPython 3.12: Returns item or null
        /// </summary>
        protected GeneratedPtr ParseOptional(Func<GeneratedPtr> parser)
        {
            // A rule that fails puts the position back itself. A group that matched with an empty
            // action, like [':' [expression] { d }], also answers null and keeps what it consumed.
            return parser();
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
