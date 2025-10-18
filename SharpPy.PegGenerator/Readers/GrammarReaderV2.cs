using SharpPy.PegGenerator.DataStructures;
using static SharpPy.PegGenerator.Readers.GrammarTokenizer;
using PegGroup = SharpPy.PegGenerator.DataStructures.Group;

namespace SharpPy.PegGenerator.Readers;

/// <summary>
/// 토큰 기반 Grammar 파서 (CPython pegen 방식)
/// </summary>
public class GrammarReaderV2
{
    private List<GrammarToken> _tokens = new();
    private int _position = 0;

    /// <summary>
    /// @trailer code block (if present in grammar)
    /// </summary>
    public TrailerCode? Trailer { get; private set; }

    public PegRule[] ReadGrammar(string filePath)
    {
        Console.WriteLine($"Reading grammar from: {filePath}");
        var source = File.ReadAllText(filePath);

        // Step 1: Tokenize
        var tokenizer = new GrammarTokenizer();
        _tokens = tokenizer.Tokenize(source);
        _position = 0;

        Console.WriteLine($"Tokenized {_tokens.Count} tokens");

        // Step 1.5: Check for @trailer directive at start of file
        ParseTrailer();

        // Step 2: Parse rules
        var rules = new List<PegRule>();

        while (_position < _tokens.Count)
        {
            var token = Current();

            // Skip newlines at top level
            if (token?.Type == TokenType.NEWLINE)
            {
                Advance();
                continue;
            }

            // End of file
            if (token?.Type == TokenType.ENDMARKER)
                break;

            // Parse rule
            Console.WriteLine($"[DEBUG-MAIN] Attempting to parse next rule at position {_position}, token: {token?.Type} '{token?.Value}' at line {token?.Line}");
            var rule = ParseRule();
            if (rule != null)
            {
                rules.Add(rule);
                Console.WriteLine($"Parsed rule: {rule.Name} ({rule.Alternatives.Count} alternatives)");
            }
            else
            {
                Console.WriteLine($"[ERROR-MAIN] Failed to parse rule at position {_position}");
                Console.WriteLine($"[ERROR-MAIN] Stopping parser. Current token: {Current()?.Type} '{Current()?.Value}' at line {Current()?.Line}");
                // Print surrounding tokens for debugging
                Console.WriteLine($"[ERROR-MAIN] Token context (10 tokens before and after):");
                for (int i = Math.Max(0, _position - 10); i < Math.Min(_tokens.Count, _position + 10); i++)
                {
                    var t = _tokens[i];
                    string marker = i == _position ? " <<< HERE" : "";
                    Console.WriteLine($"  [{i}] {t.Type,-12} '{t.Value}' (line {t.Line}){marker}");
                }
                break;
            }
        }

        Console.WriteLine($"Parsed {rules.Count} grammar rules");
        return rules.ToArray();
    }

    private PegRule? ParseRule()
    {
        // rule: NAME [return_type] ['(' 'memo' ')'] ':' alts NEWLINE

        var nameToken = Expect(TokenType.NAME);
        if (nameToken == null)
        {
            var current = Current();
            if (current != null && current.Type != TokenType.ENDMARKER)
            {
                Console.WriteLine($"[ERROR] Expected NAME at line {current.Line}, got {current.Type}: '{current.Value}'");
            }
            return null;
        }

        Console.WriteLine($"[DEBUG] Parsing rule '{nameToken.Value}' at line {nameToken.Line}");
        Console.WriteLine($"[DEBUG] Next token: {Current()?.Type} '{Current()?.Value}'");

        // Optional [return_type]
        string? returnType = null;
        if (Current()?.Type == TokenType.OP && Current()?.Value == "[")
        {
            Console.WriteLine($"[DEBUG] Found return type annotation for rule '{nameToken.Value}'");
            Advance();  // Skip '['

            // Capture type annotation content
            var typeTokens = new List<string>();
            while (Current() != null && !(Current()!.Type == TokenType.OP && Current()!.Value == "]"))
            {
                typeTokens.Add(Current()!.Value);
                Advance();
            }

            // Join type tokens to form type string (e.g., "GeneratedMod", "GeneratedExpr?")
            returnType = string.Join("", typeTokens);

            if (Current()?.Type == TokenType.OP && Current()?.Value == "]")
            {
                Advance();  // Skip ']'
            }
            Console.WriteLine($"[DEBUG] Captured return type '{returnType}' for rule '{nameToken.Value}', next token: {Current()?.Type} '{Current()?.Value}'");
        }

        // Optional (memo) annotation
        bool isMemoized = false;
        if (Current()?.Type == TokenType.OP && Current()?.Value == "(")
        {
            int mark = _position;
            Advance();  // Skip '('

            // Check if this is (memo)
            if (Current()?.Type == TokenType.NAME && Current()?.Value == "memo")
            {
                Console.WriteLine($"[DEBUG] Found (memo) annotation for rule '{nameToken.Value}'");
                Advance();  // Skip 'memo'

                if (Current()?.Type == TokenType.OP && Current()?.Value == ")")
                {
                    Advance();  // Skip ')'
                    isMemoized = true;  // ← Store the flag!
                    Console.WriteLine($"[DEBUG] After (memo), next token: {Current()?.Type} '{Current()?.Value}'");
                }
                else
                {
                    Console.WriteLine($"[WARNING] Expected ')' after 'memo', got {Current()?.Type} '{Current()?.Value}' - restoring position");
                    _position = mark;  // Not a valid (memo), restore
                }
            }
            else
            {
                // Not (memo), restore position
                Console.WriteLine($"[DEBUG] '(' found but not (memo) - restoring position");
                _position = mark;
            }
        }

        var colonToken = ExpectOp(":");
        if (colonToken == null)
        {
            var current = Current();
            Console.WriteLine($"[ERROR] Expected ':' after rule name '{nameToken.Value}'");
            Console.WriteLine($"[ERROR] Current position: {_position}, Token: {current?.Type} '{current?.Value}' at line {current?.Line}");
            // Print surrounding tokens for context
            Console.WriteLine($"[ERROR] Context (5 tokens before and after):");
            for (int i = Math.Max(0, _position - 5); i < Math.Min(_tokens.Count, _position + 5); i++)
            {
                var t = _tokens[i];
                string marker = i == _position ? " <<< HERE" : "";
                Console.WriteLine($"  [{i}] {t.Type} '{t.Value}' (line {t.Line}){marker}");
            }
            return null;
        }

        // CPython metagrammar.gram:55 - Handle ":" NEWLINE INDENT more_alts pattern
        // Skip NEWLINEs after ':' to handle rules like:
        //   invalid_expression:
        //      # comment
        //      | alt1
        //      | alt2
        while (Current()?.Type == TokenType.NEWLINE)
        {
            Advance();
        }

        var alternatives = ParseAlternatives();

        // ParseAlternatives should have consumed all alternatives including final NEWLINE
        // Just verify we're at NEWLINE or skip it if present
        if (Current()?.Type == TokenType.NEWLINE)
        {
            Advance();
        }

        return new PegRule
        {
            Name = nameToken.Value,
            ReturnType = returnType,
            Alternatives = alternatives,
            IsMemoized = isMemoized  // CPython 3.12: Store (memo) marker
        };
    }

    private List<Alternative> ParseAlternatives()
    {
        // alts: alt ('|' alt)*
        // Handles multi-line alternatives (NEWLINE '|' alt)

        var alternatives = new List<Alternative>();

        // First alternative (may have leading '|')
        if (Current()?.Type == TokenType.OP && Current()?.Value == "|")
        {
            Advance();  // Skip optional leading '|'
        }

        var alt = ParseAlternative();
        if (alt != null)
        {
            alternatives.Add(alt);
        }

        // More alternatives (may be on next line)
        while (true)
        {
            // Skip optional NEWLINE before '|'
            if (Current()?.Type == TokenType.NEWLINE)
            {
                int mark = _position;
                Advance();  // Skip NEWLINE

                // Check if next token is '|'
                if (Current()?.Type == TokenType.OP && Current()?.Value == "|")
                {
                    Advance();  // Skip '|'
                    alt = ParseAlternative();
                    if (alt != null)
                    {
                        alternatives.Add(alt);
                    }
                    continue;
                }
                else
                {
                    // Not a continuation, restore NEWLINE
                    _position = mark;
                    break;
                }
            }

            // Check for same-line '|'
            if (Current()?.Type == TokenType.OP && Current()?.Value == "|")
            {
                Advance();  // Skip '|'
                alt = ParseAlternative();
                if (alt != null)
                {
                    alternatives.Add(alt);
                }
            }
            else
            {
                break;
            }
        }

        return alternatives;
    }

    private Alternative? ParseAlternative()
    {
        // alt: item+ [action]
        // action: '{' ... '}'

        var items = new List<Item>();

        while (true)
        {
            // Skip commit operator '~' between items
            if (Current()?.Type == TokenType.OP && Current()?.Value == "~")
            {
                Console.WriteLine($"[DEBUG-ALT] Found commit operator '~' between items at position {_position}, skipping");
                Advance();
                continue;  // Continue parsing next item
            }

            // Stop at '{', '|', NEWLINE, or ENDMARKER
            var token = Current();
            if (token == null ||
                token.Type == TokenType.NEWLINE ||
                token.Type == TokenType.ENDMARKER ||
                (token.Type == TokenType.OP && (token.Value == "|" || token.Value == "{")))
            {
                break;
            }

            var item = ParseItem();
            if (item == null)
                break;

            items.Add(item);
        }

        if (items.Count == 0)
            return null;

        // Parse optional action code: { C# code }
        string? actionCode = null;
        if (Current()?.Type == TokenType.OP && Current()?.Value == "{")
        {
            actionCode = ParseActionCode();
        }

        return new Alternative { Items = items, ActionCode = actionCode };
    }

    private string? ParseActionCode()
    {
        // Parse action code block: { ... }
        // Returns the code inside braces without the braces themselves
        // CPython pegen: target_atoms → target_atom + " " + target_atoms
        // All tokens are joined with a single space, except for consecutive OPs

        if (Current()?.Type != TokenType.OP || Current()?.Value != "{")
            return null;

        Advance(); // Skip '{'

        var result = new System.Text.StringBuilder();
        int braceDepth = 1;
        TokenType? prevType = null;

        while (braceDepth > 0 && Current() != null)
        {
            var token = Current();

            if (token.Type == TokenType.OP)
            {
                if (token.Value == "{")
                {
                    braceDepth++;
                }
                else if (token.Value == "}")
                {
                    braceDepth--;
                    if (braceDepth == 0)
                    {
                        Advance(); // Skip closing '}'
                        break;
                    }
                }
            }

            // Skip NEWLINE tokens to match CPython behavior (tokenizer filters NL/COMMENT)
            if (token.Type != TokenType.NEWLINE)
            {
                // Add space before token, except:
                // - At the start (result.Length == 0)
                // - Between consecutive OP tokens (to preserve //, ->, ::, etc.)
                if (result.Length > 0 && !(prevType == TokenType.OP && token.Type == TokenType.OP))
                {
                    result.Append(' ');
                }

                result.Append(token.Value);
                prevType = token.Type;
            }

            Advance();
        }

        return result.ToString();
    }

    private Item? ParseItem()
    {
        // item: [NAME ['[' type ']'] '='] atom
        // Examples:
        //   a=NAME                  (named item)
        //   a[GeneratedExpr]=expr   (named item with type annotation)
        //   NAME                    (unnamed item)

        // Check for named item (name[type]=atom or name=atom)
        if (Current()?.Type == TokenType.NAME)
        {
            int mark = _position;
            var nameToken = Advance();
            string? variableType = null;

            // Optional type annotation: [type]
            if (Current()?.Type == TokenType.OP && Current()?.Value == "[")
            {
                Console.WriteLine($"[DEBUG-ITEM] Found type annotation for variable '{nameToken.Value}'");
                Advance();  // Skip '['

                // Capture type annotation content
                var typeTokens = new List<string>();
                int bracketDepth = 1;
                while (Current() != null && bracketDepth > 0)
                {
                    if (Current()!.Type == TokenType.OP)
                    {
                        if (Current()!.Value == "[") bracketDepth++;
                        else if (Current()!.Value == "]") bracketDepth--;
                    }

                    if (bracketDepth > 0)
                    {
                        typeTokens.Add(Current()!.Value);
                        Advance();
                    }
                }

                // Join type tokens to form type string
                variableType = string.Join("", typeTokens);

                if (Current()?.Type == TokenType.OP && Current()?.Value == "]")
                {
                    Advance();  // Skip ']'
                    Console.WriteLine($"[DEBUG-ITEM] After type annotation '{variableType}', next token: {Current()?.Type} '{Current()?.Value}'");
                }
            }

            // Check for '='
            if (Current()?.Type == TokenType.OP && Current()?.Value == "=")
            {
                Console.WriteLine($"[DEBUG-ITEM] Found named item: {nameToken.Value}" + (variableType != null ? $" with type {variableType}" : ""));
                Console.WriteLine($"[DEBUG-ITEM] After '=', next token: {Current()?.Type} '{Current()?.Value}' at position {_position}");
                Advance();  // Skip '='
                Console.WriteLine($"[DEBUG-ITEM] Before ParseAtom, current token: {Current()?.Type} '{Current()?.Value}' at position {_position}");
                var atom = ParseAtom();
                if (atom != null)
                {
                    Console.WriteLine($"[DEBUG-ITEM] Successfully parsed atom for {nameToken.Value}, current position: {_position}");
                    return new Item { Name = nameToken!.Value, Type = variableType, Atom = atom };
                }
                else
                {
                    Console.WriteLine($"[DEBUG-ITEM] Failed to parse atom for {nameToken.Value}, current position: {_position}");
                }
            }

            // Not a named item, restore
            Console.WriteLine($"[DEBUG-ITEM] Not a named item, restoring position");
            _position = mark;
        }

        // Regular item
        var atomOnly = ParseAtom();
        if (atomOnly == null)
            return null;

        return new Item { Name = null, Atom = atomOnly };
    }

    private Atom? ParseAtom()
    {
        var token = Current();
        if (token == null)
            return null;

        // STRING: 'keyword' or "soft_keyword"
        if (token.Type == TokenType.STRING)
        {
            Advance();
            bool isSoft = token.Value.StartsWith("\"");
            string value = token.Value.Substring(1, token.Value.Length - 2);  // Remove quotes

            // Check for gather pattern: STRING '.' atom ('+' | '*')
            // Example: ';'.simple_stmt+ or ','.NAME+
            if (Current()?.Type == TokenType.OP && Current()?.Value == ".")
            {
                Advance();  // Skip '.'
                var item = ParseAtomWithoutPostfix();  // Don't apply postfix yet!
                if (item == null)
                    throw new Exception($"Expected atom after '.' in gather pattern");

                // Must be followed by '+' or '*'
                var postfix = Current();
                if (postfix?.Type == TokenType.OP && (postfix.Value == "+" || postfix.Value == "*"))
                {
                    bool isPlus = postfix.Value == "+";
                    Advance();

                    var separator = new Keyword { Value = value, IsSoft = isSoft };
                    return new Gather { Separator = separator, Item = item, IsPlus = isPlus };
                }

                throw new Exception($"Expected '+' or '*' after gather item at line {token.Line}");
            }

            var keyword = new Keyword { Value = value, IsSoft = isSoft };
            return ApplyPostfix(keyword);
        }

        // Group: '(' alts ')'
        if (token.Type == TokenType.OP && token.Value == "(")
        {
            Advance();  // Skip '('
            var alternatives = ParseAlternatives();
            ExpectOp(")");

            var group = new PegGroup { Alternatives = alternatives };
            return ApplyPostfix(group);
        }

        // Optional: '[' items ']'
        // CPython metagrammar.gram:101 - '[' ~ alts ']' {Opt(alts)}
        if (token.Type == TokenType.OP && token.Value == "[")
        {
            Advance();  // Skip '['
            var alternatives = ParseAlternatives();  // Parse multiple alternatives (CPython way)
            ExpectOp("]");

            Atom inner;
            // Simple case: [item] → Optional(item)
            if (alternatives.Count == 1 &&
                alternatives[0].Items.Count == 1 &&
                alternatives[0].Items[0].Name == null &&
                string.IsNullOrEmpty(alternatives[0].ActionCode))
            {
                inner = alternatives[0].Items[0].Atom;
            }
            // Complex case: [a | b] → Optional(Group(a, b))
            else
            {
                inner = new PegGroup { Alternatives = alternatives };
            }

            return new Optional { Inner = inner };
        }

        // Lookahead: '&' atom or '!' atom
        if (token.Type == TokenType.OP && token.Value == "&")
        {
            Advance();
            var inner = ParseAtom();
            if (inner == null)
                throw new Exception($"Expected atom after '&' at line {token.Line}");
            return new PositiveLookahead { Inner = inner };
        }

        if (token.Type == TokenType.OP && token.Value == "!")
        {
            Advance();
            var inner = ParseAtom();
            if (inner == null)
                throw new Exception($"Expected atom after '!' at line {token.Line}");
            return new NegativeLookahead { Inner = inner };
        }

        // NAME: token or rule reference
        if (token.Type == TokenType.NAME)
        {
            Advance();
            var name = token.Value;

            // Check for gather pattern: name '.' atom ('+' | '*')
            if (Current()?.Type == TokenType.OP && Current()?.Value == ".")
            {
                Advance();  // Skip '.'
                var item = ParseAtomWithoutPostfix();  // Don't apply postfix yet!
                if (item == null)
                    throw new Exception($"Expected atom after '.' in gather pattern");

                // Must be followed by '+' or '*'
                var postfix = Current();
                if (postfix?.Type == TokenType.OP && (postfix.Value == "+" || postfix.Value == "*"))
                {
                    bool isPlus = postfix.Value == "+";
                    Advance();

                    var separator = IsToken(name) ? (Atom)new Token { TokenType = name } : new RuleRef { Name = name };
                    return new Gather { Separator = separator, Item = item, IsPlus = isPlus };
                }

                throw new Exception($"Expected '+' or '*' after gather item at line {token.Line}");
            }

            // Regular NAME
            var atom = IsToken(name) ? (Atom)new Token { TokenType = name } : (Atom)new RuleRef { Name = name };
            return ApplyPostfix(atom);
        }

        return null;
    }

    private Atom? ParseAtomWithoutPostfix()
    {
        // Parse atom but don't apply postfix operators
        // Used for gather patterns where the postfix belongs to the gather, not the item
        var token = Current();
        if (token == null)
            return null;

        // STRING: 'keyword' or "soft_keyword" (no gather check here, just return keyword)
        if (token.Type == TokenType.STRING)
        {
            Advance();
            bool isSoft = token.Value.StartsWith("\"");
            string value = token.Value.Substring(1, token.Value.Length - 2);
            return new Keyword { Value = value, IsSoft = isSoft };
        }

        // Group: '(' alts ')'
        if (token.Type == TokenType.OP && token.Value == "(")
        {
            Advance();
            var alternatives = ParseAlternatives();
            ExpectOp(")");
            return new PegGroup { Alternatives = alternatives };
        }

        // Optional: '[' items ']'
        // CPython metagrammar.gram:101 - '[' ~ alts ']' {Opt(alts)}
        if (token.Type == TokenType.OP && token.Value == "[")
        {
            Advance();
            var alternatives = ParseAlternatives();  // Parse multiple alternatives (CPython way)
            ExpectOp("]");

            Atom inner;
            // Simple case: [item] → Optional(item)
            if (alternatives.Count == 1 &&
                alternatives[0].Items.Count == 1 &&
                alternatives[0].Items[0].Name == null &&
                string.IsNullOrEmpty(alternatives[0].ActionCode))
            {
                inner = alternatives[0].Items[0].Atom;
            }
            // Complex case: [a | b] → Optional(Group(a, b))
            else
            {
                inner = new PegGroup { Alternatives = alternatives };
            }
            return new Optional { Inner = inner };
        }

        // NAME: token or rule reference (no gather check here)
        if (token.Type == TokenType.NAME)
        {
            Advance();
            var name = token.Value;
            return IsToken(name) ? (Atom)new Token { TokenType = name } : new RuleRef { Name = name };
        }

        return null;
    }

    private Atom ApplyPostfix(Atom atom)
    {
        // Check for postfix: '*' | '+' | '?'
        var token = Current();
        if (token?.Type == TokenType.OP)
        {
            if (token.Value == "*")
            {
                Advance();
                return new ZeroOrMore { Inner = atom };
            }
            if (token.Value == "+")
            {
                Advance();
                return new OneOrMore { Inner = atom };
            }
            if (token.Value == "?")
            {
                Advance();
                return new Optional { Inner = atom };
            }
        }

        return atom;
    }

    private bool IsToken(string name)
    {
        // Token names are ALL_CAPS
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (char c in name)
        {
            if (!char.IsUpper(c) && c != '_')
                return false;
        }

        return true;
    }

    private void ParseTrailer()
    {
        // Check for @trailer '''...''' at start of grammar
        // Format: @ trailer STRING(triple-quoted)
        // Example: @trailer '''public class Entry { ... }'''

        // Skip leading newlines
        while (Current()?.Type == TokenType.NEWLINE)
        {
            Advance();
        }

        // Check for @ operator
        if (Current()?.Type != TokenType.OP || Current()?.Value != "@")
        {
            return;  // No @trailer directive
        }

        int mark = _position;  // Save position in case this isn't a valid @trailer
        Advance();  // Skip '@'

        // Check for 'trailer' NAME token
        if (Current()?.Type != TokenType.NAME || Current()?.Value != "trailer")
        {
            _position = mark;  // Not a @trailer, restore position
            return;
        }
        Advance();  // Skip 'trailer'

        // Expect STRING token (triple-quoted string)
        var stringToken = Current();
        if (stringToken?.Type != TokenType.STRING)
        {
            Console.WriteLine($"[ERROR] Expected STRING after @trailer at line {stringToken?.Line ?? 0}");
            _position = mark;
            return;
        }

        // Extract string content (remove triple quotes)
        string rawString = stringToken.Value;
        string trailerCode;

        if (rawString.StartsWith("'''") && rawString.EndsWith("'''"))
        {
            trailerCode = rawString.Substring(3, rawString.Length - 6);
        }
        else if (rawString.StartsWith("\"\"\"") && rawString.EndsWith("\"\"\""))
        {
            trailerCode = rawString.Substring(3, rawString.Length - 6);
        }
        else
        {
            Console.WriteLine($"[ERROR] @trailer string must be triple-quoted (''' or \"\"\") at line {stringToken.Line}");
            _position = mark;
            return;
        }

        Advance();  // Skip STRING token

        // Successfully parsed @trailer
        Trailer = new TrailerCode { Code = trailerCode };
        Console.WriteLine($"Parsed @trailer directive ({trailerCode.Length} characters)");

        // Skip any trailing newlines after @trailer
        while (Current()?.Type == TokenType.NEWLINE)
        {
            Advance();
        }
    }

    private GrammarToken? Current()
    {
        if (_position >= _tokens.Count)
            return null;
        return _tokens[_position];
    }

    private GrammarToken? Advance()
    {
        if (_position >= _tokens.Count)
            return null;
        return _tokens[_position++];
    }

    private GrammarToken? Expect(TokenType type)
    {
        var token = Current();
        if (token?.Type == type)
        {
            return Advance();
        }
        return null;
    }

    private GrammarToken? ExpectOp(string value)
    {
        var token = Current();
        if (token?.Type == TokenType.OP && token.Value == value)
        {
            return Advance();
        }
        return null;
    }
}
