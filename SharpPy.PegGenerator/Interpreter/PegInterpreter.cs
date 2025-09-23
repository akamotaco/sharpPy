using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.PegGenerator.Grammar;
using SharpPy.Generated;

namespace SharpPy.PegGenerator.Interpreter
{
    /// <summary>
    /// Simple AST node types for temporary parsing results
    /// </summary>
    public class SimpleModule
    {
        public List<object>? Body { get; set; }
    }

    public class SimpleStmt
    {
        public string? Type { get; set; }
        public object? Data { get; set; }
    }

    public class SimpleExpr
    {
        public string? Type { get; set; }
        public object? Data { get; set; }
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
        private readonly Dictionary<(int, string), object?> _memoCache = new();
        private readonly Dictionary<string, object?> _variables = new();
        private readonly HashSet<(int, string)> _activeRules = new(); // Track active rules to prevent left recursion
        private readonly Dictionary<string, bool> _leftRecursiveRules = new(); // Cache for left-recursive rule detection
        private readonly Dictionary<string, object?> _seedResults = new(); // Store seed results for left-recursive expansion
        private readonly Dictionary<int, int> _positionAttempts = new(); // Track attempts at each position for infinite loop detection

        public PegInterpreter(Grammar.Grammar grammar, List<ITokenInfo> tokens)
        {
            _grammar = grammar ?? throw new ArgumentNullException(nameof(grammar));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _position = 0;
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
            // Infinite loop detection
            if (_positionAttempts.ContainsKey(_position))
            {
                _positionAttempts[_position]++;
                if (_positionAttempts[_position] > 50) // Even lower threshold for faster debugging
                {
                    throw new InvalidOperationException($"Infinite loop detected at token position {_position}. Current token: {CurrentToken?.Type}('{CurrentToken?.Value}')");
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
        /// Check if a rule is left-recursive
        /// </summary>
        private bool IsLeftRecursive(Rule rule)
        {
            if (_leftRecursiveRules.TryGetValue(rule.Name, out var cached))
            {
                return cached;
            }

            // A rule is left-recursive if any alternative starts with the rule itself
            var isLeftRec = rule.Alternatives.Any(alt =>
                alt.Items.Count > 0 &&
                alt.Items[0].Atom is RuleRef ruleRef &&
                ruleRef.Name == rule.Name);

            _leftRecursiveRules[rule.Name] = isLeftRec;
            return isLeftRec;
        }

        /// <summary>
        /// Main entry point: Parse a rule by name
        /// </summary>
        public object? ParseRule(string ruleName)
        {
            // Check memoization cache
            var cacheKey = (_position, ruleName);
            if (_memoCache.TryGetValue(cacheKey, out var cachedResult))
            {
                // Console.WriteLine($"[MEMO] Cache hit for {ruleName} at position {_position}");
                return cachedResult;
            }

            // Find the rule in grammar
            var rule = _grammar.Rules.FirstOrDefault(r => r.Name == ruleName);
            if (rule == null)
            {
                // Console.WriteLine($"[DEBUG] Rule not found: {ruleName}");
                return null;
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
        private object? ParseNormalRule(string ruleName, Rule rule)
        {
            var cacheKey = (_position, ruleName);

            // Prevent infinite recursion by detecting active rules
            if (_activeRules.Contains(cacheKey))
            {
                // Console.WriteLine($"[RECURSION] Infinite recursion detected for {ruleName} at position {_position}");
                return null;
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

                    if (result != null)
                    {
                        // Success - cache and return
                        _memoCache[cacheKey] = result;
                        // Console.WriteLine($"[DEBUG] Rule {ruleName} succeeded at position {_position}");
                        return result;
                    }

                    // Failed - backtrack
                    Reset(mark);
                }

                // No alternative succeeded
                // Console.WriteLine($"[DEBUG] Rule {ruleName} failed at position {_position}");
                _memoCache[cacheKey] = null;
                return null;
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
        private object? ParseLeftRecursiveRule(string ruleName, Rule rule)
        {
            var cacheKey = (_position, ruleName);

            // Console.WriteLine($"[LEFT-REC] Parsing left-recursive rule: {ruleName} at position {_position}");

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
                return null;
            }

            // Step 3: Parse base alternatives to get initial seed
            object? seed = null;
            int seedPosition = _position;

            foreach (var baseAlt in baseAlternatives)
            {
                var mark = Mark();
                var result = ParseAlternative(baseAlt);

                if (result != null)
                {
                    seed = result;
                    seedPosition = _position;
                    // Console.WriteLine($"[LEFT-REC] Found seed for {ruleName}: {seed}");
                    break;
                }

                Reset(mark);
            }

            if (seed == null)
            {
                // No base case matched
                _memoCache[cacheKey] = null;
                return null;
            }

            // Step 4: Iteratively expand the seed using recursive alternatives
            while (true)
            {
                var expandedSeed = seed;
                var expandedPosition = seedPosition;
                var foundExpansion = false;

                foreach (var recursiveAlt in recursiveAlternatives)
                {
                    // Reset to seed position for each recursive alternative
                    Reset(seedPosition);

                    // Temporarily set the seed result for this rule
                    var tempCacheKey = (_position, ruleName);
                    _memoCache[tempCacheKey] = seed;

                    try
                    {
                        var result = ParseAlternative(recursiveAlt);
                        if (result != null && _position > seedPosition)
                        {
                            // Found a longer parse - update seed
                            expandedSeed = result;
                            expandedPosition = _position;
                            foundExpansion = true;
                            // Console.WriteLine($"[LEFT-REC] Expanded seed for {ruleName}: {result}");
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
                    // No more expansions possible
                    break;
                }

                // Update seed for next iteration
                seed = expandedSeed;
                seedPosition = expandedPosition;
            }

            // Step 5: Set final position and cache result
            _position = seedPosition;
            _memoCache[cacheKey] = seed;

            // Console.WriteLine($"[LEFT-REC] Final result for {ruleName}: {seed}");
            return seed;
        }

        /// <summary>
        /// Parse a single alternative (sequence of items)
        /// </summary>
        private object? ParseAlternative(Alternative alternative)
        {
            var results = new List<object?>();
            var variables = new Dictionary<string, object?>();

            // Console.WriteLine($"[DEBUG] Parsing alternative with {alternative.Items.Count} items");

            // Parse each item in sequence
            foreach (var item in alternative.Items)
            {
                var result = ParseItem(item);
                if (result == null)
                {
                    // Item failed - alternative fails
                    // Console.WriteLine($"[DEBUG] Item failed in alternative");
                    return null;
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
        private object? ParseItem(Item item)
        {
            // Console.WriteLine($"[DEBUG] Parsing item: {item.Name}={item.Atom}");
            return ParseAtom(item.Atom);
        }

        /// <summary>
        /// Parse an atomic expression based on its type
        /// </summary>
        private object? ParseAtom(Atom atom)
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
                    return null;
            }
        }

        /// <summary>
        /// Parse string literal (keywords and operators)
        /// </summary>
        private object? ParseStringLiteral(StringLiteral stringLiteral)
        {
            var expected = stringLiteral.Value;
            var current = CurrentToken;

            Console.WriteLine($"[DEBUG] Expecting string literal: '{expected}', current token: {current?.Type}('{current?.Value}')");

            if (current == null) return null;

            // Handle keywords (they come as NAME tokens)
            if (IsKeyword(expected))
            {
                if (current.Type.ToString() == "NAME" && current.Value == expected)
                {
                    Advance();
                    return expected;
                }
            }
            // Handle operators and punctuation (CPython 3.12: all operators are OP tokens)
            else
            {
                // Check if token is OP type with matching value
                if (current.Type.ToString() == "OP" && current.Value == expected)
                {
                    Advance();
                    return expected;
                }
            }

            return null;
        }

        /// <summary>
        /// Parse optional expression [expr]
        /// </summary>
        private object? ParseOptional(Optional optional)
        {
            // Console.WriteLine($"[DEBUG] Parsing optional: [{optional.Expression}]");

            var mark = Mark();
            var result = ParseAtom(optional.Expression);

            if (result == null)
            {
                // Optional failed - reset and return empty success
                Reset(mark);
                // Console.WriteLine($"[DEBUG] Optional failed, continuing");
                return new object(); // Return non-null to indicate success
            }

            // Console.WriteLine($"[DEBUG] Optional succeeded");
            return result;
        }

        /// <summary>
        /// Parse group (alternatives in parentheses)
        /// </summary>
        private object? ParseGroup(Group group)
        {
            Console.WriteLine($"[DEBUG] Parsing group with {group.Alternatives.Count} alternatives");

            // Try each alternative in the group
            foreach (var alternative in group.Alternatives)
            {
                var mark = Mark();
                var result = ParseAlternative(alternative);

                if (result != null)
                {
                    return result;
                }

                Reset(mark);
            }

            return null;
        }

        /// <summary>
        /// Parse zero or more repetitions expr*
        /// </summary>
        private object? ParseZeroOrMore(ZeroOrMore zeroOrMore)
        {
            Console.WriteLine($"[DEBUG] Parsing zero or more: {zeroOrMore.Expression}*");

            var results = new List<object?>();

            while (true)
            {
                var mark = Mark();
                var result = ParseAtom(zeroOrMore.Expression);

                if (result == null)
                {
                    Reset(mark);
                    break;
                }

                results.Add(result);
            }

            Console.WriteLine($"[DEBUG] Zero or more matched {results.Count} items");
            return results;
        }

        /// <summary>
        /// Parse one or more repetitions expr+
        /// </summary>
        private object? ParseOneOrMore(OneOrMore oneOrMore)
        {
            Console.WriteLine($"[DEBUG] Parsing one or more: {oneOrMore.Expression}+");

            var results = new List<object?>();

            // Must match at least once
            var firstResult = ParseAtom(oneOrMore.Expression);
            if (firstResult == null)
            {
                return null;
            }

            results.Add(firstResult);

            // Then zero or more additional matches
            while (true)
            {
                var mark = Mark();
                var result = ParseAtom(oneOrMore.Expression);

                if (result == null)
                {
                    Reset(mark);
                    break;
                }

                results.Add(result);
            }

            Console.WriteLine($"[DEBUG] One or more matched {results.Count} items");
            return results;
        }

        /// <summary>
        /// Parse positive lookahead &expr
        /// </summary>
        private object? ParsePositiveLookahead(PositiveLookahead positiveLookahead)
        {
            Console.WriteLine($"[DEBUG] Parsing positive lookahead: &{positiveLookahead.Expression}");

            var mark = Mark();
            var result = ParseAtom(positiveLookahead.Expression);

            // Always reset position (lookahead doesn't consume)
            Reset(mark);

            // Return success/failure based on whether expression matched
            return result != null ? new object() : null;
        }

        /// <summary>
        /// Parse negative lookahead !expr
        /// </summary>
        private object? ParseNegativeLookahead(NegativeLookahead negativeLookahead)
        {
            Console.WriteLine($"[DEBUG] Parsing negative lookahead: !{negativeLookahead.Expression}");

            var mark = Mark();
            var result = ParseAtom(negativeLookahead.Expression);

            // Always reset position (lookahead doesn't consume)
            Reset(mark);

            // Return success if expression did NOT match
            return result == null ? new object() : null;
        }

        /// <summary>
        /// Parse cut operator ~expr (not implemented yet)
        /// </summary>
        private object? ParseCut(Cut cut)
        {
            Console.WriteLine($"[DEBUG] Cut operator not implemented yet");
            return ParseAtom(cut.Expression);
        }

        /// <summary>
        /// Execute action with parsed results and variable bindings
        /// </summary>
        private object? ExecuteAction(string? action, Dictionary<string, object?> variables, List<object?> results)
        {
            if (string.IsNullOrEmpty(action))
            {
                // No action - return first result or success marker
                return results.FirstOrDefault() ?? new object();
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
        private object? ParseAction(string action, Dictionary<string, object?> variables, List<object?> results)
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
                return results.FirstOrDefault();
            }
            else if (action.Contains("_PyAST_Assign"))
            {
                // Assignment: a[asdl_expr_seq*]=(z=star_targets '=' { z })+ b=(yield_expr | star_expressions)
                return new SimpleStmt
                {
                    Type = "assignment",
                    Data = new {
                        Targets = variables.ContainsKey("a") ? variables["a"] : null,
                        Value = variables.ContainsKey("b") ? variables["b"] : null
                    }
                };
            }
            else if (action.Contains("_PyAST_Module"))
            {
                // Module: statements+
                return new SimpleModule
                {
                    Body = variables.ContainsKey("a") ? variables["a"] as List<object> : new List<object>()
                };
            }
            else if (action.Contains("_PyAST_Name"))
            {
                // Name expression: NAME
                return new SimpleExpr
                {
                    Type = "name",
                    Data = variables.ContainsKey("id") ? variables["id"] : null
                };
            }
            else if (action.Contains("_PyAST_Constant") || action.Contains("_PyAST_Num"))
            {
                // Constant/Number expression
                return new SimpleExpr
                {
                    Type = "constant",
                    Data = variables.ContainsKey("value") ? variables["value"] : null
                };
            }
            else if (action.Contains("_PyAST_BinOp"))
            {
                // Binary operation: left op right
                return new SimpleExpr
                {
                    Type = "binop",
                    Data = new {
                        Left = variables.ContainsKey("left") ? variables["left"] : null,
                        Op = variables.ContainsKey("op") ? variables["op"] : null,
                        Right = variables.ContainsKey("right") ? variables["right"] : null
                    }
                };
            }

            // Default: return generic success marker for now
            Console.WriteLine($"[DEBUG] Unhandled action pattern: {action}");
            return new { Action = action, Variables = variables, Results = results };
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
        /// </summary>
        private object? ParseTokenType(string tokenType)
        {
            var current = CurrentToken;
            if (current == null) return null;

            Console.WriteLine($"[DEBUG] Expecting token type: {tokenType}, current token: {current.Type}('{current.Value}')");

            if (current.Type.ToString() == tokenType)
            {
                var value = current.Value;
                Advance();
                return value; // Return the token value
            }

            return null;
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
    }
}