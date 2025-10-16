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
            var rule = ParseRule();
            if (rule != null)
            {
                rules.Add(rule);
                Console.WriteLine($"Parsed rule: {rule.Name} ({rule.Alternatives.Count} alternatives)");
            }
            else
            {
                break;
            }
        }

        Console.WriteLine($"Parsed {rules.Count} grammar rules");
        return rules.ToArray();
    }

    private PegRule? ParseRule()
    {
        // rule: NAME ':' alts NEWLINE

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

        // Optional [return_type]
        if (Current()?.Type == TokenType.OP && Current()?.Value == "[")
        {
            Advance();  // Skip '['
            // Skip until ']'
            while (Current() != null && !(Current()!.Type == TokenType.OP && Current()!.Value == "]"))
            {
                Advance();
            }
            if (Current()?.Type == TokenType.OP && Current()?.Value == "]")
            {
                Advance();  // Skip ']'
            }
        }

        var colonToken = ExpectOp(":");
        if (colonToken == null)
        {
            Console.WriteLine($"[ERROR] Expected ':' after rule name '{nameToken.Value}'");
            return null;
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
            Alternatives = alternatives
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

        if (Current()?.Type != TokenType.OP || Current()?.Value != "{")
            return null;

        Advance(); // Skip '{'

        var code = new System.Text.StringBuilder();
        int braceDepth = 1;

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

            // Append token value with appropriate spacing
            if (code.Length > 0 && token.Type != TokenType.OP)
            {
                code.Append(' ');
            }
            code.Append(token.Value);

            Advance();
        }

        return code.ToString().Trim();
    }

    private Item? ParseItem()
    {
        // item: [NAME '='] atom

        // Check for named item (name=atom)
        if (Current()?.Type == TokenType.NAME)
        {
            int mark = _position;
            var nameToken = Advance();

            if (Current()?.Type == TokenType.OP && Current()?.Value == "=")
            {
                Advance();  // Skip '='
                var atom = ParseAtom();
                if (atom != null)
                {
                    return new Item { Name = nameToken!.Value, Atom = atom };
                }
            }

            // Not a named item, restore
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
        if (token.Type == TokenType.OP && token.Value == "[")
        {
            Advance();  // Skip '['
            var alt = ParseAlternative();
            ExpectOp("]");

            Atom inner;
            if (alt != null && alt.Items.Count == 1 && alt.Items[0].Name == null)
            {
                inner = alt.Items[0].Atom;
            }
            else
            {
                inner = new PegGroup
                {
                    Alternatives = alt != null ? new List<Alternative> { alt } : new()
                };
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

        // Commit: '~'
        if (token.Type == TokenType.OP && token.Value == "~")
        {
            Advance();
            // Skip commit operator, parse next atom
            return ParseAtom();
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
        if (token.Type == TokenType.OP && token.Value == "[")
        {
            Advance();
            var alt = ParseAlternative();
            ExpectOp("]");

            Atom inner;
            if (alt != null && alt.Items.Count == 1 && alt.Items[0].Name == null)
            {
                inner = alt.Items[0].Atom;
            }
            else
            {
                inner = new PegGroup
                {
                    Alternatives = alt != null ? new List<Alternative> { alt } : new()
                };
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
