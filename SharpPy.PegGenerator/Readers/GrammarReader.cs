using System.Text;
using SharpPy.PegGenerator.DataStructures;
using PegGroup = SharpPy.PegGenerator.DataStructures.Group;

namespace SharpPy.PegGenerator.Readers;

/// <summary>
/// python_py.gram 파일 파서
/// CRITICAL: Quote type (' vs ")을 정확히 보존하여 HARD/SOFT keyword 구분
/// </summary>
public class GrammarReader
{
    private string _source = "";
    private int _position = 0;
    private int _line = 1;
    private int _column = 0;

    public PegRule[] ReadGrammar(string filePath)
    {
        Console.WriteLine($"Reading grammar from: {filePath}");
        _source = File.ReadAllText(filePath);
        _position = 0;
        _line = 1;
        _column = 0;

        var rules = new List<PegRule>();

        // Parse all rules
        while (_position < _source.Length)
        {
            SkipWhitespaceAndComments();
            if (_position >= _source.Length)
                break;

            Console.WriteLine($"[DEBUG] Parsing rule #{rules.Count + 1} at line {_line}, position {_position}");
            var rule = ParseRule();
            if (rule != null)
            {
                Console.WriteLine($"[DEBUG] Parsed rule: {rule.Name} with {rule.Alternatives.Count} alternatives");
                rules.Add(rule);
            }
            else
            {
                Console.WriteLine($"[DEBUG] ParseRule returned null at line {_line}, position {_position}");
                break;
            }
        }

        Console.WriteLine($"Parsed {rules.Count} grammar rules");
        return rules.ToArray();
    }

    private void SkipWhitespaceAndComments()
    {
        while (_position < _source.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(_source[_position]))
            {
                if (_source[_position] == '\n')
                {
                    _line++;
                    _column = 0;
                }
                else
                {
                    _column++;
                }
                _position++;
                continue;
            }

            // Skip comments (# ...)
            if (_source[_position] == '#')
            {
                while (_position < _source.Length && _source[_position] != '\n')
                    _position++;
                continue;
            }

            break;
        }
    }

    private PegRule? ParseRule()
    {
        // rule_name[return_type]?: alternatives

        Console.WriteLine($"[DEBUG] ParseRule: Starting at line {_line}, pos {_position}");
        if (_position < _source.Length)
        {
            int endPos = Math.Min(_position + 50, _source.Length);
            string preview = _source.Substring(_position, endPos - _position).Replace("\n", "\\n").Replace("\r", "\\r");
            Console.WriteLine($"[DEBUG] ParseRule: Next 50 chars: '{preview}'");
            Console.WriteLine($"[DEBUG] ParseRule: Current char: '{_source[_position]}' (code: {(int)_source[_position]})");
        }

        // Parse rule name
        var ruleName = ParseIdentifier();
        Console.WriteLine($"[DEBUG] ParseRule: Parsed identifier '{ruleName}'");
        if (string.IsNullOrEmpty(ruleName))
        {
            Console.WriteLine($"[DEBUG] ParseRule: Empty identifier, returning null");
            return null;
        }

        // Skip optional return type [return_type]
        SkipWhitespaceAndComments();
        if (_position < _source.Length && _source[_position] == '[')
        {
            _position++;
            _column++;
            // Skip until ]
            while (_position < _source.Length && _source[_position] != ']')
            {
                if (_source[_position] == '\n')
                {
                    _line++;
                    _column = 0;
                }
                else
                {
                    _column++;
                }
                _position++;
            }
            if (_position < _source.Length && _source[_position] == ']')
            {
                _position++;
                _column++;
            }
        }

        SkipWhitespaceAndComments();

        // Expect ':'
        if (_position >= _source.Length || _source[_position] != ':')
        {
            throw new Exception($"Expected ':' after rule name '{ruleName}' at line {_line}");
        }
        _position++;
        _column++;

        SkipWhitespaceAndComments();

        // Parse alternatives
        var alternatives = ParseAlternatives();

        return new PegRule
        {
            Name = ruleName,
            Alternatives = alternatives
        };
    }

    private List<Alternative> ParseAlternatives()
    {
        var alternatives = new List<Alternative>();

        while (true)
        {
            SkipWhitespaceAndComments();

            Console.WriteLine($"[DEBUG] ParseAlternatives: line {_line}, pos {_position}, char: {(_position < _source.Length ? _source[_position] : 'E')}");

            // Check for '|' (may be on next line)
            if (_position < _source.Length && _source[_position] == '|')
            {
                _position++;
                _column++;
                SkipWhitespaceAndComments();
            }

            // Parse alternative items
            var items = ParseItems();
            Console.WriteLine($"[DEBUG] ParseItems returned {items.Count} items");
            if (items.Count == 0)
                break;

            alternatives.Add(new Alternative { Items = items });

            // Check if next alternative exists
            SkipWhitespaceAndComments();
            if (_position >= _source.Length || _source[_position] != '|')
                break;
        }

        Console.WriteLine($"[DEBUG] ParseAlternatives completed: {alternatives.Count} alternatives");
        return alternatives;
    }

    private List<Item> ParseItems()
    {
        var items = new List<Item>();
        int maxIterations = 1000;  // Safety limit
        int iterations = 0;

        while (true)
        {
            iterations++;
            if (iterations > maxIterations)
            {
                Console.WriteLine($"[ERROR] ParseItems exceeded max iterations at line {_line}");
                throw new Exception($"ParseItems infinite loop detected at line {_line}");
            }

            SkipWhitespaceAndComments();

            Console.WriteLine($"[DEBUG] ParseItems iteration {iterations}: line {_line}, pos {_position}, items so far: {items.Count}");

            // Stop at '|' or end of line (new rule)
            if (_position >= _source.Length)
                break;

            char c = _source[_position];

            // Stop at '|' (next alternative)
            if (c == '|')
            {
                Console.WriteLine($"[DEBUG] ParseItems: Found '|', stopping");
                break;
            }

            // Stop at newline if next non-whitespace is a rule name (identifier followed by ':')
            if (c == '\n')
            {
                int savePos = _position;
                int saveLine = _line;
                int saveCol = _column;

                _position++;
                _line++;
                _column = 0;
                SkipWhitespaceAndComments();

                if (_position < _source.Length && (char.IsLetter(_source[_position]) || _source[_position] == '_'))
                {
                    // Peek ahead without consuming - check if it's identifier followed by ':'
                    int testPos = _position;
                    while (testPos < _source.Length && (char.IsLetterOrDigit(_source[testPos]) || _source[testPos] == '_'))
                    {
                        testPos++;
                    }

                    // Skip whitespace after identifier
                    while (testPos < _source.Length && char.IsWhiteSpace(_source[testPos]) && _source[testPos] != '\n')
                    {
                        testPos++;
                    }

                    // Check for '[' (optional return type) or ':'
                    if (testPos < _source.Length && (_source[testPos] == ':' || _source[testPos] == '['))
                    {
                        Console.WriteLine($"[DEBUG] ParseItems: Found new rule marker at pos {testPos}, stopping");
                        // It's a new rule, restore position
                        _position = savePos;
                        _line = saveLine;
                        _column = saveCol;
                        break;
                    }
                }

                // Not a new rule, restore and continue
                _position = savePos;
                _line = saveLine;
                _column = saveCol;
                _position++;
                _line++;
                _column = 0;
                continue;
            }

            int posBeforeItem = _position;
            var item = ParseItem();
            if (item == null)
            {
                Console.WriteLine($"[DEBUG] ParseItems: ParseItem returned null, stopping");
                break;
            }

            // If position didn't advance, we're stuck - break to avoid infinite loop
            if (_position == posBeforeItem)
            {
                Console.WriteLine($"[ERROR] ParseItem didn't advance position, breaking to avoid infinite loop");
                break;
            }

            items.Add(item);

            // After adding an item, check if we're at end of line (rule complete)
            // Skip only inline whitespace (not newlines)
            int savePos2 = _position;
            while (_position < _source.Length && (_source[_position] == ' ' || _source[_position] == '\t'))
            {
                _position++;
                _column++;
            }

            // If we hit newline or end of file after whitespace, this rule is complete
            if (_position >= _source.Length || _source[_position] == '\n' || _source[_position] == '\r')
            {
                _position = savePos2;  // Don't consume the whitespace, let next iteration handle it
                Console.WriteLine($"[DEBUG] ParseItems: Reached end of line after item, will check for continuation");
                // Don't break here - let the newline check at top of loop determine if it's a new rule
            }
            else
            {
                _position = savePos2;  // Restore position for next item
            }
        }

        Console.WriteLine($"[DEBUG] ParseItems completed: {items.Count} items after {iterations} iterations");
        return items;
    }

    private Item? ParseItem()
    {
        SkipWhitespaceAndComments();

        if (_position >= _source.Length)
            return null;

        // Check for named item (name=atom)
        int savePos = _position;
        int saveLine = _line;
        int saveCol = _column;

        var identifier = ParseIdentifier();
        if (!string.IsNullOrEmpty(identifier))
        {
            SkipWhitespaceAndComments();
            if (_position < _source.Length && _source[_position] == '=')
            {
                _position++;
                _column++;
                SkipWhitespaceAndComments();

                var atom = ParseAtom();
                if (atom != null)
                {
                    return new Item { Name = identifier, Atom = atom };
                }
            }
        }

        // Not a named item, restore and parse as atom
        _position = savePos;
        _line = saveLine;
        _column = saveCol;

        var atomOnly = ParseAtom();
        if (atomOnly == null)
            return null;

        return new Item { Name = null, Atom = atomOnly };
    }

    private Atom? ParseAtom()
    {
        SkipWhitespaceAndComments();

        if (_position >= _source.Length)
            return null;

        char c = _source[_position];

        // CRITICAL: Parse keywords with QUOTE TYPE preservation
        // Single quote 'keyword' → HARD KEYWORD
        // Double quote "keyword" → SOFT KEYWORD
        if (c == '\'' || c == '"')
        {
            return ParseKeyword();
        }

        // Group (...)
        if (c == '(')
        {
            return ParseGroup();
        }

        // Optional [...]
        if (c == '[')
        {
            return ParseOptional();
        }

        // Positive lookahead &...
        if (c == '&')
        {
            _position++;
            _column++;
            var inner = ParseAtom();
            if (inner == null)
                throw new Exception($"Expected atom after '&' at line {_line}");
            return new PositiveLookahead { Inner = inner };
        }

        // Negative lookahead !...
        if (c == '!')
        {
            _position++;
            _column++;
            var inner = ParseAtom();
            if (inner == null)
                throw new Exception($"Expected atom after '!' at line {_line}");
            return new NegativeLookahead { Inner = inner };
        }

        // Commit operator ~
        if (c == '~')
        {
            _position++;
            _column++;
            // Skip commit operator (we don't use it for code generation)
            return ParseAtom();
        }

        // Token or RuleRef (identifier)
        if (char.IsLetter(c) || c == '_')
        {
            var identifier = ParseIdentifier();

            // Check for postfix operators (*, +, ?)
            SkipWhitespaceAndComments();
            if (_position < _source.Length)
            {
                char postfix = _source[_position];
                if (postfix == '*')
                {
                    _position++;
                    _column++;
                    var atom = IsToken(identifier) ? (Atom)new Token { TokenType = identifier } : new RuleRef { Name = identifier };
                    return new ZeroOrMore { Inner = atom };
                }
                if (postfix == '+')
                {
                    _position++;
                    _column++;
                    var atom = IsToken(identifier) ? (Atom)new Token { TokenType = identifier } : new RuleRef { Name = identifier };
                    return new OneOrMore { Inner = atom };
                }
                if (postfix == '?')
                {
                    _position++;
                    _column++;
                    var atom = IsToken(identifier) ? (Atom)new Token { TokenType = identifier } : new RuleRef { Name = identifier };
                    return new Optional { Inner = atom };
                }

                // Check for gather pattern (sep.item+ or sep.item*)
                if (postfix == '.')
                {
                    _position++;
                    _column++;
                    SkipWhitespaceAndComments();

                    var item = ParseAtom();
                    if (item == null)
                        throw new Exception($"Expected atom after '.' in gather pattern at line {_line}");

                    // Check for + or *
                    SkipWhitespaceAndComments();
                    if (_position < _source.Length)
                    {
                        if (_source[_position] == '+')
                        {
                            _position++;
                            _column++;
                            var sep = IsToken(identifier) ? (Atom)new Token { TokenType = identifier } : new RuleRef { Name = identifier };
                            return new Gather { Separator = sep, Item = item, IsPlus = true };
                        }
                        if (_source[_position] == '*')
                        {
                            _position++;
                            _column++;
                            var sep = IsToken(identifier) ? (Atom)new Token { TokenType = identifier } : new RuleRef { Name = identifier };
                            return new Gather { Separator = sep, Item = item, IsPlus = false };
                        }
                    }

                    throw new Exception($"Expected '+' or '*' after gather item at line {_line}");
                }
            }

            // Regular token or rule reference
            if (IsToken(identifier))
            {
                return new Token { TokenType = identifier };
            }
            else
            {
                return new RuleRef { Name = identifier };
            }
        }

        return null;
    }

    private Keyword ParseKeyword()
    {
        char quoteChar = _source[_position];
        bool isSoft = (quoteChar == '"');  // CRITICAL: " = SOFT, ' = HARD

        _position++;
        _column++;

        var sb = new StringBuilder();
        while (_position < _source.Length && _source[_position] != quoteChar)
        {
            sb.Append(_source[_position]);
            _position++;
            _column++;
        }

        if (_position >= _source.Length)
            throw new Exception($"Unterminated keyword at line {_line}");

        _position++; // Skip closing quote
        _column++;

        return new Keyword
        {
            Value = sb.ToString(),
            IsSoft = isSoft
        };
    }

    private PegGroup ParseGroup()
    {
        _position++; // Skip '('
        _column++;

        var alternatives = ParseAlternatives();

        SkipWhitespaceAndComments();
        if (_position >= _source.Length || _source[_position] != ')')
            throw new Exception($"Expected ')' at line {_line}");

        _position++; // Skip ')'
        _column++;

        // Check for postfix operators (*, +, ?)
        SkipWhitespaceAndComments();
        if (_position < _source.Length)
        {
            char postfix = _source[_position];
            var group = new PegGroup { Alternatives = alternatives };

            if (postfix == '*')
            {
                _position++;
                _column++;
                return (PegGroup)(object)new ZeroOrMore { Inner = group };
            }
            if (postfix == '+')
            {
                _position++;
                _column++;
                return (PegGroup)(object)new OneOrMore { Inner = group };
            }
            if (postfix == '?')
            {
                _position++;
                _column++;
                return (PegGroup)(object)new Optional { Inner = group };
            }
        }

        return new PegGroup { Alternatives = alternatives };
    }

    private Optional ParseOptional()
    {
        _position++; // Skip '['
        _column++;

        var items = ParseItems();

        SkipWhitespaceAndComments();
        if (_position >= _source.Length || _source[_position] != ']')
            throw new Exception($"Expected ']' at line {_line}");

        _position++; // Skip ']'
        _column++;

        // Wrap items in a group if multiple
        Atom inner;
        if (items.Count == 1 && items[0].Name == null)
        {
            inner = items[0].Atom;
        }
        else
        {
            inner = new PegGroup
            {
                Alternatives = new List<Alternative>
                {
                    new Alternative { Items = items }
                }
            };
        }

        return new Optional { Inner = inner };
    }

    private string ParseIdentifier()
    {
        SkipWhitespaceAndComments();

        if (_position >= _source.Length)
            return "";

        if (!char.IsLetter(_source[_position]) && _source[_position] != '_')
            return "";

        var sb = new StringBuilder();
        while (_position < _source.Length)
        {
            char c = _source[_position];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
                _position++;
                _column++;
            }
            else
            {
                break;
            }
        }

        return sb.ToString();
    }

    private bool IsToken(string identifier)
    {
        // Token names are ALL_CAPS (e.g., NAME, NUMBER, STRING, NEWLINE, ASYNC, AWAIT)
        if (string.IsNullOrEmpty(identifier))
            return false;

        foreach (char c in identifier)
        {
            if (!char.IsUpper(c) && c != '_')
                return false;
        }

        return true;
    }
}
