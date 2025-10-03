using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.PegGenerator.Grammar;
using SharpPy.Generated;
using SharpPy.Tokenizer;

namespace SharpPy.PegGenerator.Interpreter
{
    /// <summary>
    /// Parser context types for tracking parsing state
    /// CPython 3.12 compatible context management
    /// </summary>
    public enum ContextType
    {
        Module,
        Function,
        Class,
        Loop,
        Async,
        Lambda,
        Comprehension
    }

    /// <summary>
    /// Parser context information for validation
    /// </summary>
    public class ParserContext
    {
        public ContextType Type { get; set; }
        public int NestingLevel { get; set; }
        public int StartPosition { get; set; }
        public string? Name { get; set; } // Function/class name for debugging

        public ParserContext(ContextType type, int nestingLevel = 0, int startPosition = 0, string? name = null)
        {
            Type = type;
            NestingLevel = nestingLevel;
            StartPosition = startPosition;
            Name = name;
        }

        public override string ToString()
        {
            return $"{Type}({Name ?? "anonymous"}) at level {NestingLevel}, pos {StartPosition}";
        }
    }

    /// <summary>
    /// Legacy AST node types for backward compatibility
    /// </summary>
    public class LegacySimpleStmt
    {
        public string? Type { get; set; }
        public GeneratedPtr? Data { get; set; }
    }

    public class LegacySimpleExpr
    {
        public string? Type { get; set; }
        public GeneratedPtr? Data { get; set; }
    }

    /// <summary>
    /// Simple AST node types for temporary parsing results
    /// </summary>
    public class SimpleModule
    {
        public List<GeneratedPtr>? Body { get; set; }
    }

    public class SimpleStmt
    {
        public string? Type { get; set; }
        public GeneratedPtr? Data { get; set; }
    }

    public class SimpleExpr
    {
        public string? Type { get; set; }
        public GeneratedPtr? Data { get; set; }
    }


    /// <summary>
    /// PEG Grammar Interpreter - executes python.gram rules dynamically
    /// This replaces hardcoded parser methods with a generic rule execution engine
    /// </summary>
    public class PegInterpreter
    {
        private readonly Grammar.Grammar _grammar;
        private readonly List<ITokenInfo> _tokens;
        private int _position;
        // ===== Performance Optimization: Cache Management =====
        private readonly Dictionary<(int, string), IPegParseResult> _memoCache = new();
        private readonly Queue<(int, string)> _cacheAccessOrder = new();
        private const int MAX_CACHE_SIZE = 10000; // CPython 3.12 style cache limit
        private int _cacheHits = 0;
        private int _cacheMisses = 0;

        private readonly Dictionary<string, GeneratedPtr?> _variables = new();
        private readonly HashSet<(int, string)> _activeRules = new(); // Track active rules to prevent left recursion
        private readonly Dictionary<string, bool> _leftRecursiveRules = new(); // Cache for left-recursive rule detection
        private readonly Dictionary<string, IPegParseResult> _seedResults = new(); // Store seed results for left-recursive expansion

        // ===== Advanced Left Recursion Support (CPython 3.12 Style) =====
        private readonly Dictionary<string, HashSet<string>> _leftRecursiveDependencies = new(); // Track indirect dependencies
        private readonly HashSet<string> _currentRecursionStack = new(); // Track current recursion chain
        private readonly Dictionary<string, int> _recursionDepth = new(); // Track recursion depth for each rule
        private readonly Dictionary<int, int> _positionAttempts = new(); // Track attempts at each position for infinite loop detection

        // ===== Parser Context Stack (CPython 3.12 Style) =====
        private readonly Stack<ParserContext> _contextStack = new();
        private int _indentLevel = 0; // Track current indentation level

        public PegInterpreter(Grammar.Grammar grammar, List<ITokenInfo> tokens)
        {
            _grammar = grammar ?? throw new ArgumentNullException(nameof(grammar));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _position = 0;

            // Initialize with module context
            _contextStack.Push(new ParserContext(ContextType.Module, 0, 0, "<module>"));
        }

        /// <summary>
        /// Current token for parsing
        /// </summary>
        private ITokenInfo? CurrentToken => _position < _tokens.Count ? _tokens[_position] : null;

        /// <summary>
        /// Check if we're at end of tokens
        /// </summary>
        private bool IsAtEnd => _position >= _tokens.Count;

        /// <summary>
        /// Set the current position (for synchronization with external parser)
        /// </summary>
        public void SetPosition(int position)
        {
            _position = position;
            _positionAttempts.Clear();
        }

        /// <summary>
        /// Get the current position
        /// </summary>
        public int GetPosition()
        {
            return _position;
        }

        /// <summary>
        /// Advance to next token
        /// </summary>
        private void Advance()
        {
            if (_position < _tokens.Count)
            {
                _position++;
                // Reset position attempt counter when we advance
                _positionAttempts.Clear();
            }
        }

        /// <summary>
        /// Mark current position for backtracking
        /// </summary>
        private int Mark()
        {
            // Infinite loop detection - aggressive threshold for early detection
            if (_positionAttempts.ContainsKey(_position))
            {
                _positionAttempts[_position]++;
                if (_positionAttempts[_position] > 20) // Lower threshold for faster infinite loop detection
                {
                    var tokenInfo = CurrentToken != null ? $"{CurrentToken.Type}('{CurrentToken.Value}')" : "EOF";
                    var activeRulesInfo = _activeRules.Count > 0 ?
                        string.Join(", ", _activeRules.Select(ar => $"{ar.Item2}@{ar.Item1}")) : "none";

                    throw new InvalidOperationException(
                        $"Infinite loop detected at token position {_position}. " +
                        $"Current token: {tokenInfo}. " +
                        $"Active rules: {activeRulesInfo}. " +
                        $"Attempts at this position: {_positionAttempts[_position]}");
                }
            }
            else
            {
                _positionAttempts[_position] = 1;
            }

            return _position;
        }

        /// <summary>
        /// Reset position for backtracking
        /// </summary>
        private void Reset(int mark) => _position = mark;

        /// <summary>
        /// Check if a rule is left-recursive (direct, indirect, or mutual)
        /// CPython 3.12 compatible advanced left recursion detection
        /// </summary>
        private bool IsLeftRecursive(Rule rule)
        {
            if (_leftRecursiveRules.TryGetValue(rule.Name, out var cached))
            {
                return cached;
            }

            // Exclude certain rules from left recursion handling to preserve correct parsing behavior
            // These rules rely on token boundaries (INDENT/DEDENT) that must be processed sequentially
            if (rule.Name == "block" || rule.Name == "statements" || rule.Name == "statement")
            {
                Console.WriteLine($"[LEFT-REC] Excluding rule '{rule.Name}' from left recursion detection");
                _leftRecursiveRules[rule.Name] = false;
                return false;
            }

            // Clear recursion tracking for this check
            _currentRecursionStack.Clear();
            _recursionDepth.Clear();

            // Check for any form of left recursion
            var isLeftRec = CheckIndirectLeftRecursion(rule.Name, rule.Name);

            Console.WriteLine($"[LEFT-REC] Rule '{rule.Name}' left-recursive check: {isLeftRec}");
            if (rule.Name == "primary")
            {
                Console.WriteLine($"[LEFT-REC] Primary rule has {rule.Alternatives.Count} alternatives");
                for (int i = 0; i < rule.Alternatives.Count; i++)
                {
                    var alt = rule.Alternatives[i];
                    var firstItem = alt.Items.Count > 0 ? alt.Items[0] : null;
                    var firstAtom = firstItem?.Atom;
                    var isRuleRef = firstAtom is RuleRef ruleRef;
                    var refName = isRuleRef ? ((RuleRef)firstAtom).Name : "not-ruleref";
                    Console.WriteLine($"[LEFT-REC]   Alt[{i}]: {alt.Items.Count} items, first={firstAtom?.GetType().Name}({refName})");
                }
            }

            _leftRecursiveRules[rule.Name] = isLeftRec;
            return isLeftRec;
        }

        /// <summary>
        /// Check for indirect left recursion using depth-first search
        /// This detects patterns like: A -> B, B -> A (mutual) or A -> B, B -> C, C -> A (indirect)
        /// </summary>
        private bool CheckIndirectLeftRecursion(string originalRule, string currentRule)
        {
            // Prevent infinite recursion during detection
            if (_currentRecursionStack.Contains(currentRule))
            {
                // Found a cycle - check if it involves the original rule
                return currentRule == originalRule;
            }

            // Prevent too deep recursion
            if (_recursionDepth.GetValueOrDefault(currentRule, 0) > 50)
            {
                return false;
            }

            _currentRecursionStack.Add(currentRule);
            _recursionDepth[currentRule] = _recursionDepth.GetValueOrDefault(currentRule, 0) + 1;

            try
            {
                // Get the rule definition
                var rule = _grammar.Rules.FirstOrDefault(r => r.Name == currentRule);
                if (rule == null) return false;

                // Check each alternative
                foreach (var alternative in rule.Alternatives)
                {
                    if (alternative.Items.Count == 0) continue;

                    var firstItem = alternative.Items[0];
                    if (firstItem.Atom is RuleRef ruleRef)
                    {
                        var referencedRule = ruleRef.Name;

                        // Direct left recursion
                        if (referencedRule == originalRule)
                        {
                            return true;
                        }

                        // Indirect left recursion - recurse
                        if (CheckIndirectLeftRecursion(originalRule, referencedRule))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            finally
            {
                _currentRecursionStack.Remove(currentRule);
                if (_recursionDepth.ContainsKey(currentRule))
                {
                    _recursionDepth[currentRule]--;
                    if (_recursionDepth[currentRule] <= 0)
                    {
                        _recursionDepth.Remove(currentRule);
                    }
                }
            }
        }

        /// <summary>
        /// Main entry point: Parse a rule by name
        /// </summary>
        public IPegParseResult ParseRule(string ruleName)
        {
#if DEBUG_LOG
            // Add debug for assignment-related rules
            if (ruleName == "assignment" || ruleName == "simple_stmt" || ruleName == "simple_stmts")
            {
                Console.WriteLine($"[DEBUG] PEG ParseRule: Attempting '{ruleName}' at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'");
            }
#endif

            // Check if this rule should be excluded in first pass
            if (ShouldExcludeRuleInFirstPass(ruleName))
            {
                Console.WriteLine($"[2-PASS] Excluding invalid rule '{ruleName}' in first pass");
                return PegFailure.Instance;
            }

            // Check memoization cache with performance tracking
            var cacheKey = (_position, ruleName);
            if (TryGetFromCache(cacheKey, out var cachedResult))
            {
                // Console.WriteLine($"[MEMO] Cache hit for {ruleName} at position {_position}");
                return cachedResult;
            }

            // Find the rule in grammar
            var rule = _grammar.Rules.FirstOrDefault(r => r.Name == ruleName);
            if (rule == null)
            {
                // Console.WriteLine($"[DEBUG] Rule not found: {ruleName}");
                return PegFailure.Instance;
            }

            // Check if this is a left-recursive rule and handle specially
            if (IsLeftRecursive(rule))
            {
                return ParseLeftRecursiveRule(ruleName, rule);
            }

            // Handle normal (non-left-recursive) rules
            return ParseNormalRule(ruleName, rule);
        }

        /// <summary>
        /// Parse normal (non-left-recursive) rules
        /// </summary>
        private IPegParseResult ParseNormalRule(string ruleName, Rule rule)
        {
            var cacheKey = (_position, ruleName);

            // Prevent infinite recursion by detecting active rules
            if (_activeRules.Contains(cacheKey))
            {
                // Console.WriteLine($"[RECURSION] Infinite recursion detected for {ruleName} at position {_position}");
                return PegFailure.Instance;
            }

            // Mark rule as active
            _activeRules.Add(cacheKey);

            try
            {
                // Console.WriteLine($"[DEBUG] Parsing normal rule: {ruleName} at position {_position}");

                // Try each alternative (PEG ordered choice)
                foreach (var alternative in rule.Alternatives)
                {
                    var mark = Mark();
                    var result = ParseAlternative(alternative);

                    if (result is PegCutResult cutResult && cutResult.IsCutFailure)
                    {
                        // Cut failed - no backtracking to other alternatives allowed
                        Console.WriteLine($"[DEBUG] Rule {ruleName}: Cut failure prevents trying other alternatives");
                        StoreInCache(cacheKey, PegFailure.Instance);
                        return PegFailure.Instance;
                    }
                    else if (result.IsSuccess)
                    {
                        // Success - cache and return
                        StoreInCache(cacheKey, result);
                        // Console.WriteLine($"[DEBUG] Rule {ruleName} succeeded at position {_position}");
                        return result;
                    }

                    // Failed - backtrack
                    Reset(mark);
                }

                // No alternative succeeded
                // Console.WriteLine($"[DEBUG] Rule {ruleName} failed at position {_position}");
                StoreInCache(cacheKey, PegFailure.Instance);
                return PegFailure.Instance;
            }
            finally
            {
                // Remove rule from active set
                _activeRules.Remove(cacheKey);
            }
        }

        /// <summary>
        /// Parse left-recursive rules using seed parsing technique
        /// </summary>
        private IPegParseResult ParseLeftRecursiveRule(string ruleName, Rule rule)
        {
            var cacheKey = (_position, ruleName);

            Console.WriteLine($"[LEFT-REC] Parsing left-recursive rule: {ruleName} at position {_position}, token: {CurrentToken?.Type}('{CurrentToken?.Value}')");

            // Step 1: Find base alternatives (non-recursive ones)
            var baseAlternatives = rule.Alternatives.Where(alt =>
                !(alt.Items.Count > 0 &&
                  alt.Items[0].Atom is RuleRef ruleRef &&
                  ruleRef.Name == ruleName)).ToList();

            // Step 2: Find recursive alternatives
            var recursiveAlternatives = rule.Alternatives.Where(alt =>
                alt.Items.Count > 0 &&
                alt.Items[0].Atom is RuleRef ruleRef &&
                ruleRef.Name == ruleName).ToList();

            if (baseAlternatives.Count == 0)
            {
                // No base case - this shouldn't happen in well-formed grammar
                // Console.WriteLine($"[LEFT-REC] No base alternatives found for {ruleName}");
                return PegFailure.Instance;
            }

            // Step 3: Parse base alternatives to get initial seed
            IPegParseResult? seed = null;
            int seedPosition = _position;

            foreach (var baseAlt in baseAlternatives)
            {
                var mark = Mark();
                var result = ParseAlternative(baseAlt);

                if (result.IsSuccess)
                {
                    seed = result;
                    seedPosition = _position;
                    Console.WriteLine($"[LEFT-REC] Found seed for {ruleName} at position {seedPosition}: {seed}");
                    break;
                }

                Reset(mark);
            }

            if (seed == null)
            {
                // No base case matched
                StoreInCache(cacheKey, PegFailure.Instance);
                return PegFailure.Instance;
            }

            // Step 4: Iteratively expand the seed using recursive alternatives
            const int maxIterations = 1000; // Prevent infinite loops
            int iterationCount = 0;
            int lastPosition = seedPosition;

            while (iterationCount < maxIterations)
            {
                iterationCount++;
                var expandedSeed = seed;
                var expandedPosition = seedPosition;
                var foundExpansion = false;

                foreach (var recursiveAlt in recursiveAlternatives)
                {
                    // Reset to seed position for each recursive alternative
                    Reset(seedPosition);

                    // Temporarily set the seed result for this rule
                    var tempCacheKey = (_position, ruleName);
                    _memoCache[tempCacheKey] = seed; // Direct assignment for left recursion

                    try
                    {
                        var result = ParseAlternative(recursiveAlt);
                        // CPython-style check: must advance position to be a valid expansion
                        if (result.IsSuccess && _position > seedPosition)
                        {
                            // Found a longer parse - update seed
                            expandedSeed = result;
                            expandedPosition = _position;
                            foundExpansion = true;
                            Console.WriteLine($"[LEFT-REC] Iteration {iterationCount}: Expanded seed for {ruleName} from pos {seedPosition} to {_position}");
                            break;
                        }
                    }
                    finally
                    {
                        // Remove temporary cache entry
                        _memoCache.Remove(tempCacheKey);
                    }
                }

                if (!foundExpansion)
                {
                    // No more expansions possible - terminate normally
                    Console.WriteLine($"[LEFT-REC] No expansion found for {ruleName} at iteration {iterationCount}, terminating");
                    break;
                }

                // CPython-style progress check: if position didn't advance, we're stuck
                if (expandedPosition <= lastPosition)
                {
                    Console.WriteLine($"[LEFT-REC] Position didn't advance for {ruleName} (was {lastPosition}, now {expandedPosition}), terminating to prevent infinite loop");
                    break;
                }

                // Update seed for next iteration
                seed = expandedSeed;
                lastPosition = seedPosition;  // Store previous seed position
                seedPosition = expandedPosition;  // New seed position
            }

            if (iterationCount >= maxIterations)
            {
                Console.WriteLine($"[LEFT-REC] WARNING: Maximum iterations ({maxIterations}) reached for rule {ruleName}, terminating to prevent infinite loop");
            }

            // Step 5: Set final position and cache result
            _position = seedPosition;
            StoreInCache(cacheKey, seed);

            Console.WriteLine($"[LEFT-REC] Final result for {ruleName}: {seed} at position {seedPosition}");
            return seed;
        }

        /// <summary>
        /// Parse a single alternative (sequence of items)
        /// </summary>
        private IPegParseResult ParseAlternative(Alternative alternative)
        {
            var results = new List<IPegParseResult>();
            var variables = new Dictionary<string, IPegParseResult>();

            // Console.WriteLine($"[DEBUG] Parsing alternative with {alternative.Items.Count} items");

            // Parse each item in sequence
            foreach (var item in alternative.Items)
            {
                var result = ParseItem(item);

                // Handle cut operations
                if (result is PegCutResult cutResult)
                {
                    if (!cutResult.IsCutFailure)
                    {
                        // Cut succeeded - use the result and continue with committed parse
                        Console.WriteLine($"[DEBUG] Cut succeeded, committed to this alternative");
                        var innerResult = cutResult.InnerResult ?? PegSuccess.Instance;
                        results.Add(innerResult);
                        // Store variable binding if item has a name
                        if (!string.IsNullOrEmpty(item.Name))
                        {
                            variables[item.Name] = innerResult;
                        }
                        // Continue parsing rest of the alternative
                        continue;
                    }
                    else
                    {
                        // Cut failed - this prevents backtracking to other alternatives
                        Console.WriteLine($"[DEBUG] Cut failed at position {cutResult.EndPosition}, no backtracking allowed");
                        return cutResult; // Propagate cut failure up to prevent backtracking
                    }
                }
                else if (!result.IsSuccess)
                {
                    // Item failed - alternative fails
                    // Console.WriteLine($"[DEBUG] Item failed in alternative");
                    return PegFailure.Instance;
                }

                results.Add(result);

                // Store variable binding if item has a name
                if (!string.IsNullOrEmpty(item.Name))
                {
                    variables[item.Name] = result;
                    // Console.WriteLine($"[DEBUG] Variable binding: {item.Name} = {result}");
                }
            }

            // All items succeeded - execute action
            return ExecuteAction(alternative.Action, variables, results);
        }

        /// <summary>
        /// Parse a single item (with optional variable binding)
        /// </summary>
        private IPegParseResult ParseItem(Item item)
        {
            // Console.WriteLine($"[DEBUG] Parsing item: {item.Name}={item.Atom}");
            return ParseAtom(item.Atom);
        }

        /// <summary>
        /// Parse an atomic expression based on its type
        /// </summary>
        private IPegParseResult ParseAtom(Atom atom)
        {
            switch (atom)
            {
                case StringLiteral stringLiteral:
                    return ParseStringLiteral(stringLiteral);

                case RuleRef ruleRef:
                    // Check if this is a token type (uppercase name)
                    if (IsTokenType(ruleRef.Name))
                    {
                        return ParseTokenType(ruleRef.Name);
                    }
                    return ParseRule(ruleRef.Name);

                case Optional optional:
                    return ParseOptional(optional);

                case Group group:
                    return ParseGroup(group);

                case ZeroOrMore zeroOrMore:
                    return ParseZeroOrMore(zeroOrMore);

                case OneOrMore oneOrMore:
                    return ParseOneOrMore(oneOrMore);

                case PositiveLookahead positiveLookahead:
                    return ParsePositiveLookahead(positiveLookahead);

                case NegativeLookahead negativeLookahead:
                    return ParseNegativeLookahead(negativeLookahead);

                case Cut cut:
                    return ParseCut(cut);

                default:
                    Console.WriteLine($"[ERROR] Unknown atom type: {atom.GetType()}");
                    return PegFailure.Instance;
            }
        }

        /// <summary>
        /// Parse string literal (keywords and operators)
        /// </summary>
        private IPegParseResult ParseStringLiteral(StringLiteral stringLiteral)
        {
            var expected = stringLiteral.Value;
            var current = CurrentToken;

            Console.WriteLine($"[DEBUG] Expecting string literal: '{expected}', current token: {current?.Type}('{current?.Value}')");

            if (current == null) return PegFailure.Instance;

            // Handle keywords (they come as NAME tokens)
            if (IsKeyword(expected))
            {
                if (current.Type.ToString() == "NAME" && current.Value == expected)
                {
                    Advance();
                    return new PegTokenResult(current, _position);
                }
            }
            // Handle operators and punctuation (CPython 3.12: all operators are OP tokens)
            else
            {
                // Check if token is OP type with matching value
                if (current.Type.ToString() == "OP" && current.Value == expected)
                {
                    Advance();
                    return new PegTokenResult(current, _position);
                }
            }

            return PegFailure.Instance;
        }

        /// <summary>
        /// Parse optional expression [expr]
        /// </summary>
        private IPegParseResult ParseOptional(Optional optional)
        {
            // Console.WriteLine($"[DEBUG] Parsing optional: [{optional.Expression}]");

            var mark = Mark();
            var result = ParseAtom(optional.Expression);

            if (!result.IsSuccess)
            {
                // Optional failed - reset and return empty success
                Reset(mark);
                // Console.WriteLine($"[DEBUG] Optional failed, continuing");
                return new PegSuccess(_position); // Return success to indicate optional succeeded
            }

            // Console.WriteLine($"[DEBUG] Optional succeeded");
            return result;
        }

        /// <summary>
        /// Parse group (alternatives in parentheses)
        /// </summary>
        private IPegParseResult ParseGroup(Group group)
        {
            Console.WriteLine($"[DEBUG] Parsing group with {group.Alternatives.Count} alternatives");

            // Try each alternative in the group
            foreach (var alternative in group.Alternatives)
            {
                var mark = Mark();
                var result = ParseAlternative(alternative);

                if (result.IsSuccess)
                {
                    return result;
                }

                Reset(mark);
            }

            return PegFailure.Instance;
        }

        /// <summary>
        /// Parse zero or more repetitions expr*
        /// </summary>
        private IPegParseResult ParseZeroOrMore(ZeroOrMore zeroOrMore)
        {
            Console.WriteLine($"[DEBUG] Parsing zero or more: {zeroOrMore.Expression}*");

            var results = new List<IPegParseResult>();

            while (true)
            {
                var mark = Mark();
                var result = ParseAtom(zeroOrMore.Expression);

                if (!result.IsSuccess)
                {
                    Reset(mark);
                    break;
                }

                results.Add(result);
            }

            Console.WriteLine($"[DEBUG] Zero or more matched {results.Count} items");
            return new PegListResult(results, _position);
        }

        /// <summary>
        /// Parse one or more repetitions expr+
        /// </summary>
        private IPegParseResult ParseOneOrMore(OneOrMore oneOrMore)
        {
            Console.WriteLine($"[DEBUG] Parsing one or more: {oneOrMore.Expression}+");

            var results = new List<IPegParseResult>();

            // Must match at least once
            var firstResult = ParseAtom(oneOrMore.Expression);
            if (!firstResult.IsSuccess)
            {
                return PegFailure.Instance;
            }

            results.Add(firstResult);

            // Then zero or more additional matches
            while (true)
            {
                var mark = Mark();
                var result = ParseAtom(oneOrMore.Expression);

                if (!result.IsSuccess)
                {
                    Reset(mark);
                    break;
                }

                results.Add(result);
            }

            Console.WriteLine($"[DEBUG] One or more matched {results.Count} items");
            return new PegListResult(results, _position);
        }

        /// <summary>
        /// Parse positive lookahead &expr
        /// </summary>
        private IPegParseResult ParsePositiveLookahead(PositiveLookahead positiveLookahead)
        {
            Console.WriteLine($"[DEBUG] Parsing positive lookahead: &{positiveLookahead.Expression}");

            var mark = Mark();
            var result = ParseAtom(positiveLookahead.Expression);

            // Always reset position (lookahead doesn't consume)
            Reset(mark);

            // Return success/failure based on whether expression matched
            return result.IsSuccess ? new PegSuccess(_position) : PegFailure.Instance;
        }

        /// <summary>
        /// Parse negative lookahead !expr
        /// </summary>
        private IPegParseResult ParseNegativeLookahead(NegativeLookahead negativeLookahead)
        {
            Console.WriteLine($"[DEBUG] Parsing negative lookahead: !{negativeLookahead.Expression}");

            var mark = Mark();
            var result = ParseAtom(negativeLookahead.Expression);

            // Always reset position (lookahead doesn't consume)
            Reset(mark);

            // Return success if expression did NOT match
            return !result.IsSuccess ? new PegSuccess(_position) : PegFailure.Instance;
        }

        /// <summary>
        /// Parse cut operator ~expr - prevents backtracking beyond this point
        /// In CPython 3.12, cut operator commits to the current alternative and
        /// prevents exploring other alternatives in case of failure
        /// </summary>
        private IPegParseResult ParseCut(Cut cut)
        {
            Console.WriteLine($"[DEBUG] ParseCut: Cut operator encountered at position {_position}");

            // Save the current position - this is our "cut point"
            var cutPosition = _position;

            // Try to parse the expression after the cut
            var result = ParseAtom(cut.Expression);

            if (result.IsSuccess)
            {
                Console.WriteLine($"[DEBUG] ParseCut: Expression after cut succeeded, committing to this path");
                // Success - mark this as a "committed" parse by setting a special flag
                // The cut succeeds and we return the result
                return CreateCutSuccess(result, cutPosition);
            }
            else
            {
                Console.WriteLine($"[DEBUG] ParseCut: Expression after cut failed, cut prevents backtracking");
                // Failure after cut - this should prevent backtracking to earlier alternatives
                // In a full implementation, this would throw a special exception or set a flag
                // For now, we'll return a special failure marker
                return CreateCutFailure(cutPosition);
            }
        }

        /// <summary>
        /// Helper methods for creating cut results
        /// </summary>
        private static IPegParseResult CreateCutSuccess(IPegParseResult result, int cutPosition)
        {
            return new PegCutResult(false, result, cutPosition);
        }

        private static IPegParseResult CreateCutFailure(int cutPosition)
        {
            return new PegCutResult(true, null, cutPosition);
        }

        // ===== Performance Optimization: Cache Management Methods =====

        /// <summary>
        /// Try to get value from cache with LRU tracking
        /// </summary>
        private bool TryGetFromCache((int, string) key, out IPegParseResult result)
        {
            if (_memoCache.TryGetValue(key, out result))
            {
                _cacheHits++;
                // Update access order for LRU
                UpdateCacheAccess(key);
                return true;
            }

            _cacheMisses++;
            return false;
        }

        /// <summary>
        /// Store value in cache with size management
        /// </summary>
        private void StoreInCache((int, string) key, IPegParseResult value)
        {
            // Evict oldest entries if cache is full
            while (_memoCache.Count >= MAX_CACHE_SIZE)
            {
                EvictOldestCacheEntry();
            }

            _memoCache[key] = value;
            _cacheAccessOrder.Enqueue(key);
        }

        /// <summary>
        /// Update cache access order for LRU
        /// </summary>
        private void UpdateCacheAccess((int, string) key)
        {
            // For simplicity, we just add to queue again
            // In a full LRU implementation, we would remove from middle and add to end
            _cacheAccessOrder.Enqueue(key);
        }

        /// <summary>
        /// Evict oldest cache entry (LRU policy)
        /// </summary>
        private void EvictOldestCacheEntry()
        {
            if (_cacheAccessOrder.Count > 0)
            {
                var oldestKey = _cacheAccessOrder.Dequeue();
                _memoCache.Remove(oldestKey);
                Console.WriteLine($"[CACHE] Evicted entry: {oldestKey}");
            }
        }

        /// <summary>
        /// Get cache performance statistics
        /// </summary>
        public string GetCacheStats()
        {
            var hitRate = _cacheHits + _cacheMisses > 0
                ? (double)_cacheHits / (_cacheHits + _cacheMisses) * 100
                : 0;

            return $"Cache Stats: {_cacheHits} hits, {_cacheMisses} misses, " +
                   $"{hitRate:F1}% hit rate, {_memoCache.Count}/{MAX_CACHE_SIZE} entries";
        }

        /// <summary>
        /// Clear performance counters
        /// </summary>
        public void ResetCacheStats()
        {
            _cacheHits = 0;
            _cacheMisses = 0;
        }

        /// <summary>
        /// Execute action with parsed results and variable bindings
        /// </summary>
        private IPegParseResult ExecuteAction(string? action, Dictionary<string, IPegParseResult> variables, List<IPegParseResult> results)
        {
            if (string.IsNullOrEmpty(action))
            {
                // No action - return first result or success marker
                return results.FirstOrDefault() ?? new PegSuccess(_position);
            }

            Console.WriteLine($"[DEBUG] Executing action: {action}");
            Console.WriteLine($"[DEBUG] Variables: {string.Join(", ", variables.Select(kv => $"{kv.Key}={kv.Value}"))}");
            Console.WriteLine($"[DEBUG] Results count: {results.Count}");

            // Parse the action to generate appropriate AST node
            return ParseAction(action, variables, results);
        }

        /// <summary>
        /// Parse semantic action and create corresponding AST node
        /// </summary>
        private IPegParseResult ParseAction(string action, Dictionary<string, IPegParseResult> variables, List<IPegParseResult> results)
        {
            // Handle common action patterns
            if (action.Contains("_PyPegen_set_expr_context"))
            {
                // Expression context setting: _PyPegen_set_expr_context(p, a, Store)
                // Just return the variable 'a' as this is primarily for context setting
                if (variables.ContainsKey("a"))
                {
                    return variables["a"];
                }
                return results.FirstOrDefault() ?? new PegSuccess(_position);
            }
            else if (action.Contains("CHECK_VERSION"))
            {
                // Handle version checks - for Python 3.12 interpreter, allow all modern features
                // Extract the version requirement (typically version 6 for annotations)
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: CHECK_VERSION called, allowing modern Python features");
#endif

                // For now, always allow version checks and return the actual AST construction
                if (action.Contains("_PyAST_AnnAssign"))
                {
                    // Annotated Assignment: a=NAME ':' b=expression c=['=' d=annotated_rhs { d }]
                    var stmt = new LegacySimpleStmt
                    {
                        Type = "ann_assign",
                        Data = new {
                            Target = variables.ContainsKey("a") ? variables["a"] : null,
                            Annotation = variables.ContainsKey("b") ? variables["b"] : null,
                            Value = variables.ContainsKey("c") ? variables["c"] : null,
                            Simple = 1 // Always 1 for NAME annotations as per CPython
                        }
                    };
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] PEG: Created ann_assign statement");
#endif
                    return new PegAstResult(stmt, _position);
                }

                // For other version checks, continue with normal processing
                return results.FirstOrDefault() ?? new PegSuccess(_position);
            }
            else if (action.Contains("_PyAST_AnnAssign"))
            {
                // Annotated Assignment: a=NAME ':' b=expression c=['=' d=annotated_rhs { d }]
                var stmt = new LegacySimpleStmt
                {
                    Type = "ann_assign",
                    Data = new {
                        Target = variables.ContainsKey("a") ? variables["a"] : null,
                        Annotation = variables.ContainsKey("b") ? variables["b"] : null,
                        Value = variables.ContainsKey("c") ? variables["c"] : null,
                        Simple = 1 // Always 1 for NAME annotations as per CPython
                    }
                };
                return new PegAstResult(stmt, _position);
            }
            else if (action.Contains("_PyAST_TryStar"))
            {
                // Try statement with except* handlers (Python 3.11+ Exception Groups)
                var stmt = new LegacySimpleStmt
                {
                    Type = "try_star",
                    Data = new {
                        body = variables.ContainsKey("b") ? variables["b"] : null,
                        handlers = variables.ContainsKey("ex") ? variables["ex"] : null,
                        orelse = variables.ContainsKey("el") ? variables["el"] : null,
                        finalbody = variables.ContainsKey("f") ? variables["f"] : null
                    }
                };
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: Created try_star statement");
#endif
                return new PegAstResult(stmt, _position);
            }
            else if (action.Contains("_PyAST_Try"))
            {
                // Regular try statement
                var stmt = new LegacySimpleStmt
                {
                    Type = "try",
                    Data = new {
                        body = variables.ContainsKey("b") ? variables["b"] : null,
                        handlers = variables.ContainsKey("ex") ? variables["ex"] : null,
                        orelse = variables.ContainsKey("el") ? variables["el"] : null,
                        finalbody = variables.ContainsKey("f") ? variables["f"] : null
                    }
                };
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: Created try statement");
#endif
                return new PegAstResult(stmt, _position);
            }
            else if (action.Contains("_PyAST_ExceptHandler"))
            {
                // Exception handler: 'except' [expression ['as' NAME]] ':' block
                var handler = new LegacySimpleStmt
                {
                    Type = "except_handler",
                    Data = new {
                        type = variables.ContainsKey("e") ? variables["e"] : null,
                        name = variables.ContainsKey("t") ? variables["t"] : null,
                        body = variables.ContainsKey("b") ? variables["b"] : null
                    }
                };
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: Created except_handler");
#endif
                return new PegAstResult(handler, _position);
            }
            else if (action.Contains("_PyAST_Assign"))
            {
                // Assignment: a[asdl_expr_seq*]=(z=star_targets '=' { z })+ b=(yield_expr | star_expressions)
                var stmt = new LegacySimpleStmt
                {
                    Type = "assignment",
                    Data = new {
                        Targets = variables.ContainsKey("a") ? variables["a"] : null,
                        Value = variables.ContainsKey("b") ? variables["b"] : null
                    }
                };
                return new PegAstResult(stmt, _position);
            }
            else if (action.Contains("_PyAST_Module"))
            {
                // Module: statements+
                var module = new SimpleModule
                {
                    Body = variables.ContainsKey("a") ? variables["a"] as List<object> : new List<object>()
                };
                return new PegAstResult(module, _position);
            }
            else if (action.Contains("_PyAST_Name"))
            {
                // Name expression: NAME
                var expr = new LegacySimpleExpr
                {
                    Type = "name",
                    Data = variables.ContainsKey("id") ? variables["id"] : null
                };
                return new PegAstResult(expr, _position);
            }
            else if (action.Contains("_PyAST_Constant") || action.Contains("_PyAST_Num"))
            {
                // Constant/Number expression
                var expr = new LegacySimpleExpr
                {
                    Type = "constant",
                    Data = variables.ContainsKey("value") ? variables["value"] : null
                };
                return new PegAstResult(expr, _position);
            }
            else if (action.Contains("_PyAST_BinOp"))
            {
                // Binary operation: left op right
                var expr = new LegacySimpleExpr
                {
                    Type = "binop",
                    Data = new {
                        Left = variables.ContainsKey("left") ? variables["left"] : null,
                        Op = variables.ContainsKey("op") ? variables["op"] : null,
                        Right = variables.ContainsKey("right") ? variables["right"] : null
                    }
                };
                return new PegAstResult(expr, _position);
            }
            else if (action.Contains("_PyAST_TypeAlias"))
            {
                // Type alias: "type" n=NAME t=[type_params] '=' b=expression
                var stmt = new LegacySimpleStmt
                {
                    Type = "type_alias",
                    Data = new {
                        Name = variables.ContainsKey("n") ? variables["n"] : null,
                        TypeParams = variables.ContainsKey("t") ? variables["t"] : null,
                        Value = variables.ContainsKey("b") ? variables["b"] : null
                    }
                };
                return new PegAstResult(stmt, _position);
            }
            else if (action.Contains("_PyAST_AsyncFunctionDef"))
            {
                // Async function definition: ASYNC 'def' n=NAME params=[params] b=block
                var stmt = new LegacySimpleStmt
                {
                    Type = "async_function_def",
                    Data = new {
                        Name = variables.ContainsKey("n") ? variables["n"] : null,
                        Params = variables.ContainsKey("params") ? variables["params"] : null,
                        Body = variables.ContainsKey("b") ? variables["b"] : null,
                        TypeParams = variables.ContainsKey("t") ? variables["t"] : null,
                        Returns = variables.ContainsKey("a") ? variables["a"] : null
                    }
                };
                return new PegAstResult(stmt, _position);
            }
            else if (action.Contains("_PyAST_Break"))
            {
                // Break statement: 'break' { _PyAST_Break(EXTRA) }
                var stmt = new LegacySimpleStmt
                {
                    Type = "break",
                    Data = null
                };
                return new PegAstResult(stmt, _position);
            }
            else if (action.Contains("_PyAST_Continue"))
            {
                // Continue statement: 'continue' { _PyAST_Continue(EXTRA) }
                var stmt = new LegacySimpleStmt
                {
                    Type = "continue",
                    Data = null
                };
                return new PegAstResult(stmt, _position);
            }

            // Default: return generic success marker for now
            Console.WriteLine($"[DEBUG] Unhandled action pattern: {action}");
            var defaultResult = new { Action = action, Variables = variables, Results = results };
            return new PegActionResult(defaultResult, _position);
        }

        /// <summary>
        /// Check if a name refers to a token type (from GeneratedTokenType enum)
        /// </summary>
        private bool IsTokenType(string name)
        {
            // Check if the name corresponds to a GeneratedTokenType enum value
            return Enum.TryParse<GeneratedTokenType>(name, out _);
        }

        /// <summary>
        /// Parse a token type by matching current token against expected type
        /// CPython 3.12: NAME tokens cannot be keywords
        /// </summary>
        private IPegParseResult ParseTokenType(string tokenType)
        {
            var current = CurrentToken;
            if (current == null) return PegFailure.Instance;

            Console.WriteLine($"[DEBUG] Expecting token type: {tokenType}, current token: {current.Type}('{current.Value}')");

            if (current.Type.ToString() == tokenType)
            {
                // CPython 3.12: NAME tokens cannot be keywords (in, not, for, etc.)
                if (tokenType == "NAME" && IsKeyword(current.Value))
                {
                    Console.WriteLine($"[DEBUG] Rejecting keyword '{current.Value}' as NAME token");
                    return PegFailure.Instance;
                }

                Advance();
                return new PegTokenResult(current, _position);
            }

            return PegFailure.Instance;
        }

        /// <summary>
        /// Check if string is a Python keyword
        /// </summary>
        private bool IsKeyword(string value)
        {
            var keywords = new HashSet<string>
            {
                "and", "as", "assert", "async", "await", "break", "case", "class", "continue",
                "def", "del", "elif", "else", "except", "False", "finally", "for", "from",
                "global", "if", "import", "in", "is", "lambda", "match", "None", "nonlocal",
                "not", "or", "pass", "raise", "return", "True", "try", "type", "while", "with", "yield"
            };
            return keywords.Contains(value);
        }

        /// <summary>
        /// Map string literals to token types
        /// </summary>

        // ===== 2-Pass Parsing Support for Invalid Rules =====

        /// <summary>
        /// Indicates whether we're in the first pass (excluding invalid rules) or second pass (including invalid rules)
        /// </summary>
        private bool _isFirstPass = true;
        private int _firstPassFailurePosition = -1;

        /// <summary>
        /// Parse a rule with 2-pass support for better error messages
        /// This is the new main entry point that handles invalid rules properly
        /// </summary>
        public IPegParseResult ParseRuleWithTwoPass(string ruleName)
        {
            Console.WriteLine($"[2-PASS] Starting 2-pass parsing for rule: {ruleName}");

            // First pass: exclude invalid rules
            _isFirstPass = true;
            _firstPassFailurePosition = -1;
            var firstPassPosition = _position;

            var result = ParseRule(ruleName);
            if (result.IsSuccess)
            {
                Console.WriteLine($"[2-PASS] First pass succeeded for rule: {ruleName}");
                return result;
            }

            // First pass failed - record failure position
            _firstPassFailurePosition = _position;
            Console.WriteLine($"[2-PASS] First pass failed for rule: {ruleName} at position {_firstPassFailurePosition}");

            // Reset position for second pass
            _position = firstPassPosition;
            _memoCache.Clear(); // Clear memoization cache for second pass

            // Second pass: include invalid rules for better error messages
            _isFirstPass = false;
            Console.WriteLine($"[2-PASS] Starting second pass (with invalid rules) for rule: {ruleName}");

            result = ParseRule(ruleName);
            if (result.IsSuccess)
            {
                Console.WriteLine($"[2-PASS] Second pass succeeded for rule: {ruleName}");
                return result;
            }

            // Both passes failed - use first pass failure position for more accurate error location
            if (_firstPassFailurePosition >= 0)
            {
                _position = _firstPassFailurePosition;
                Console.WriteLine($"[2-PASS] Both passes failed, using first pass failure position: {_firstPassFailurePosition}");
            }

            return PegFailure.Instance;
        }

        /// <summary>
        /// Check if a rule should be excluded in the first pass (is it an invalid rule?)
        /// </summary>
        private bool ShouldExcludeRuleInFirstPass(string ruleName)
        {
            return _isFirstPass && ruleName.StartsWith("invalid_");
        }

        // ===== Parser Context Management (CPython 3.12 Style) =====

        /// <summary>
        /// Push a new context onto the context stack
        /// </summary>
        private void PushContext(ContextType type, string? name = null)
        {
            var newLevel = _contextStack.Count > 0 ? _contextStack.Peek().NestingLevel + 1 : 0;
            var context = new ParserContext(type, newLevel, _position, name);
            _contextStack.Push(context);

            Console.WriteLine($"[CONTEXT] Pushed {context}");
        }

        /// <summary>
        /// Pop the current context from the context stack
        /// </summary>
        private ParserContext? PopContext()
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
        private bool IsInContext(ContextType type)
        {
            return _contextStack.Any(ctx => ctx.Type == type);
        }

        /// <summary>
        /// Get the current top context
        /// </summary>
        private ParserContext? GetCurrentContext()
        {
            return _contextStack.Count > 0 ? _contextStack.Peek() : null;
        }

        /// <summary>
        /// Validate if a statement is allowed in the current context
        /// </summary>
        private void ValidateStatementContext(string statementType)
        {
            switch (statementType)
            {
                case "return":
                    if (!IsInContext(ContextType.Function) && !IsInContext(ContextType.Lambda))
                    {
                        throw new InvalidOperationException("SyntaxError: 'return' outside function");
                    }
                    break;

                case "yield":
                    if (!IsInContext(ContextType.Function))
                    {
                        throw new InvalidOperationException("SyntaxError: 'yield' outside function");
                    }
                    break;

                case "break":
                case "continue":
                    if (!IsInContext(ContextType.Loop))
                    {
                        var msg = statementType == "break" ? "'break' outside loop" : "'continue' not properly in loop";
                        throw new InvalidOperationException($"SyntaxError: {msg}");
                    }
                    break;

                case "await":
                    if (!IsInContext(ContextType.Async))
                    {
                        throw new InvalidOperationException("SyntaxError: 'await' outside async function");
                    }
                    break;
            }
        }

        /// <summary>
        /// Debug method to print current context stack
        /// </summary>
        private void PrintContextStack()
        {
            Console.WriteLine($"[CONTEXT-STACK] Current stack ({_contextStack.Count} levels):");
            foreach (var ctx in _contextStack.Reverse())
            {
                Console.WriteLine($"[CONTEXT-STACK]   {ctx}");
            }
        }

    }
}