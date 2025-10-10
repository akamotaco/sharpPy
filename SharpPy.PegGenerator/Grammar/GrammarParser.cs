using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy.PegGenerator.Grammar
{
    /// <summary>
    /// Parser for PEG grammar files
    /// </summary>
    public class GrammarParser
    {
        private readonly List<GrammarToken> _tokens;
        private int _position;

        public GrammarParser(List<GrammarToken> tokens)
        {
            _tokens = tokens.Where(t => t.Type != GrammarTokenType.COMMENT).ToList();
            _position = 0;
        }

        /// <summary>
        /// Parse the grammar file
        /// </summary>
        public Grammar ParseGrammar()
        {
            var grammar = new Grammar();
            // Starting grammar parsing

            // Skip leading newlines
            SkipNewlines();

            // Parse trailer if present
            if (CurrentToken?.Type == GrammarTokenType.NAME && CurrentToken.Value == "@")
            {
                var nextToken = PeekToken();
                if (nextToken?.Type == GrammarTokenType.NAME && nextToken.Value == "trailer")
                {
                    grammar.Trailer = ParseTrailer();
                    SkipNewlines();
                }
                else
                {
                    // Skip unknown directive
                    Advance(); // Skip @
                    if (CurrentToken?.Type == GrammarTokenType.NAME)
                        Advance(); // Skip directive name
                    SkipNewlines();
                }
            }

            // Parse rules
            int ruleCount = 0;
            while (!IsAtEnd() && CurrentToken?.Type != GrammarTokenType.ENDMARKER)
            {
                var oldPosition = _position;

                if (CurrentToken?.Type == GrammarTokenType.NEWLINE)
                {
                    Advance();
                    continue;
                }

                if (CurrentToken?.Type == GrammarTokenType.DEDENT)
                {
                    Advance();
                    continue;
                }

                var rule = ParseRule();
                if (rule != null)
                {
                    Console.WriteLine($"[DEBUG] Parsed rule: {rule.Name}");
                    grammar.Rules.Add(rule);
                    ruleCount++;
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Failed to parse rule at position {_position}, token: {CurrentToken?.Type}({CurrentToken?.Value})");
                    // Show some context around this position
                    for (int i = Math.Max(0, _position - 2); i < Math.Min(_tokens.Count, _position + 3); i++)
                    {
                        var marker = i == _position ? " >> " : "    ";
                        Console.WriteLine($"[DEBUG]{marker}Position {i}: {_tokens[i].Type}({_tokens[i].Value})");
                    }
                }

                // Safety check for infinite loop
                if (_position == oldPosition)
                {
                    Console.WriteLine($"[DEBUG] No progress made at position {_position}, trying to skip to next rule");
                    // Try to find the next rule start
                    bool foundNextRule = false;
                    for (int i = _position + 1; i < Math.Min(_tokens.Count, _position + 50); i++)
                    {
                        if (IsRuleStartAtPosition(i))
                        {
                            Console.WriteLine($"[DEBUG] Found next rule at position {i}: {_tokens[i].Type}({_tokens[i].Value})");
                            _position = i;
                            foundNextRule = true;
                            break;
                        }
                    }

                    if (!foundNextRule)
                    {
                        Console.WriteLine($"[DEBUG] No next rule found, breaking");
                        break;
                    }
                }

                SkipNewlines();

                // Safety limit
                if (ruleCount > 250)
                {
                    Console.WriteLine($"[DEBUG] Reached safety limit of 250 rules, stopping parsing");
                    break;
                }
            }

            return grammar;
        }

        /// <summary>
        /// Parse a @trailer block
        /// </summary>
        private string ParseTrailer()
        {
            if (CurrentToken?.Value != "@")
                return "";

            Advance(); // Skip @

            if (CurrentToken?.Value != "trailer")
                return "";

            Advance(); // Skip trailer
            SkipNewlines();

            var trailer = "";

            // Look for triple quotes or any string
            if (CurrentToken?.Type == GrammarTokenType.STRING)
            {
                trailer = CurrentToken.Value.Trim('\'', '"');
                Advance();
            }

            return trailer;
        }

        /// <summary>
        /// Parse a single rule
        /// </summary>
        private Rule? ParseRule()
        {
            if (CurrentToken?.Type != GrammarTokenType.NAME)
                return null;

            var rule = new Rule
            {
                Name = CurrentToken.Value
            };
            Advance();

            // Parse optional return type [type] - handle complex types with * and other characters
            if (CurrentToken?.Type == GrammarTokenType.LSQB)
            {
                Advance(); // Skip [
                var typeAnnotation = "";

                // Read everything until the closing ]
                while (CurrentToken?.Type != GrammarTokenType.RSQB && CurrentToken != null)
                {
                    typeAnnotation += CurrentToken.Value;
                    Advance();
                }

                rule.ReturnType = typeAnnotation;

                if (CurrentToken?.Type == GrammarTokenType.RSQB)
                {
                    Advance(); // Skip ]
                }
            }

            // Parse optional memo directive (memo) or (inline)
            if (CurrentToken?.Type == GrammarTokenType.LPAR)
            {
                Advance(); // Skip (

                // Read the directive content (memo, inline, etc.)
                var directive = "";
                while (CurrentToken?.Type != GrammarTokenType.RPAR && CurrentToken != null)
                {
                    directive += CurrentToken.Value;
                    Advance();
                }

                if (directive == "memo")
                {
                    rule.IsMemoized = true;
                }

                if (CurrentToken?.Type == GrammarTokenType.RPAR)
                {
                    Advance(); // Skip )
                }
            }

            // Expect colon
            if (CurrentToken?.Type != GrammarTokenType.COLON)
            {
                throw new ParseException($"Expected ':' after rule name, got {CurrentToken?.Type}");
            }
            Advance();

            SkipNewlines();

            // Parse alternatives
            do
            {
                // Handle indented alternatives
                if (CurrentToken?.Type == GrammarTokenType.INDENT)
                {
                    Advance(); // Skip INDENT
                }

                // Skip optional leading pipe
                if (CurrentToken?.Type == GrammarTokenType.PIPE)
                {
                    Advance();
                }

                var alternative = ParseAlternative();
                if (alternative != null)
                {
                    rule.Alternatives.Add(alternative);
                }

                // Handle dedent
                if (CurrentToken?.Type == GrammarTokenType.DEDENT)
                {
                    Advance(); // Skip DEDENT
                    break; // End of indented block
                }

                // Only skip newlines if we're not continuing with more alternatives on the same line
                if (CurrentToken?.Type != GrammarTokenType.PIPE)
                {
                    SkipNewlines();
                }
            }
            while (CurrentToken?.Type == GrammarTokenType.PIPE);

            return rule;
        }

        /// <summary>
        /// Parse an alternative (sequence of items)
        /// </summary>
        private Alternative? ParseAlternative()
        {
            var alternative = new Alternative();
            var startPosition = _position;

            // Parse items until we hit a pipe, newline, or end
            while (CurrentToken != null &&
                   CurrentToken.Type != GrammarTokenType.PIPE &&
                   CurrentToken.Type != GrammarTokenType.NEWLINE &&
                   CurrentToken.Type != GrammarTokenType.ENDMARKER &&
                   !IsRuleStart())
            {
                var oldPosition = _position;
                if (CurrentToken.Type == GrammarTokenType.TILDE)
                {
                    Console.WriteLine($"[ParseAlternative] About to parse cut at position {_position}, next: {PeekToken()?.Value}");
                }
                var item = ParseItem();

                if (item != null)
                {
                    alternative.Items.Add(item);
                }
                else
                {
                    // If no progress was made, break to prevent infinite loop
                    if (_position == oldPosition)
                    {
                        break;
                    }
                }
            }

            // Parse optional action
            if (CurrentToken?.Type == GrammarTokenType.ACTION)
            {
                alternative.Action = CurrentToken.Value.Trim('{', '}').Trim();
                Advance();
            }

            return alternative.Items.Count > 0 || !string.IsNullOrEmpty(alternative.Action) ? alternative : null;
        }

        /// <summary>
        /// Parse an item (named or unnamed atom)
        /// </summary>
        private Item? ParseItem()
        {
            if (CurrentToken?.Type == GrammarTokenType.ACTION)
                return null; // Action will be handled by alternative

            var item = new Item();

            // Handle cut operator before named assignment: ~ name=atom
            bool hasCut = false;
            if (CurrentToken?.Type == GrammarTokenType.TILDE)
            {
                hasCut = true;
                Console.WriteLine($"[GrammarParser] Found cut operator, next token: {PeekToken()?.Type} '{PeekToken()?.Value}'");
                Advance(); // Skip ~
            }

            // Check for named item (name=atom or name[type]=atom)
            if (CurrentToken?.Type == GrammarTokenType.NAME)
            {
                var nextToken = PeekToken();
                if (nextToken?.Type == GrammarTokenType.EQUAL)
                {
                    // Simple named item: name=atom
                    item.Name = CurrentToken.Value;
                    Console.WriteLine($"[GrammarParser] Found named item: '{item.Name}' (after cut: {hasCut})");
                    Advance(); // Skip name
                    Advance(); // Skip =
                }
                else if (nextToken?.Type == GrammarTokenType.LSQB)
                {
                    // Named item with type annotation: name[type]=atom
                    item.Name = CurrentToken.Value;
                    Advance(); // Skip name

                    // Parse the type annotation [type]
                    Advance(); // Skip [
                    var typeAnnotationTokens = new List<string>();
                    while (CurrentToken?.Type != GrammarTokenType.RSQB && CurrentToken != null)
                    {
                        typeAnnotationTokens.Add(CurrentToken.Value ?? "");
                        Advance();
                    }
                    item.TypeAnnotation = string.Join("", typeAnnotationTokens);
                    Console.WriteLine($"[GrammarParser] Parsed type annotation: '{item.TypeAnnotation}' for '{item.Name}'");

                    if (CurrentToken?.Type == GrammarTokenType.RSQB)
                    {
                        Advance(); // Skip ]
                    }

                    // Now expect =
                    if (CurrentToken?.Type == GrammarTokenType.EQUAL)
                    {
                        Advance(); // Skip =
                    }
                }
            }

            // Parse the atom
            item.Atom = ParseAtom();

            // If we had a cut operator, wrap the atom in a Cut node
            if (hasCut && item.Atom != null)
            {
                Console.WriteLine($"[GrammarParser] Wrapping atom in Cut node, item.Name={item.Name}");
                item.Atom = new Cut { Expression = item.Atom };
            }

            if (item.Atom != null && !string.IsNullOrEmpty(item.Name))
            {
                Console.WriteLine($"[GrammarParser] Created item with Name='{item.Name}', Atom={item.Atom.GetType().Name}");
            }

            return item.Atom != null ? item : null;
        }

        /// <summary>
        /// Parse an atomic expression
        /// </summary>
        private Atom? ParseAtom()
        {
            if (CurrentToken == null)
                return null;

            Atom atom;

            switch (CurrentToken.Type)
            {
                case GrammarTokenType.NAME:
                    atom = new RuleRef { Name = CurrentToken.Value };
                    Advance();
                    break;

                case GrammarTokenType.STRING:
                    // CPython 3.12: Preserve quote type for soft keyword detection
                    // ' (single quote) = hard keyword, " (double quote) = soft keyword
                    var rawValue = CurrentToken.Value;
                    char quoteChar = rawValue[0]; // First char is the quote
                    var trimmedValue = rawValue.Trim('\'', '"');
                    atom = new StringLiteral { Value = trimmedValue, QuoteChar = quoteChar };
                    Advance();
                    break;

                case GrammarTokenType.LPAR:
                    atom = ParseGroup();
                    break;

                case GrammarTokenType.LSQB:
                    atom = ParseOptional();
                    break;

                case GrammarTokenType.AMPERSAND:
                    Advance();
                    // CPython 3.12: Check for && (forced token) vs & (positive lookahead)
                    if (CurrentToken?.Type == GrammarTokenType.AMPERSAND)
                    {
                        // && = Forced token
                        Advance();
                        var forcedAtom = ParseAtom();
                        atom = forcedAtom != null ? new Forced { Node = forcedAtom } : null;
                    }
                    else
                    {
                        // & = Positive lookahead
                        var posAtom = ParseAtom();
                        atom = posAtom != null ? new PositiveLookahead { Expression = posAtom } : null;
                    }
                    break;

                case GrammarTokenType.EXCLAMATION:
                    Advance();
                    var negAtom = ParseAtom();
                    atom = negAtom != null ? new NegativeLookahead { Expression = negAtom } : null;
                    break;

                case GrammarTokenType.TILDE:
                    Advance();
                    var cutAtom = ParseAtom();
                    atom = cutAtom != null ? new Cut { Expression = cutAtom } : null;
                    break;

                default:
                    return null;
            }

            // Check for gather pattern (separator.item+ or separator.item*)
            // CPython 3.12: Gather must be checked before postfix operators
            if (CurrentToken?.Type == GrammarTokenType.DOT)
            {
                Advance(); // Skip '.'

                // Parse the item part
                var itemAtom = ParseAtom();
                if (itemAtom == null)
                {
                    throw new ParseException($"Expected item after '.' in gather pattern at position {_position}");
                }

                // The itemAtom should already have its postfix operator (+ or *)
                // Determine if it's oneOrMore or zeroOrMore
                bool isOneOrMore = itemAtom is OneOrMore;
                bool isZeroOrMore = itemAtom is ZeroOrMore;

                if (!isOneOrMore && !isZeroOrMore)
                {
                    throw new ParseException($"Gather pattern requires + or * after item at position {_position}");
                }

                // Extract the inner expression from OneOrMore/ZeroOrMore
                var innerItem = isOneOrMore
                    ? ((OneOrMore)itemAtom).Expression
                    : ((ZeroOrMore)itemAtom).Expression;

                return new Gather
                {
                    Separator = atom,  // The part before '.' is the separator
                    Item = innerItem,   // The inner expression
                    IsOneOrMore = isOneOrMore
                };
            }

            // Handle postfix operators (*, +, ?)
            while (CurrentToken != null)
            {
                switch (CurrentToken.Type)
                {
                    case GrammarTokenType.STAR:
                        atom = new ZeroOrMore { Expression = atom };
                        Advance();
                        break;

                    case GrammarTokenType.PLUS:
                        atom = new OneOrMore { Expression = atom };
                        Advance();
                        break;

                    case GrammarTokenType.QUESTION:
                        atom = new Optional { Expression = atom };
                        Advance();
                        break;

                    default:
                        return atom;
                }
            }

            return atom;
        }

        /// <summary>
        /// Parse a group (parentheses)
        /// </summary>
        private Group ParseGroup()
        {
            if (CurrentToken?.Type != GrammarTokenType.LPAR)
                throw new ParseException("Expected '('");

            Advance(); // Skip (

            var group = new Group();

            // Parse alternatives within the group
            do
            {
                // Skip newlines within groups
                SkipNewlines();

                if (CurrentToken?.Type == GrammarTokenType.PIPE)
                {
                    Advance();
                    SkipNewlines(); // Skip newlines after pipe
                }

                var alternative = ParseAlternative();
                if (alternative != null)
                {
                    group.Alternatives.Add(alternative);
                }

                SkipNewlines(); // Skip newlines after alternative
            }
            while (CurrentToken?.Type == GrammarTokenType.PIPE);

            SkipNewlines(); // Skip any final newlines before closing paren

            if (CurrentToken?.Type != GrammarTokenType.RPAR)
                throw new ParseException($"Expected ')' but found {CurrentToken?.Type}({CurrentToken?.Value}) at position {_position}");

            Advance(); // Skip )

            return group;
        }

        /// <summary>
        /// Parse an optional expression [expr]
        /// </summary>
        private Optional ParseOptional()
        {
            if (CurrentToken?.Type != GrammarTokenType.LSQB)
                throw new ParseException("Expected '['");

            Advance(); // Skip [

            // Parse a full alternative (sequence of items) inside the optional
            var alternative = ParseAlternative();
            if (alternative == null)
            {
                // Allow empty optional expressions
                Console.WriteLine($"[DEBUG] ParseOptional: Empty optional expression at position {_position}");
                alternative = new Alternative(); // Create empty alternative
            }

            if (CurrentToken?.Type != GrammarTokenType.RSQB)
            {
                Console.WriteLine($"[DEBUG] ParseOptional: Expected ']' but found {CurrentToken?.Type}({CurrentToken?.Value}) at position {_position}");
                // Try to recover by looking for the next ']'
                var startPos = _position;
                while (_position < _tokens.Count && CurrentToken?.Type != GrammarTokenType.RSQB && _position < startPos + 20)
                {
                    Advance();
                }
                if (CurrentToken?.Type != GrammarTokenType.RSQB)
                {
                    throw new ParseException($"Expected ']' but found {CurrentToken?.Type}({CurrentToken?.Value})");
                }
            }

            Advance(); // Skip ]

            // Create a group with the alternative as the optional expression
            var group = new Group();
            group.Alternatives.Add(alternative);
            return new Optional { Expression = group };
        }

        // Helper methods
        private void SkipNewlines()
        {
            while (CurrentToken?.Type == GrammarTokenType.NEWLINE ||
                   CurrentToken?.Type == GrammarTokenType.INDENT ||
                   CurrentToken?.Type == GrammarTokenType.DEDENT)
            {
                Advance();
            }
        }

        private bool IsRuleStartAtPosition(int position)
        {
            if (position >= _tokens.Count || _tokens[position].Type != GrammarTokenType.NAME)
                return false;

            var nextPos = position + 1;
            if (nextPos >= _tokens.Count)
                return false;

            var nextToken = _tokens[nextPos];
            if (nextToken.Type == GrammarTokenType.COLON)
                return true;

            // Check for rule with type annotation: NAME[type]:
            if (nextToken.Type == GrammarTokenType.LSQB)
            {
                // Look ahead to see if we eventually get to a colon
                var pos = nextPos + 1;
                int bracketDepth = 1;
                while (pos < _tokens.Count && bracketDepth > 0)
                {
                    var token = _tokens[pos];
                    if (token.Type == GrammarTokenType.LSQB)
                        bracketDepth++;
                    else if (token.Type == GrammarTokenType.RSQB)
                        bracketDepth--;
                    pos++;
                }

                // After the closing ], look for optional (memo) or other directives before :
                if (pos < _tokens.Count)
                {
                    var tokenAfterBracket = _tokens[pos];

                    // Check if there's a (memo) or similar directive
                    if (tokenAfterBracket.Type == GrammarTokenType.LPAR)
                    {
                        // Skip through the parentheses (memo), (inline), etc.
                        int parenDepth = 1;
                        pos++;
                        while (pos < _tokens.Count && parenDepth > 0)
                        {
                            var token = _tokens[pos];
                            if (token.Type == GrammarTokenType.LPAR)
                                parenDepth++;
                            else if (token.Type == GrammarTokenType.RPAR)
                                parenDepth--;
                            pos++;
                        }

                        // Now check for colon after the directive
                        if (pos < _tokens.Count)
                        {
                            return _tokens[pos].Type == GrammarTokenType.COLON;
                        }
                    }
                    else
                    {
                        // No directive, check directly for colon
                        return tokenAfterBracket.Type == GrammarTokenType.COLON;
                    }
                }
            }

            return false;
        }

        private bool IsRuleStart()
        {
            return IsRuleStartAtPosition(_position);
        }

        private GrammarToken? CurrentToken => _position < _tokens.Count ? _tokens[_position] : null;
        private GrammarToken? PeekToken(int offset = 1) => _position + offset < _tokens.Count ? _tokens[_position + offset] : null;
        private bool IsAtEnd() => _position >= _tokens.Count;

        private void Advance()
        {
            if (_position < _tokens.Count)
                _position++;
        }
    }

    /// <summary>
    /// Exception thrown during grammar parsing
    /// </summary>
    public class ParseException : Exception
    {
        public ParseException(string message) : base(message) { }
        public ParseException(string message, Exception innerException) : base(message, innerException) { }
    }
}