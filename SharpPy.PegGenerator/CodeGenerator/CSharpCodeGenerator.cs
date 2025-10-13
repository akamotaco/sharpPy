using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SharpPy.PegGenerator.Grammar;

namespace SharpPy.PegGenerator.CodeGenerator
{
    /// <summary>
    /// Represents a parsed PEG grammar rule
    /// </summary>
    public class PegRule
    {
        public string Name { get; set; } = "";
        public string ReturnType { get; set; } = "object";
        public List<PegAlternative> Alternatives { get; set; } = new();
        public bool IsMemo { get; set; }
    }

    /// <summary>
    /// Represents an alternative in a PEG rule
    /// </summary>
    public class PegAlternative
    {
        public List<PegElement> Elements { get; set; } = new();
        public string? Action { get; set; }
    }

    /// <summary>
    /// Represents an element in a PEG alternative
    /// </summary>
    public class PegElement
    {
        public string Type { get; set; } = ""; // "keyword", "token", "rule", "group"
        public string Value { get; set; } = "";
        public string? Modifier { get; set; } // "?", "*", "+", "&", "!"
        public string? Label { get; set; }
        public List<PegElement>? GroupElements { get; set; }
        public string? Separator { get; set; } // for s.e+ syntax
    }

    /// <summary>
    /// Generates C# parser code from PEG grammar
    /// </summary>
    public partial class CSharpCodeGenerator
    {
        private readonly Grammar.Grammar _grammar;
        private readonly List<TokenDefinition> _tokens;
        private readonly StringBuilder _output;
        private int _indentLevel;
        private readonly List<PegRule> _pegRules = new();

        // CPython 3.12: artificial_rule_from_repeat - Loop rule 레지스트리
        private readonly Dictionary<string, string> _loopRules = new(); // pattern -> rule name (e.g., "_Loop1_4")
        private readonly Dictionary<string, Atom> _loopRuleAtoms = new(); // rule name -> Atom (for complex patterns)
        private int _loopRuleCounter = 0;

        // CPython 3.12: artificial_rule_from_gather - Gather rule 레지스트리
        // Uses SAME counter as loop rules (CPython behavior: self.counter for all artificial rules)
        private readonly Dictionary<string, string> _gatherRules = new(); // pattern -> _gather_N rule name
        private readonly List<Rule> _artificialRules = new(); // Auto-generated helper rules

        // Memoization cache for rule return types to prevent infinite recursion
        private readonly Dictionary<string, string> _ruleReturnTypeCache = new();
        private readonly HashSet<string> _ruleReturnTypeInProgress = new();

        // CPython 3.12: Keyword management (like CPython's self.keywords and self.keyword_counter)
        // Maps keyword string to unique token type number (starting from 500)
        private readonly Dictionary<string, int> _keywords = new(); // keyword -> token type
        private int _keywordCounter = 499; // CPython starts at 499, increments to 500, 501, etc.

        public CSharpCodeGenerator(Grammar.Grammar grammar, List<TokenDefinition> tokens)
        {
            _grammar = grammar;
            _tokens = tokens;
            _output = new StringBuilder();
            _indentLevel = 0;
        }

        /// <summary>
        /// Parse python.gram file directly and extract all rules
        /// </summary>
        public void ParseGrammarFile(string grammarFilePath)
        {
            if (!File.Exists(grammarFilePath))
            {
                throw new FileNotFoundException($"Grammar file not found: {grammarFilePath}");
            }

            var lines = File.ReadAllLines(grammarFilePath);
            var currentRule = new PegRule();
            var inRule = false;
            var ruleLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Skip comments and empty lines
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith("@"))
                    continue;

                // Check if this is the start of a new rule
                var ruleMatch = Regex.Match(trimmed, @"^([a-zA-Z_][a-zA-Z0-9_]*)(?:\[([^\]]+)\])?\s*(?:\(memo\))?\s*:");
                if (ruleMatch.Success)
                {
                    // Save previous rule if we were in one
                    if (inRule && !string.IsNullOrEmpty(currentRule.Name))
                    {
                        ParseRuleBody(currentRule, string.Join(" ", ruleLines));
                        _pegRules.Add(currentRule);
                    }

                    // Start new rule
                    currentRule = new PegRule
                    {
                        Name = ruleMatch.Groups[1].Value,
                        ReturnType = ruleMatch.Groups[2].Success ? ruleMatch.Groups[2].Value : "object",
                        IsMemo = trimmed.Contains("(memo)")
                    };
                    inRule = true;
                    ruleLines.Clear();

                    // Add the part after the colon to rule lines
                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex >= 0 && colonIndex < trimmed.Length - 1)
                    {
                        var afterColon = trimmed.Substring(colonIndex + 1).Trim();
                        if (!string.IsNullOrEmpty(afterColon))
                        {
                            ruleLines.Add(afterColon);
                        }
                    }
                }
                else if (inRule)
                {
                    // Continue current rule
                    ruleLines.Add(trimmed);
                }
            }

            // Save the last rule
            if (inRule && !string.IsNullOrEmpty(currentRule.Name))
            {
                ParseRuleBody(currentRule, string.Join(" ", ruleLines));
                _pegRules.Add(currentRule);
            }

            Console.WriteLine($"[INFO] Parsed {_pegRules.Count} rules from grammar file");
        }

        /// <summary>
        /// Parse rule body and extract alternatives
        /// </summary>
        private void ParseRuleBody(PegRule rule, string body)
        {
            try
            {
                // Extract and preserve action code, then split by | for alternatives
                var alternatives = new List<string>();
                var actions = new List<string?>();

                // Split by | while preserving actions
                var parts = body.Split('|');
                foreach (var part in parts)
                {
                    var trimmedPart = part.Trim();
                    if (string.IsNullOrEmpty(trimmedPart)) continue;

                    // Extract action code if present
                    string? action = null;
                    var actionMatch = Regex.Match(trimmedPart, @"\{([^}]*)\}");
                    if (actionMatch.Success)
                    {
                        action = actionMatch.Value; // Keep the braces
                        trimmedPart = Regex.Replace(trimmedPart, @"\{[^}]*\}", "").Trim();
                    }

                    alternatives.Add(trimmedPart);
                    actions.Add(action);
                }

                for (int i = 0; i < alternatives.Count; i++)
                {
                    var alternative = new PegAlternative();
                    alternative.Action = actions[i]; // Preserve the action
                    ParseAlternative(alternative, alternatives[i]);
                    rule.Alternatives.Add(alternative);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Error parsing rule {rule.Name}: {ex.Message}");
                // Add empty alternative as fallback
                rule.Alternatives.Add(new PegAlternative());
            }
        }

        /// <summary>
        /// Parse a single alternative and extract elements with enhanced PEG pattern support
        /// </summary>
        private void ParseAlternative(PegAlternative alternative, string text)
        {
            var elements = new List<PegElement>();
            var i = 0;

            while (i < text.Length)
            {
                // Skip whitespace
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;

                if (i >= text.Length) break;

                var element = new PegElement();
                var startPos = i;

                // Handle labels (a=expression)
                if (IsLabelPattern(text, i))
                {
                    var equalPos = text.IndexOf('=', i);
                    element.Label = text.Substring(i, equalPos - i).Trim();
                    i = equalPos + 1;
                    // Skip whitespace after =
                    while (i < text.Length && char.IsWhiteSpace(text[i]))
                        i++;
                    startPos = i;
                }

                // Handle optional groups [expression] or optional elements
                if (text[i] == '[')
                {
                    var closingBracket = FindMatchingBracket(text, i, '[', ']');
                    if (closingBracket > 0)
                    {
                        var optionalContent = text.Substring(i + 1, closingBracket - i - 1);
                        element.Type = "optional";
                        element.Value = optionalContent.Trim();
                        element.Modifier = "?";
                        i = closingBracket + 1;
                    }
                    else
                    {
                        i++; // Skip malformed bracket
                    }
                }
                // Handle grouped expressions (expression)
                else if (text[i] == '(')
                {
                    var closingParen = FindMatchingBracket(text, i, '(', ')');
                    if (closingParen > 0)
                    {
                        var groupContent = text.Substring(i + 1, closingParen - i - 1);
                        element.Type = "group";
                        element.Value = groupContent.Trim();
                        i = closingParen + 1;

                        // Check for modifiers after group
                        if (i < text.Length && (text[i] == '?' || text[i] == '*' || text[i] == '+'))
                        {
                            element.Modifier = text[i].ToString();
                            i++;
                        }
                    }
                    else
                    {
                        i++; // Skip malformed parenthesis
                    }
                }
                // Handle quoted strings (keywords)
                else if (text[i] == '\'' || text[i] == '"')
                {
                    var quote = text[i];
                    i++; // skip opening quote
                    var start = i;
                    while (i < text.Length && text[i] != quote)
                    {
                        if (text[i] == '\\' && i + 1 < text.Length)
                            i += 2; // skip escaped character
                        else
                            i++;
                    }
                    if (i < text.Length) i++; // skip closing quote

                    element.Value = text.Substring(start, i - start - 1);
                    element.Type = quote == '\'' ? "keyword" : "soft_keyword";
                }
                else
                {
                    // Handle regular tokens and rules
                    var tokenEnd = FindTokenEnd(text, i);
                    var token = text.Substring(i, tokenEnd - i);
                    i = tokenEnd;

                    // Check for modifiers after token
                    if (i < text.Length && (text[i] == '?' || text[i] == '*' || text[i] == '+'))
                    {
                        element.Modifier = text[i].ToString();
                        i++;
                    }
                    // Check for lookahead/negative lookahead
                    else if (token.StartsWith("&") && token.Length > 1)
                    {
                        element.Modifier = "&";
                        element.Value = token.Substring(1);
                    }
                    else if (token.StartsWith("!") && token.Length > 1)
                    {
                        element.Modifier = "!";
                        element.Value = token.Substring(1);
                    }
                    else
                    {
                        element.Value = token;
                    }

                    // Determine element type for non-quoted tokens
                    if (string.IsNullOrEmpty(element.Value))
                        element.Value = token;

                    if (element.Value.All(c => char.IsUpper(c) || c == '_') && element.Value.Length > 0)
                    {
                        element.Type = "token";
                    }
                    else if (!string.IsNullOrEmpty(element.Value))
                    {
                        element.Type = "rule";
                    }
                }

                // Only add valid elements
                if (!string.IsNullOrEmpty(element.Value) || !string.IsNullOrEmpty(element.Type))
                {
                    elements.Add(element);
                }
            }

            alternative.Elements.AddRange(elements);
        }

        /// <summary>
        /// Check if current position matches label pattern (name=)
        /// </summary>
        private bool IsLabelPattern(string text, int pos)
        {
            var equalPos = text.IndexOf('=', pos);
            if (equalPos <= pos) return false;

            var labelCandidate = text.Substring(pos, equalPos - pos).Trim();
            return Regex.IsMatch(labelCandidate, @"^[a-zA-Z_][a-zA-Z0-9_]*$");
        }

        /// <summary>
        /// Find matching closing bracket/parenthesis
        /// </summary>
        private int FindMatchingBracket(string text, int start, char open, char close)
        {
            int depth = 1;
            for (int i = start + 1; i < text.Length; i++)
            {
                if (text[i] == open) depth++;
                else if (text[i] == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Find end of current token (stops at whitespace or special chars)
        /// </summary>
        private int FindTokenEnd(string text, int start)
        {
            int i = start;
            while (i < text.Length &&
                   !char.IsWhiteSpace(text[i]) &&
                   text[i] != '(' && text[i] != ')' &&
                   text[i] != '[' && text[i] != ']' &&
                   text[i] != '?' && text[i] != '*' && text[i] != '+')
            {
                i++;
            }
            return i;
        }

        /// <summary>
        /// Generate the complete C# parser class
        /// </summary>
        public string GenerateParser()
        {
            // CPython 3.12: First pass - collect all keywords from grammar
            CollectKeywords();

            GenerateHeader();
            GenerateParserClass();
            return _output.ToString();
        }

        /// <summary>
        /// CPython 3.12: Collect all keywords from grammar (like CPython's parser_generator.py)
        /// Scans all StringLiteral atoms and assigns unique token type numbers
        /// </summary>
        private void CollectKeywords()
        {
            foreach (var rule in _grammar.Rules)
            {
                CollectKeywordsFromRule(rule);
            }
            Console.WriteLine($"[INFO] Collected {_keywords.Count} keywords from grammar");
        }

        private void CollectKeywordsFromRule(Rule rule)
        {
            foreach (var alt in rule.Alternatives)
            {
                foreach (var item in alt.Items)
                {
                    CollectKeywordsFromAtom(item.Atom);
                }
            }
        }

        private void CollectKeywordsFromAtom(Atom atom)
        {
            switch (atom)
            {
                case StringLiteral lit:
                    // CPython 3.12: Grammar quote convention determines keyword type
                    // 'keyword' (single quote) = hard keyword → add to KeywordType enum
                    // "keyword" (double quote) = soft keyword → handled by ExpectSoftKeyword()
                    if (System.Text.RegularExpressions.Regex.IsMatch(lit.Value, @"^[a-zA-Z_]\w*$"))
                    {
                        // Soft keywords use ExpectSoftKeyword() and are NOT added to KeywordType enum
                        if (lit.QuoteChar == '"')
                        {
                            // This is a soft keyword - do NOT add to keyword dictionary
                            break;
                        }
                        // Hard keyword (single quote) - add to keyword dictionary for token type mapping
                        if (!_keywords.ContainsKey(lit.Value))
                        {
                            _keywordCounter++;
                            _keywords[lit.Value] = _keywordCounter;
                        }
                    }
                    break;

                case Grammar.Group grp:
                    foreach (var alt in grp.Alternatives)
                    {
                        foreach (var item in alt.Items)
                        {
                            CollectKeywordsFromAtom(item.Atom);
                        }
                    }
                    break;

                case Optional opt:
                    CollectKeywordsFromAtom(opt.Expression);
                    break;

                case ZeroOrMore zm:
                    CollectKeywordsFromAtom(zm.Expression);
                    break;

                case OneOrMore om:
                    CollectKeywordsFromAtom(om.Expression);
                    break;

                case Gather gather:
                    CollectKeywordsFromAtom(gather.Item);
                    CollectKeywordsFromAtom(gather.Separator);
                    break;

                case PositiveLookahead pla:
                    CollectKeywordsFromAtom(pla.Expression);
                    break;

                case NegativeLookahead nla:
                    CollectKeywordsFromAtom(nla.Expression);
                    break;
            }
        }

        private void GenerateHeader()
        {
            WriteLine("// Generated parser from Grammar/python.gram");
            WriteLine("// DO NOT EDIT - This file is automatically generated");
            WriteLine("// Complete standalone parser without external dependencies");
            WriteLine();
            WriteLine("using System;");
            WriteLine("using System.Collections.Generic;");
            WriteLine("using System.Linq;");
            WriteLine();
            WriteLine("using static SharpPy.Generated.PegenHelpers;");
            WriteLine("using static SharpPy.Generated.AstFactory;");
            WriteLine("using static SharpPy.GeneratedParserBridge;");
            WriteLine();
        }

        // GenerateTokenEnum method removed - tokens now generated in tokenizer only

        private void GenerateParserClass()
        {
            // WriteLine("using SharpPy.Tokenizer;");
            WriteLine();
            WriteLine("// CPython 3.12: Type aliases for grammar compatibility");
            WriteLine("using stmt_ty = SharpPy.Generated.GeneratedStmt;");
            WriteLine("using expr_ty = SharpPy.Generated.GeneratedExpr;");
            WriteLine("using alias_ty = SharpPy.Generated.GeneratedAlias;");
            WriteLine("using arguments_ty = SharpPy.Generated.GeneratedArguments;");
            WriteLine("using asdl_stmt_seq = SharpPy.Generated.GeneratedStmtSeq;");
            WriteLine("using asdl_expr_seq = SharpPy.Generated.GeneratedExprSeq;");
            WriteLine("using asdl_arg_seq = SharpPy.Generated.GeneratedArgSeq;");
            WriteLine("using asdl_identifier_seq = SharpPy.Generated.GeneratedIdentifierSeq;");
            WriteLine("using asdl_pattern_seq = SharpPy.Generated.GeneratedPatternSeq;");
            WriteLine("using asdl_int_seq = SharpPy.Generated.GeneratedCmpopSeq;");
            WriteLine("using asdl_keyword_seq = SharpPy.Generated.GeneratedKeywordSeq;");
            WriteLine("using asdl_seq = SharpPy.Generated.GeneratedSeq;");
            WriteLine("using keyword_ty = SharpPy.Generated.GeneratedKeyword;");
            WriteLine();
            WriteLine("namespace SharpPy.Generated");
            WriteLine("{");
            Indent();

            // AST node types are now defined in Generated/GeneratedAstTypes.cs
            // This provides type-safe inheritance hierarchy without boxing/unboxing
            WriteLine("// AST node types (GeneratedStmt, GeneratedExpr, etc.) are defined in GeneratedAstTypes.cs");
            WriteLine();

            // TokenInfoWrapper now defined in SharpPy.Tokenizer project

            WriteLine("/// <summary>");
            WriteLine("/// Generated PEG parser for Python 3.12 grammar");
            WriteLine("/// Inherits from PyParserBase for common parsing logic");
            WriteLine("/// </summary>");
            WriteLine("public partial class GeneratedPyParser : PyParserBase<GeneratedMod>");
            WriteLine("{");
            Indent();

            // CPython 3.12: Generate keyword table (like CPython's reserved_keywords)
            GenerateKeywordTable();

            GenerateParserConstructor();
            // CPython 3.12: Generate all rule methods from grammar
            GenerateParserMethods();
            GeneratePythonGrammarMethods();
            GenerateTypeParameterMethods();
            GenerateStringHelperMethods();

            Dedent();
            WriteLine("}");

            Dedent();
            WriteLine("}"); // Close namespace
        }


        /// <summary>
        /// CPython 3.12: Generate keyword table (like CPython's reserved_keywords in parser.c)
        /// Groups keywords by length for efficient lookup
        /// </summary>
        private void GenerateKeywordTable()
        {
            WriteLine("// CPython 3.12: Keyword token types as enum");
            WriteLine("// C# improvement: Type-safe keyword types");
            WriteLine("private enum KeywordType");
            WriteLine("{");
            Indent();

            var sortedKeywords = _keywords.OrderBy(kv => kv.Key).ToList();
            for (int i = 0; i < sortedKeywords.Count; i++)
            {
                var kv = sortedKeywords[i];
                string enumName = kv.Key.ToUpper();
                if (enumName == "NONE" || enumName == "TRUE" || enumName == "FALSE")
                {
                    enumName = "KW_" + enumName; // Avoid conflict with C# keywords
                }
                string comma = (i < sortedKeywords.Count - 1) ? "," : "";
                WriteLine($"{enumName} = {kv.Value}{comma}");
            }

            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// CPython 3.12: Keyword table (like reserved_keywords in parser.c)");
            WriteLine("// Length-indexed array for O(1) lookup by keyword length");
            WriteLine();

            // Group keywords by length (CPython optimization)
            var keywordsByLength = new Dictionary<int, List<KeyValuePair<string, int>>>();
            foreach (var kv in _keywords)
            {
                int len = kv.Key.Length;
                if (!keywordsByLength.ContainsKey(len))
                {
                    keywordsByLength[len] = new List<KeyValuePair<string, int>>();
                }
                keywordsByLength[len].Add(kv);
            }

            int maxLength = keywordsByLength.Keys.Max();

            // CPython: static KeywordToken *reserved_keywords[]
            WriteLine($"private static readonly int NKeywordLists = {maxLength + 1};");
            WriteLine("private static readonly Dictionary<string, int>[] ReservedKeywords = new Dictionary<string, int>[]");
            WriteLine("{");
            Indent();

            // Generate array indexed by keyword length
            for (int len = 0; len <= maxLength; len++)
            {
                if (keywordsByLength.ContainsKey(len))
                {
                    WriteLine("new Dictionary<string, int> {");
                    Indent();
                    var keywords = keywordsByLength[len];
                    for (int i = 0; i < keywords.Count; i++)
                    {
                        var kv = keywords[i];
                        string comma = (i < keywords.Count - 1) ? "," : "";
                        WriteLine($"{{ \"{kv.Key}\", {kv.Value} }}{comma}");
                    }
                    Dedent();
                    string arrayComma = (len < maxLength) ? "}," : "}";
                    WriteLine(arrayComma);
                }
                else
                {
                    string arrayComma = (len < maxLength) ? "null," : "null";
                    WriteLine(arrayComma);
                }
            }

            Dedent();
            WriteLine("};");
            WriteLine();

            // CPython: _get_keyword_or_name_type() equivalent
            WriteLine("// CPython 3.12: _get_keyword_or_name_type() - Check if NAME token is a keyword");
            WriteLine("// Implements abstract method from PyParserBase");
            WriteLine("protected override int GetKeywordOrNameType(string name, int nameLen)");
            WriteLine("{");
            Indent();
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[GetKeywordOrNameType] name='{name}', nameLen={nameLen}, NKeywordLists={NKeywordLists}\");");
            WriteLine("#endif");
            WriteLine("if (nameLen >= NKeywordLists || ReservedKeywords[nameLen] == null)");
            WriteLine("{");
            Indent();
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[GetKeywordOrNameType] OUT OF BOUNDS or NULL, returning NAME\");");
            WriteLine("#endif");
            WriteLine("return (int)TokenType.NAME;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("if (ReservedKeywords[nameLen].TryGetValue(name, out int keywordType))");
            WriteLine("{");
            Indent();
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[GetKeywordOrNameType] FOUND keyword '{name}' = {keywordType}\");");
            WriteLine("#endif");
            WriteLine("return keywordType;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine("Console.WriteLine($\"[GetKeywordOrNameType] NOT FOUND '{name}', returning NAME\");");
            WriteLine("#endif");
            WriteLine("return (int)TokenType.NAME;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Helper for compile.cs to check if a string is a keyword
            WriteLine("// Helper for compile.cs to check if a string is a keyword");
            WriteLine("public static bool IsKeyword(string name)");
            WriteLine("{");
            Indent();
            WriteLine("int nameLen = name.Length;");
            WriteLine("if (nameLen >= NKeywordLists || ReservedKeywords[nameLen] == null)");
            WriteLine("{");
            Indent();
            WriteLine("return false;");
            Dedent();
            WriteLine("}");
            WriteLine("return ReservedKeywords[nameLen].ContainsKey(name);");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateParserConstructor()
        {
            // CPython 3.12: Generated parser is independent - no interpreter needed
            WriteLine("public GeneratedPyParser(List<GeneratedTokenInfo> tokens, string filename = \"<string>\")");
            WriteLine("    : base(tokens, filename)");
            WriteLine("{");
            WriteLine("    // CPython 3.12: Compiled parser - no runtime interpreter");
            WriteLine("}");
            WriteLine();

            // CPython 3.12: Constructor with source code for error reporting
            WriteLine("// CPython 3.12: Constructor with source code for error reporting");
            WriteLine("public GeneratedPyParser(List<GeneratedTokenInfo> tokens, string filename, string source)");
            WriteLine("    : base(tokens, filename, source)");
            WriteLine("{");
            WriteLine("    // CPython 3.12: Source code available for detailed error messages");
            WriteLine("}");
            WriteLine();

            WriteLine("// Override abstract Parse method");
            WriteLine("public override GeneratedMod Parse()");
            WriteLine("{");
            Indent();
            WriteLine("return ParseFile();");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateParserMethods()
        {

            // CPython 3.12: ALL rules are auto-generated from grammar
            // No manual expression hierarchy generation needed
            // GenerateExpressionHierarchy();

            // Note: EmbeddedPegInterpreter is now in PyParserBase

            // CPython 3.12: Analyze left-recursion BEFORE generating code (parser_generator.py:107)
            Console.WriteLine("[PEGEN] Computing left-recursive rules...");
            var ruleDict = _grammar.Rules.ToDictionary(r => r.Name, r => r);
            Analysis.LeftRecursionAnalyzer.ComputeLeftRecursives(ruleDict);

            // Debug: Print left-recursive rules
            foreach (var rule in _grammar.Rules.Where(r => r.IsLeftRecursive))
            {
                Console.WriteLine($"[PEGEN] Left-recursive rule: {rule.Name}, IsLeader={rule.IsLeader}");
            }

            // Use existing implementation for now
            Console.WriteLine($"[DEBUG] Total rules in _grammar.Rules: {_grammar.Rules.Count}");
            foreach (var rule in _grammar.Rules)
            {
                Console.WriteLine($"[DEBUG] Processing rule: {rule.Name}");
                GenerateRuleMethod(rule);
            }

            // CPython 3.12: Generate loop rules after all main rules
            // This is the artificial_rule_from_repeat mechanism
            GenerateLoopRuleMethods();

            // CPython 3.12: Generate artificial rules (gather helpers)
            // This is the artificial_rule_from_gather mechanism
            GenerateArtificialRules();
        }

        private void GenerateHelperMethods()
        {
            WriteLine("// Helper methods");

            // Current token property
            WriteLine("private GeneratedTokenInfo? CurrentToken => _position < _tokens.Count ? _tokens[_position] : null;");
            WriteLine();

            // Advance method
            WriteLine("private void Advance()");
            WriteLine("{");
            Indent();
            WriteLine("if (_position < _tokens.Count) _position++;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Expect method - CPython 3.12: Returns token if matches, null otherwise
            WriteLine("/// <summary>");
            WriteLine("/// Expect a specific token value. Returns the token if it matches, null otherwise.");
            WriteLine("/// CPython 3.12 PEG compatible.");
            WriteLine("/// </summary>");
            WriteLine("private GeneratedTokenInfo? Expect(string expected)");
            WriteLine("{");
            Indent();
            WriteLine("if (CurrentToken?.Value == expected)");
            WriteLine("{");
            Indent();
            WriteLine("var token = CurrentToken;");
            WriteLine("Advance();");
            WriteLine("return token;");
            Dedent();
            WriteLine("}");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Skip NL tokens method - for implicit line joining inside parentheses
            WriteLine("/// <summary>");
            WriteLine("/// Skip NL tokens for implicit line joining (CPython 3.12 compatible)");
            WriteLine("/// </summary>");
            WriteLine("private void SkipNL()");
            WriteLine("{");
            Indent();
            WriteLine("while (CurrentToken?.Type == TokenType.NL)");
            WriteLine("{");
            WriteLine("    Advance(); // skip NL token");
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();

            // ExpectKeyword method - REMOVED: Now using base class Expect() method
            // Keywords are matched using Expect("keyword") which checks token value

            // Mark/Reset for backtracking
            WriteLine("private int Mark() => _position;");
            WriteLine();
            WriteLine("private void Reset(int mark) => _position = mark;");
            WriteLine();

            // Memoization methods
            WriteLine("private T? GetMemo<T>(string ruleName) where T : GeneratedAstNode");
            WriteLine("{");
            Indent();
            WriteLine("var key = (_position, ruleName);");
            WriteLine("return _memoCache.TryGetValue(key, out var value) ? value as T : default;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("private void SetMemo<T>(string ruleName, T? value) where T : GeneratedAstNode");
            WriteLine("{");
            Indent();
            WriteLine("var key = (_position, ruleName);");
            WriteLine("_memoCache[key] = value;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add ParseZeroOrMore helper method
            WriteLine("private List<T> ParseZeroOrMore<T>(Func<T?> parseFunc) where T : class");
            WriteLine("{");
            Indent();
            WriteLine("var results = new List<T>();");
            WriteLine("while (true)");
            WriteLine("{");
            Indent();
            WriteLine("var startPos = _position;");
            WriteLine("var result = parseFunc();");
            WriteLine("if (result == null)");
            WriteLine("{");
            Indent();
            WriteLine("_position = startPos;");
            WriteLine("break;");
            Dedent();
            WriteLine("}");
            WriteLine("results.Add(result);");
            Dedent();
            WriteLine("}");
            WriteLine("return results;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add ParseOneOrMore helper method
            WriteLine("private List<T>? ParseOneOrMore<T>(Func<T?> parseFunc) where T : class");
            WriteLine("{");
            Indent();
            WriteLine("var results = ParseZeroOrMore(parseFunc);");
            WriteLine("return results.Count > 0 ? results : null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add ParseOptional helper method
            WriteLine("private T? ParseOptional<T>(Func<T?> parseFunc) where T : class");
            WriteLine("{");
            Indent();
            WriteLine("var startPos = _position;");
            WriteLine("var result = parseFunc();");
            WriteLine("if (result == null)");
            WriteLine("{");
            Indent();
            WriteLine("_position = startPos;");
            Dedent();
            WriteLine("}");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add ParseGroup helper method
            WriteLine("private T? ParseGroup<T>(Func<T?> parseFunc) where T : class");
            WriteLine("{");
            Indent();
            WriteLine("return parseFunc();");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add AtEndOfFile method
            WriteLine("private bool AtEndOfFile()");
            WriteLine("{");
            Indent();
            WriteLine("return _position >= _tokens.Count || CurrentToken?.Type.ToString() == \"ENDMARKER\";");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add ExpectOperator method
            WriteLine("private bool ExpectOperator(string op)");
            WriteLine("{");
            Indent();
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == op)");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            WriteLine("return true;");
            Dedent();
            WriteLine("}");
            WriteLine("return false;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add ExpectTokenType method
            WriteLine("private string? ExpectTokenType(string tokenType)");
            WriteLine("{");
            Indent();
            WriteLine("if (CurrentToken?.Type.ToString() == tokenType)");
            WriteLine("{");
            Indent();
            WriteLine("var value = CurrentToken?.Value;");
            WriteLine("Advance();");
            WriteLine("return value;");
            Dedent();
            WriteLine("}");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // CPython 3.12: No runtime string parsing needed
            // Generated parser methods handle all parsing directly (no TryParseGrammarItems, ParsePattern, etc.)
        }

        /// <summary>
        /// Generate a method for a PEG rule automatically
        /// </summary>
        private void GenerateAutoRuleMethod(PegRule rule)
        {
            var returnType = TranslatePegTypeToCS(rule.ReturnType);
            var methodName = ToCSharpMethodName(rule.Name);

            WriteLine($"// Auto-generated rule: {rule.Name}");
            WriteLine($"public {returnType} {methodName}()");
            WriteLine("{");
            Indent();

            if (rule.IsMemo)
            {
                WriteLine($"// Check memo cache");
                WriteLine($"var cached = GetMemo<{returnType}>(\"{rule.Name}\");");
                WriteLine($"if (cached != null) return cached;");
                WriteLine();
                WriteLine($"var startPos = _position;");
                WriteLine();
            }

            // Generate alternatives
            if (rule.Alternatives.Count == 1)
            {
                GenerateAlternative(rule.Alternatives[0], returnType);
            }
            else
            {
                WriteLine("// Try alternatives in order");
                for (int i = 0; i < rule.Alternatives.Count; i++)
                {
                    var alt = rule.Alternatives[i];
                    WriteLine($"// Alternative {i + 1}");
                    WriteLine("{");
                    Indent();
                    WriteLine("var mark = Mark();");
                    GenerateAlternative(alt, returnType);
                    WriteLine("Reset(mark);");
                    Dedent();
                    WriteLine("}");
                    WriteLine();
                }
                WriteLine($"return default({returnType});");
            }

            if (rule.IsMemo)
            {
                WriteLine();
                WriteLine($"// Cache result");
                WriteLine($"SetMemo(\"{rule.Name}\", result);");
                WriteLine($"return result;");
            }

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate code for a single alternative with actual parsing logic
        /// </summary>
        private void GenerateAlternative(PegAlternative alternative, string returnType)
        {
            if (alternative.Elements.Count == 0)
            {
                WriteLine($"return default({returnType});");
                return;
            }

            WriteLine("// Parse elements in sequence");
            var varNames = new List<string>();

            // Generate parsing code for each element
            for (int i = 0; i < alternative.Elements.Count; i++)
            {
                var element = alternative.Elements[i];
                var varName = string.IsNullOrEmpty(element.Label) ? $"element{i}" : element.Label;
                varNames.Add(varName);

                GenerateElementParsingCode(element, varName);
            }

            // Generate return statement with AST creation
            GenerateASTCreation(alternative, returnType, varNames);
        }

        /// <summary>
        /// Generate parsing code for a single element
        /// </summary>
        private void GenerateElementParsingCode(PegElement element, string varName)
        {
            switch (element.Type)
            {
                case "keyword":
                    GenerateKeywordParsingCode(element, varName);
                    break;
                case "soft_keyword":
                    GenerateSoftKeywordParsingCode(element, varName);
                    break;
                case "token":
                    GenerateTokenParsingCode(element, varName);
                    break;
                case "rule":
                    GenerateRuleParsingCode(element, varName);
                    break;
                case "optional":
                    GenerateOptionalParsingCode(element, varName);
                    break;
                case "group":
                    GenerateGroupParsingCode(element, varName);
                    break;
                default:
                    WriteLine($"// Unknown element type: {element.Type}");
                    WriteLine($"var {varName} = default(object);");
                    break;
            }
        }

        /// <summary>
        /// Generate keyword parsing code
        /// </summary>
        private void GenerateKeywordParsingCode(PegElement element, string varName)
        {
            var modifier = element.Modifier ?? "";
            var escapedValue = EscapeStringForCSharp(element.Value);

            switch (modifier)
            {
                case "?":
                    WriteLine($"var {varName} = ParseOptional(() => ExpectKeyword(\"{escapedValue}\") ? \"keyword\" : null);");
                    break;
                case "*":
                    WriteLine($"var {varName} = ParseZeroOrMore(() => ExpectKeyword(\"{escapedValue}\") ? \"keyword\" : null);");
                    break;
                case "+":
                    WriteLine($"var {varName} = ParseOneOrMore(() => ExpectKeyword(\"{escapedValue}\") ? \"keyword\" : null);");
                    WriteLine($"if ({varName} == null) return default;");
                    break;
                case "&":
                    WriteLine($"// Positive lookahead for '{element.Value}'");
                    WriteLine($"if (CurrentToken?.Type.ToString() != \"NAME\" || CurrentToken?.Value != \"{escapedValue}\")");
                    WriteLine($"    return default;");
                    WriteLine($"var {varName} = true; // lookahead succeeded");
                    break;
                case "!":
                    WriteLine($"// Negative lookahead for '{element.Value}'");
                    WriteLine($"if (CurrentToken?.Type.ToString() == \"NAME\" && CurrentToken?.Value == \"{escapedValue}\")");
                    WriteLine($"    return default;");
                    WriteLine($"var {varName} = true; // negative lookahead succeeded");
                    break;
                default:
                    WriteLine($"if (!ExpectKeyword(\"{escapedValue}\"))");
                    WriteLine($"    return default;");
                    WriteLine($"var {varName} = \"{escapedValue}\"; // keyword matched");
                    break;
            }
        }

        /// <summary>
        /// Generate token parsing code
        /// </summary>
        private void GenerateTokenParsingCode(PegElement element, string varName)
        {
            var modifier = element.Modifier ?? "";

            switch (modifier)
            {
                case "?":
                    WriteLine($"var {varName} = ParseOptional(() => Expect(\"{element.Value}\") ? CurrentToken?.Value : null);");
                    break;
                case "*":
                    WriteLine($"var {varName} = ParseZeroOrMore(() => {{");
                    WriteLine($"    if (Expect(\"{element.Value}\")) return CurrentToken?.Value;");
                    WriteLine($"    return null;");
                    WriteLine($"}});");
                    break;
                case "+":
                    WriteLine($"var {varName} = ParseOneOrMore(() => {{");
                    WriteLine($"    if (Expect(\"{element.Value}\")) return CurrentToken?.Value;");
                    WriteLine($"    return null;");
                    WriteLine($"}});");
                    WriteLine($"if ({varName} == null || {varName}.Count == 0) return default;");
                    break;
                default:
                    WriteLine($"if (!Expect(\"{element.Value}\"))");
                    WriteLine($"    return default;");
                    WriteLine($"var {varName} = CurrentToken?.Value; // token matched");
                    break;
            }
        }

        /// <summary>
        /// Generate rule parsing code
        /// </summary>
        private void GenerateRuleParsingCode(PegElement element, string varName)
        {
            var modifier = element.Modifier ?? "";
            var ruleName = ToCSharpMethodName(element.Value);

            switch (modifier)
            {
                case "?":
                    WriteLine($"var {varName} = ParseOptional(() => {ruleName}());");
                    break;
                case "*":
                    WriteLine($"var {varName} = ParseZeroOrMore(() => {ruleName}());");
                    break;
                case "+":
                    WriteLine($"var {varName} = ParseOneOrMore(() => {ruleName}());");
                    WriteLine($"if ({varName} == null || {varName}.Count == 0) return default;");
                    break;
                default:
                    WriteLine($"var {varName} = {ruleName}();");
                    WriteLine($"if ({varName} == null) return default;");
                    break;
            }
        }

        /// <summary>
        /// Generate optional parsing code
        /// </summary>
        private void GenerateOptionalParsingCode(PegElement element, string varName)
        {
            WriteLine($"// Optional: {element.Value}");
            WriteLine($"var {varName} = default(object); // placeholder for optional");
        }

        /// <summary>
        /// Generate group parsing code
        /// </summary>
        private void GenerateGroupParsingCode(PegElement element, string varName)
        {
            WriteLine($"// Group: {element.Value}");
            WriteLine($"var {varName} = default(object); // placeholder for group");
        }

        /// <summary>
        /// Generate soft keyword parsing code
        /// </summary>
        private void GenerateSoftKeywordParsingCode(PegElement element, string varName)
        {
            // Soft keywords are treated similarly to keywords
            GenerateKeywordParsingCode(element, varName);
        }

        /// <summary>
        /// Generate AST creation code
        /// </summary>
        private void GenerateASTCreation(PegAlternative alternative, string returnType, List<string> varNames)
        {
            WriteLine();
            WriteLine("// Create and return AST node");

            // If there's AST action code, try to convert it
            if (!string.IsNullOrEmpty(alternative.Action))
            {
                GenerateASTAction(alternative.Action, returnType, varNames, alternative.Elements);
                return;
            }

            // Fallback to pattern-based generation
            if (returnType.Contains("Stmt"))
            {
                WriteLine($"var result = new {returnType}();");
                var stmtType = GuessStatementTypeFromElements(alternative.Elements);
                WriteLine($"result.StatementType = \"{stmtType}\";");

                // Set the first non-null variable as Value
                if (varNames.Count > 0)
                {
                    WriteLine($"result.Value = {varNames[0]}; // Primary element");
                }

                WriteLine($"return result;");
            }
            else if (returnType.Contains("Expr"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"// TODO: Set expression properties when structure is defined");
                WriteLine($"return result;");
            }
            else
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"// TODO: Set node properties when structure is defined");
                WriteLine($"return result;");
            }
        }

        /// <summary>
        /// Generate C# code from PEG AST action (e.g., { _PyAST_Pass(EXTRA) })
        /// </summary>
        private void GenerateASTAction(string action, string returnType, List<string> varNames, List<PegElement> elements)
        {
            // Remove braces and trim
            var actionCode = action.Trim('{', '}').Trim();

            // Handle common patterns
            if (actionCode.Contains("_PyAST_Pass"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"pass\";");
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_Break"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"break\";");
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_Continue"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"continue\";");
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_Constant") && actionCode.Contains("Py_True"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"// True constant");
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_Constant") && actionCode.Contains("Py_False"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"// False constant");
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_Constant") && actionCode.Contains("Py_None"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"// None constant");
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyPegen_make_module"))
            {
                WriteLine($"var result = new {returnType}();");
                if (varNames.Count > 0)
                {
                    WriteLine($"result.Body = {varNames[0]}; // statements");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyPegen_seq_flatten"))
            {
                WriteLine($"var result = new List<GeneratedStmt>();");
                if (varNames.Count > 0)
                {
                    WriteLine($"// CPython 3.12 compatible _PyPegen_seq_flatten: only flatten statement sequences, not compound statement bodies");
                    WriteLine($"foreach (var item in {varNames[0]})");
                    WriteLine($"{{");
                    WriteLine($"    if (item is List<GeneratedStmt> stmtSeq)");
                    WriteLine($"    {{");
                    WriteLine($"        // Only flatten if this is a statement sequence (simple_stmts), not a compound statement body");
                    WriteLine($"        result.AddRange(stmtSeq);");
                    WriteLine($"    }}");
                    WriteLine($"    else if (item is GeneratedStmt stmt)");
                    WriteLine($"    {{");
                    WriteLine($"        result.Add(stmt);");
                    WriteLine($"    }}");
                    WriteLine($"}}");
                }
                WriteLine($"return (GeneratedStmtSeq)result;");
            }
            else if (actionCode.Contains("_PyPegen_singleton_seq"))
            {
                WriteLine($"var result = new GeneratedStmtSeq();");
                if (varNames.Count > 0)
                {
                    WriteLine($"result.Add({varNames[0]}); // single statement");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_Name"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"// NAME expression");
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyPegen_number_token"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"// NUMBER expression");
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_Assign"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"assignment\";");
                if (varNames.Count >= 2)
                {
                    WriteLine($"result.Value = new {{ target = {varNames[0]}, value = {varNames[1]} }};");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_AugAssign"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"aug_assign\";");
                if (varNames.Count >= 3)
                {
                    WriteLine($"result.Value = new {{ target = {varNames[0]}, op = {varNames[1]}, value = {varNames[2]} }};");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_Expr"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"expression\";");
                if (varNames.Count > 0)
                {
                    WriteLine($"result.Value = {varNames[0]};");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_While"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"while\";");
                if (varNames.Count >= 2)
                {
                    WriteLine($"result.Value = new {{ condition = {varNames[0]}, body = {varNames[1]}, elseBody = {(varNames.Count > 2 ? varNames[2] : "null")} }};");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_For"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"for\";");
                if (varNames.Count >= 3)
                {
                    WriteLine($"result.Value = new {{ target = {varNames[0]}, iter = {varNames[1]}, body = {varNames[2]}, elseBody = {(varNames.Count > 3 ? varNames[3] : "null")} }};");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_If"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"if\";");
                if (varNames.Count >= 2)
                {
                    WriteLine($"result.Value = new {{ condition = {varNames[0]}, body = {varNames[1]}, elifs = new List<object>(), elseBody = {(varNames.Count > 2 ? varNames[2] : "null")} }};");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_Return"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"return\";");
                if (varNames.Count > 0)
                {
                    WriteLine($"result.Value = {varNames[0]};");
                }
                else
                {
                    WriteLine($"result.Value = null; // bare return");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_FunctionDef"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"function_def\";");
                if (varNames.Count >= 3)
                {
                    WriteLine($"result.Value = new {{ name = {varNames[0]}, args = {varNames[1]}, body = {varNames[2]} }};");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_ClassDef"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"class_def\";");
                if (varNames.Count >= 2)
                {
                    WriteLine($"result.Value = new {{ name = {varNames[0]}, body = {varNames[1]} }};");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_Import") || actionCode.Contains("_PyAST_ImportFrom"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"import\";");
                if (varNames.Count > 0)
                {
                    WriteLine($"result.Value = {varNames[0]};");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_Global"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"global\";");
                if (varNames.Count > 0)
                {
                    WriteLine($"result.Value = {varNames[0]};");
                }
                WriteLine($"return result;");
            }
            else if (actionCode.Contains("_PyAST_Nonlocal"))
            {
                WriteLine($"var result = new {returnType}();");
                WriteLine($"result.StatementType = \"nonlocal\";");
                if (varNames.Count > 0)
                {
                    WriteLine($"result.Value = {varNames[0]};");
                }
                WriteLine($"return result;");
            }
            else
            {
                WriteLine($"// AST action: {actionCode}");
                WriteLine($"// Phase 1: Minimal implementation");
                WriteLine($"return default({returnType});");
            }
        }

        /// <summary>
        /// Guess statement type from parsed elements
        /// </summary>
        private string GuessStatementTypeFromElements(List<PegElement> elements)
        {
            foreach (var element in elements)
            {
                if (element.Type == "keyword")
                {
                    switch (element.Value)
                    {
                        case "if": return "if";
                        case "while": return "while";
                        case "for": return "for";
                        case "def": return "function_def";
                        case "class": return "class_def";
                        case "try": return "try";
                        case "pass": return "pass";
                        case "break": return "break";
                        case "continue": return "continue";
                        case "return": return "return";
                    }
                }
            }
            return "expression"; // default
        }

        /// <summary>
        /// Generate code for a single element
        /// </summary>
        private void GenerateElement(PegElement element, string varName, bool isLast)
        {
            switch (element.Type)
            {
                case "keyword":
                    GenerateKeywordElement(element, varName);
                    break;
                case "soft_keyword":
                    GenerateSoftKeywordElement(element, varName);
                    break;
                case "token":
                    GenerateTokenElement(element, varName);
                    break;
                case "rule":
                    GenerateRuleElement(element, varName);
                    break;
                default:
                    WriteLine($"// Unknown element type: {element.Type}");
                    break;
            }
        }

        /// <summary>
        /// Generate code for keyword element
        /// </summary>
        private void GenerateKeywordElement(PegElement element, string varName)
        {
            var modifier = element.Modifier ?? "";
            var escapedValue = EscapeStringForCSharp(element.Value);

            switch (modifier)
            {
                case "?":
                    WriteLine($"var {varName} = ParseOptional(() => ExpectKeyword(\"{escapedValue}\") ? \"keyword\" : null);");
                    break;
                case "*":
                    WriteLine($"var {varName} = ParseZeroOrMore(() => ExpectKeyword(\"{escapedValue}\") ? \"keyword\" : null);");
                    break;
                case "+":
                    WriteLine($"var {varName} = ParseOneOrMore(() => ExpectKeyword(\"{escapedValue}\") ? \"keyword\" : null);");
                    break;
                case "&":
                    WriteLine($"// Positive lookahead for '{element.Value}'");
                    WriteLine($"if (CurrentToken?.Type.ToString() != \"NAME\" || CurrentToken?.Value != \"{escapedValue}\")");
                    WriteLine($"    return default;");
                    break;
                case "!":
                    WriteLine($"// Negative lookahead for '{element.Value}'");
                    WriteLine($"if (CurrentToken?.Type.ToString() == \"NAME\" && CurrentToken?.Value == \"{escapedValue}\")");
                    WriteLine($"    return default;");
                    break;
                default:
                    WriteLine($"if (!ExpectKeyword(\"{escapedValue}\"))");
                    WriteLine($"    return default;");
                    break;
            }
        }

        /// <summary>
        /// Escape string for C# code generation
        /// </summary>
        private string EscapeStringForCSharp(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "";

            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        /// <summary>
        /// Generate code for soft keyword element
        /// </summary>
        private void GenerateSoftKeywordElement(PegElement element, string varName)
        {
            // Soft keywords are treated similarly to keywords but may not be reserved
            GenerateKeywordElement(element, varName);
        }

        /// <summary>
        /// Generate code for token element
        /// </summary>
        private void GenerateTokenElement(PegElement element, string varName)
        {
            var modifier = element.Modifier ?? "";

            switch (modifier)
            {
                case "?":
                    WriteLine($"var {varName} = ParseOptional(() => Expect(\"{element.Value}\") ? CurrentToken?.Value : null);");
                    break;
                case "*":
                    WriteLine($"var {varName} = ParseZeroOrMore(() => Expect(\"{element.Value}\") ? CurrentToken?.Value : null);");
                    break;
                case "+":
                    WriteLine($"var {varName} = ParseOneOrMore(() => Expect(\"{element.Value}\") ? CurrentToken?.Value : null);");
                    break;
                default:
                    WriteLine($"if (!Expect(\"{element.Value}\"))");
                    WriteLine($"    return default;");
                    break;
            }
        }

        /// <summary>
        /// Generate code for rule element
        /// </summary>
        private void GenerateRuleElement(PegElement element, string varName)
        {
            var modifier = element.Modifier ?? "";
            var ruleName = ToCSharpMethodName(element.Value);

            switch (modifier)
            {
                case "?":
                    WriteLine($"var {varName} = ParseOptional(() => {ruleName}());");
                    break;
                case "*":
                    WriteLine($"var {varName} = ParseZeroOrMore(() => {ruleName}());");
                    break;
                case "+":
                    WriteLine($"var {varName} = ParseOneOrMore(() => {ruleName}());");
                    break;
                default:
                    WriteLine($"var {varName} = {ruleName}();");
                    WriteLine($"if ({varName} == null)");
                    WriteLine($"    return default;");
                    break;
            }
        }

        /// <summary>
        /// Guess statement type from alternative elements
        /// </summary>
        private string GuessStatementType(PegAlternative alternative)
        {
            // Look for keywords that indicate statement type
            foreach (var element in alternative.Elements)
            {
                if (element.Type == "keyword")
                {
                    switch (element.Value)
                    {
                        case "if": return "if";
                        case "while": return "while";
                        case "for": return "for";
                        case "def": return "function_def";
                        case "class": return "class_def";
                        case "try": return "try";
                        case "pass": return "pass";
                        case "break": return "break";
                        case "continue": return "continue";
                        case "return": return "return";
                        case "global": return "global";
                        case "nonlocal": return "nonlocal";
                        case "del": return "del";
                        case "import": return "import";
                        case "from": return "from_import";
                        case "raise": return "raise";
                        case "assert": return "assert";
                        case "yield": return "yield";
                        case "with": return "with";
                        case "match": return "match";
                    }
                }
            }
            return "expression";
        }

        /// <summary>
        /// Translate PEG type to C# type
        /// </summary>
        public string TranslatePegTypeToCS(string pegType)
        {
            return pegType switch
            {
                // AST types (from ASDL) - CPython 3.12: All non-nullable at declaration, null checks at usage
                "mod_ty" => "GeneratedMod",
                "stmt_ty" => "GeneratedStmt",
                "expr_ty" => "GeneratedExpr",
                "pattern_ty" => "GeneratedPattern",
                "type_param_ty" => "GeneratedTypeParam",
                "comprehension_ty" => "GeneratedComprehension",
                "excepthandler_ty" => "GeneratedExcepthandler",
                "match_case_ty" => "GeneratedMatchCase",
                "alias_ty" => "GeneratedAlias",
                "withitem_ty" => "GeneratedWithitem",
                "arg_ty" => "GeneratedArg",
                "arguments_ty" => "GeneratedArguments",

                // Sequence types (from ASDL) - CPython 3.12: asdl_seq* is non-nullable pointer
                "asdl_stmt_seq*" => "GeneratedStmtSeq",
                "asdl_expr_seq*" => "GeneratedExprSeq",
                "asdl_identifier_seq*" => "GeneratedIdentifierSeq",
                "asdl_alias_seq*" => "GeneratedAliasSeq",
                "asdl_arg_seq*" => "GeneratedArgSeq",
                "asdl_comprehension_seq*" => "GeneratedComprehensionSeq",
                "asdl_excepthandler_seq*" => "GeneratedExcepthandlerSeq",
                "asdl_match_case_seq*" => "GeneratedMatchCaseSeq",
                "asdl_pattern_seq*" => "GeneratedPatternSeq",
                "asdl_type_param_seq*" => "GeneratedTypeParamSeq",
                "asdl_withitem_seq*" => "GeneratedWithitemSeq",
                "asdl_seq*" => "GeneratedSeq",  // Generic sequence (CPython: void* seq)

                // Special parser types (not in ASDL, defined in parser)
                "KeyValuePair*" => "GeneratedKeyValuePair",
                "CmpopExprPair*" => "GeneratedCmpopExprPair",
                "KeyPatternPair*" => "GeneratedKeyPatternPair",
                "KeywordOrStarred*" => "GeneratedKeywordOrStarred",
                "NameDefaultPair*" => "GeneratedNameDefaultPair",
                "SlashWithDefault*" => "GeneratedSlashWithDefault",
                "StarEtc*" => "GeneratedStarEtc",
                "ResultTokenWithMetadata*" => "GeneratedResultTokenWithMetadata",

                // Operators and tokens
                "AugOperator*" => "GeneratedOperator",
                "Token*" => "GeneratedTokenInfo",

                // String types
                "func_type_comment" => "string",
                "return_type" => "string",

                _ => "GeneratedPtr"  // CPython 3.12: void* equivalent for untyped rules
            };
        }

        /// <summary>
        /// Check if a rule has left recursion (first alternative starts with the rule itself)
        /// CPython 3.12 PEG: bitwise_or: bitwise_or '|' bitwise_xor | bitwise_xor
        /// </summary>
        private bool IsLeftRecursive(Rule rule)
        {
            if (rule.Alternatives == null || rule.Alternatives.Count == 0)
                return false;

            // Check first alternative
            var firstAlt = rule.Alternatives[0];
            if (firstAlt.Items == null || firstAlt.Items.Count == 0)
                return false;

            // Get first item
            var firstItem = firstAlt.Items[0];

            // Check if it's an Item with an Atom pointing to the same rule
            if (firstItem.Atom != null)
            {
                // Check if Atom is a RuleRef pointing to the same rule
                if (firstItem.Atom is RuleRef ruleRef)
                {
                    // Compare rule names (case-insensitive)
                    return string.Equals(ruleRef.Name, rule.Name, StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        private void GenerateRuleMethod(Rule rule)
        {
            Console.WriteLine($"[DEBUG] GenerateRuleMethod called for rule: '{rule.Name}'");
            var returnType = GetRuleReturnType(rule);
            var methodName = ToCSharpMethodName(rule.Name);

            WriteLine($"// Rule: {rule.Name} from python.gram");

            // CPython 3.12: Check if this is a *_without_invalid rule
            bool isWithoutInvalid = rule.Name.EndsWith("_without_invalid", StringComparison.OrdinalIgnoreCase);

            // CPython 3.12: c_generator.py:663-688 - Check for left_recursive AND leader
            bool isLeftRecursiveLeader = rule.IsLeftRecursive && rule.IsLeader;

            if (isLeftRecursiveLeader)
            {
                // CPython: Generate wrapper + _raw pattern for left-recursive leaders
                Console.WriteLine($"[CODEGEN] Rule '{rule.Name}' is left-recursive leader - generating wrapper + raw method");
                GenerateLeftRecursiveWrapper(rule, returnType, methodName, isWithoutInvalid);
            }
            else if (rule.IsMemoized)
            {
                // CPython: c_generator.py:575-606 - Simple memoization for non-left-recursive
                Console.WriteLine($"[CODEGEN] Rule '{rule.Name}' is memoized (non-left-recursive)");
                GenerateMemoizedMethod(rule, returnType, methodName, isWithoutInvalid);
            }
            else
            {
                // CPython: c_generator.py:578-606 - No memoization, direct method
                Console.WriteLine($"[CODEGEN] Rule '{rule.Name}' is non-memoized");
                GenerateDirectMethod(rule, returnType, methodName, isWithoutInvalid);
            }
        }

        /// <summary>
        /// CPython: c_generator.py:541-573 _set_up_rule_memoization
        /// Generate left-recursive wrapper with growth loop
        /// </summary>
        private void GenerateLeftRecursiveWrapper(Rule rule, string returnType, string methodName, bool isWithoutInvalid)
        {
            // CPython: public wrapper - {name}_rule
            WriteLine($"public {returnType} {methodName}()");
            WriteLine("{");
            Indent();

            WriteLine($"// CPython 3.12: Left-recursive leader - growth loop pattern");
            WriteLine($"{returnType} _res = null;");

            // CPython: if (_PyPegen_is_memoized(p, {node.name}_type, &_res))
            WriteLine($"if (TryGetMemoized(\"{methodName}\", out var _memoized))");
            WriteLine("{");
            Indent();
            WriteLine($"return ({returnType})_memoized;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine($"int _mark = _position;");
            WriteLine($"int _resmark = _position;");
            WriteLine($"int _iteration = 0;");
            WriteLine();

            // CPython: while (1) loop
            WriteLine($"while (true)");
            WriteLine("{");
            Indent();

            WriteLine($"_iteration++;");
            WriteLine($"#if DEBUG_PARSE_LOG");
            WriteLine($"Console.WriteLine($\"[GROWTH-LOOP] {methodName} iteration {{_iteration}}: _mark={{_mark}}, _resmark={{_resmark}}, _res={{(_res == null ? \"null\" : \"non-null\")}}\");");
            WriteLine($"#endif");
            WriteLine();

            // CPython: _PyPegen_update_memo(p, _mark, {node.name}_type, _res)
            WriteLine($"// Update memo with current result");
            WriteLine($"UpdateMemoized(\"{methodName}\", _mark, _res, _resmark);");
            WriteLine();

            // CPython: p->mark = _mark; void *_raw = {node.name}_raw(p);
            WriteLine($"_position = _mark;");
            WriteLine($"#if DEBUG_PARSE_LOG");
            WriteLine($"Console.WriteLine($\"[GROWTH-LOOP] {methodName} calling {methodName}_raw() at pos={{_position}}\");");
            WriteLine($"#endif");
            WriteLine($"var _raw = {methodName}_raw();");
            WriteLine($"#if DEBUG_PARSE_LOG");
            WriteLine($"Console.WriteLine($\"[GROWTH-LOOP] {methodName}_raw() returned: {{(_raw == null ? \"null\" : \"non-null\")}}, _position={{_position}}\");");
            WriteLine($"#endif");
            WriteLine();

            // CPython: if (p->error_indicator) return NULL;
            WriteLine($"if (_pendingSyntaxError != null)");
            WriteLine("{");
            Indent();
            WriteLine($"return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // CPython: if (_raw == NULL || p->mark <= _resmark) break;
            WriteLine($"if (_raw == null || _position <= _resmark)");
            WriteLine("{");
            Indent();
            WriteLine($"#if DEBUG_PARSE_LOG");
            WriteLine($"Console.WriteLine($\"[GROWTH-LOOP] {methodName} terminating: _raw={{(_raw == null ? \"null\" : \"non-null\")}}, _position={{_position}}, _resmark={{_resmark}}\");");
            WriteLine($"#endif");
            WriteLine($"break;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // CPython: _resmark = p->mark; _res = _raw;
            WriteLine($"_resmark = _position;");
            WriteLine($"_res = _raw;");

            Dedent();
            WriteLine("}");
            WriteLine();

            // CPython: p->mark = _resmark; return _res;
            WriteLine($"_position = _resmark;");
            WriteLine($"return _res;");

            Dedent();
            WriteLine("}");
            WriteLine();

            // CPython: static {result_type} {node.name}_raw(Parser *p)
            WriteLine($"private {returnType} {methodName}_raw()");
            WriteLine("{");
            Indent();

            // Generate actual parsing logic
            GenerateRuleBody(rule, returnType, isWithoutInvalid);

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// CPython: c_generator.py:578-606 _handle_default_rule_body with memoization
        /// </summary>
        private void GenerateMemoizedMethod(Rule rule, string returnType, string methodName, bool isWithoutInvalid)
        {
            WriteLine($"public {returnType} {methodName}()");
            WriteLine("{");
            Indent();

            WriteLine($"// CPython 3.12: Memoized (non-left-recursive)");

            // CPython: if (_PyPegen_is_memoized(...))
            WriteLine($"if (TryGetMemoized(\"{methodName}\", out var _memoized))");
            WriteLine("{");
            Indent();
            WriteLine($"return ({returnType})_memoized;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Store mark for memo update
            WriteLine($"int _mark = _position;");
            WriteLine();

            // Generate body
            GenerateRuleBody(rule, returnType, isWithoutInvalid, isMemoized: true, methodName: methodName);

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// CPython: c_generator.py:578-606 _handle_default_rule_body without memoization
        /// </summary>
        private void GenerateDirectMethod(Rule rule, string returnType, string methodName, bool isWithoutInvalid)
        {
            WriteLine($"public {returnType} {methodName}()");
            WriteLine("{");
            Indent();

            GenerateRuleBody(rule, returnType, isWithoutInvalid);

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate actual rule parsing body (common to all rule types)
        /// CPython: c_generator.py:578-606 _handle_default_rule_body
        /// </summary>
        private void GenerateRuleBody(Rule rule, string returnType, bool isWithoutInvalid, bool isMemoized = false, string methodName = "")
        {
            // CPython 3.12 style: Direct parsing logic generation
            WriteLine($"// CPython 3.12 PEG: {rule.Name}");

            // For memoized functions, _mark is already declared in the wrapper
            if (!isMemoized)
            {
                WriteLine($"int _mark = _position;");
            }

            WriteLine($"{returnType} _res = null;");
            WriteLine();

            // CPython 3.12: Set call_invalid_rules flag for *_without_invalid rules
            if (isWithoutInvalid)
            {
                WriteLine($"// CPython 3.12: Disable invalid rules for {rule.Name}");
                WriteLine($"bool _prev_call_invalid = _callInvalidRules;");
                WriteLine($"_callInvalidRules = false;");
                WriteLine();
            }

            // CPython 3.12 EXTRA: Track position information for AST nodes
            WriteLine($"// Position tracking for EXTRA parameters");
            WriteLine($"var _start_token = CurrentToken;");
            WriteLine($"int _start_lineno = _start_token?.Line ?? 0;");
            WriteLine($"int _start_col_offset = _start_token?.Column ?? 0;");
            WriteLine($"int _end_lineno = 0;");
            WriteLine($"int _end_col_offset = 0;");
            WriteLine();

            // Add debug logging for Expression and SimpleStmt
            if (rule.Name.Equals("expression", StringComparison.OrdinalIgnoreCase) ||
                rule.Name.Equals("simple_stmt", StringComparison.OrdinalIgnoreCase))
            {
                WriteLine("#if DEBUG_PARSE_LOG");
                WriteLine($"Console.WriteLine($\"[{rule.Name.ToUpper()}] START at pos={{_position}}, token={{CurrentToken?.Type}}:'{{CurrentToken?.Value}}'\");");
                WriteLine("#endif");
                WriteLine();
            }

            // CPython 3.12: Generate alternatives with do-while(false) + break pattern
            for (int i = 0; i < rule.Alternatives.Count; i++)
            {
                var alt = rule.Alternatives[i];
                var altGen = new AlternativeCodeGenerator(this, rule, alt, i);
                altGen.Generate();
            }

            // All alternatives failed
            WriteLine("_position = _mark;");
            WriteLine("_res = null;");
            WriteLine();

            // Done label
            WriteLine("done:");
            WriteLine("// CPython 3.12: Simply return result, don't manipulate error indicator");

            // CPython 3.12: Restore call_invalid_rules flag for *_without_invalid rules
            if (isWithoutInvalid)
            {
                WriteLine($"_callInvalidRules = _prev_call_invalid;");
            }

            WriteLine("// Update end position before returning");
            WriteLine("var _end_token = CurrentToken ?? _start_token;");
            WriteLine("_end_lineno = _end_token?.Line ?? _start_lineno;");
            WriteLine("_end_col_offset = _end_token?.Column ?? _start_col_offset;");

            // Add debug logging for Expression and SimpleStmt before return
            if (rule.Name.Equals("expression", StringComparison.OrdinalIgnoreCase) ||
                rule.Name.Equals("simple_stmt", StringComparison.OrdinalIgnoreCase))
            {
                WriteLine("#if DEBUG_PARSE_LOG");
                WriteLine($"Console.WriteLine($\"[{rule.Name.ToUpper()}] RETURN {{(_res == null ? \"null\" : \"not-null\")}} at pos={{_position}}\");");
                WriteLine("#endif");
            }

            // CPython 3.12: Insert memo before returning (for memoized functions)
            if (isMemoized)
            {
                WriteLine($"// CPython: _PyPegen_insert_memo(p, _mark, {methodName}_type, _res)");
                WriteLine($"UpdateMemoized(\"{methodName}\", _mark, _res, _position);");
            }

            WriteLine("return _res;");
        }

        /// <summary>
        /// Generate code for a Grammar.Alternative (not PegAlternative)
        /// </summary>
        private void GenerateGrammarAlternative(Alternative alt, string returnType)
        {
            var itemsString = string.Join(" ", alt.Items.Select(i => i.ToString()));
            WriteLine($"// Parse alternative: {itemsString}");
            WriteLine("result = TryParseGrammarItems(new[] { " + string.Join(", ", alt.Items.Select(i => $"\"{i.ToString()?.Replace("\"", "\\\"")}\"")) + " });");
        }

        private void GenerateFileMethod(string returnType)
        {
            // file[mod_ty]: a=[statements] ENDMARKER { _PyPegen_make_module(p, a) }
            WriteLine("// file[mod_ty]: a=[statements] ENDMARKER { _PyPegen_make_module(p, a) }");
            WriteLine("var statements = Statements(); // Parse optional statements");
            WriteLine();
            WriteLine("if (!Expect(\"ENDMARKER\"))");
            WriteLine("{");
            Indent();
            WriteLine("// If no ENDMARKER, we're not at end of file - this is an error for complete parsing");
            WriteLine("// For now, continue gracefully");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Create a module containing the statements");
            WriteLine($"var module = new {returnType}();");
            WriteLine("module.Body = statements;");
            WriteLine("return module;");
        }

        private void GenerateStatementsMethod(string returnType)
        {
            // statements[asdl_stmt_seq*]: a=statement+ { (asdl_stmt_seq*)_PyPegen_seq_flatten(p, a) }
            WriteLine("// statements[asdl_stmt_seq*]: a=statement+ { (asdl_stmt_seq*)_PyPegen_seq_flatten(p, a) }");
            WriteLine("var statementList = new List<GeneratedStmt>();");
            WriteLine();
            WriteLine("// Parse one or more statements");
            WriteLine("while (_position < _tokens.Count && CurrentToken?.Type.ToString() != \"ENDMARKER\")");
            WriteLine("{");
            Indent();
            WriteLine("var stmtSeq = Statement(); // Returns GeneratedStmtSeq");
            WriteLine("if (stmtSeq != null && stmtSeq.Count > 0)");
            WriteLine("{");
            Indent();
            WriteLine("// Add all statements from the sequence");
            WriteLine("statementList.AddRange(stmtSeq);");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("// No more statements to parse");
            WriteLine("break;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Convert to statement sequence");
            WriteLine($"var result = new {returnType}();");
            WriteLine("result.AddRange(statementList);");
            WriteLine("return result;");
        }

        private void GeneratePegStmtMethod(string returnType)
        {
            // simple_stmt[stmt_ty] with multiple alternatives including 'pass', 'break', 'continue'
            WriteLine("// simple_stmt[stmt_ty]: Multiple alternatives including simple keyword statements");
            WriteLine();
            WriteLine("// Try 'pass' keyword (simplest case)");
            WriteLine("if (ExpectKeyword(\"pass\"))");
            WriteLine("{");
            Indent();
            WriteLine("// 'pass' { _PyAST_Pass(EXTRA) }");
            WriteLine($"var passStmt = new {returnType}();");
            WriteLine("passStmt.StatementType = \"pass\";");
            WriteLine("return passStmt;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'break' keyword");
            WriteLine("if (ExpectKeyword(\"break\"))");
            WriteLine("{");
            Indent();
            WriteLine("// 'break' { _PyAST_Break(EXTRA) }");
            WriteLine($"var breakStmt = new {returnType}();");
            WriteLine("breakStmt.StatementType = \"break\";");
            WriteLine("return breakStmt;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'continue' keyword");
            WriteLine("if (ExpectKeyword(\"continue\"))");
            WriteLine("{");
            Indent();
            WriteLine("// 'continue' { _PyAST_Continue(EXTRA) }");
            WriteLine($"var continueStmt = new {returnType}();");
            WriteLine("continueStmt.StatementType = \"continue\";");
            WriteLine("return continueStmt;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try assignment statement (name = expression)");
            WriteLine("var savedPos = _position;");
            WriteLine("var nameToken = CurrentToken;");
            WriteLine("if (nameToken != null && nameToken.Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume name");
            WriteLine("if (CurrentToken?.Type == TokenType.EQUAL)");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '='");
            WriteLine("// For now, expect a NUMBER token for the value");
            WriteLine("var valueToken = CurrentToken;");
            WriteLine("if (valueToken?.Type == TokenType.NUMBER)");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume number");
            WriteLine($"var assignStmt = new {returnType}();");
            WriteLine("assignStmt.StatementType = \"assignment\";");
            WriteLine("assignStmt.Value = new { Target = nameToken.Value, Value = valueToken.Value };");
            WriteLine("return assignStmt;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("_position = savedPos; // backtrack");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'return' statement");
            WriteLine("if (ExpectKeyword(\"return\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Check if there's a value after return");
            WriteLine("object? returnValue = null;");
            WriteLine("if (CurrentToken?.Type == TokenType.NUMBER)");
            WriteLine("{");
            Indent();
            WriteLine("returnValue = CurrentToken.Value;");
            WriteLine("Advance(); // consume number");
            Dedent();
            WriteLine("}");
            WriteLine($"var returnStmt = new {returnType}();");
            WriteLine("returnStmt.StatementType = \"return\";");
            WriteLine("returnStmt.Value = returnValue;");
            WriteLine("return returnStmt;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try simple expression statement (NUMBER)");
            WriteLine("if (CurrentToken?.Type == TokenType.NUMBER)");
            WriteLine("{");
            Indent();
            WriteLine("var numberToken = CurrentToken;");
            WriteLine("Advance(); // consume number");
            WriteLine($"var exprStmt = new {returnType}();");
            WriteLine("exprStmt.StatementType = \"expression\";");
            WriteLine("exprStmt.Value = numberToken.Value;");
            WriteLine("return exprStmt;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'global' statement");
            WriteLine("if (ExpectKeyword(\"global\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Expect one or more NAME tokens separated by commas");
            WriteLine("var names = new List<string>();");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("names.Add(CurrentToken.Value);");
            WriteLine("Advance(); // consume first name");
            WriteLine("// Handle comma-separated additional names");
            WriteLine("while (CurrentToken?.Type == TokenType.COMMA)");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume comma");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("names.Add(CurrentToken.Value);");
            WriteLine("Advance(); // consume name");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine($"var globalStmt = new {returnType}();");
            WriteLine("globalStmt.StatementType = \"global\";");
            WriteLine("globalStmt.Value = names.ToArray();");
            WriteLine("return globalStmt;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'nonlocal' statement");
            WriteLine("if (ExpectKeyword(\"nonlocal\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Expect one or more NAME tokens separated by commas");
            WriteLine("var names = new List<string>();");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("names.Add(CurrentToken.Value);");
            WriteLine("Advance(); // consume first name");
            WriteLine("// Handle comma-separated additional names");
            WriteLine("while (CurrentToken?.Type == TokenType.COMMA)");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume comma");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("names.Add(CurrentToken.Value);");
            WriteLine("Advance(); // consume name");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine($"var nonlocalStmt = new {returnType}();");
            WriteLine("nonlocalStmt.StatementType = \"nonlocal\";");
            WriteLine("nonlocalStmt.Value = names.ToArray();");
            WriteLine("return nonlocalStmt;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'del' statement");
            WriteLine("if (ExpectKeyword(\"del\"))");
            WriteLine("{");
            Indent();
            WriteLine("// For now, expect a simple NAME token");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("var targetName = CurrentToken.Value;");
            WriteLine("Advance(); // consume name");
            WriteLine($"var delStmt = new {returnType}();");
            WriteLine("delStmt.StatementType = \"del\";");
            WriteLine("delStmt.Value = targetName;");
            WriteLine("return delStmt;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'import' statement");
            WriteLine("if (ExpectKeyword(\"import\"))");
            WriteLine("{");
            Indent();
            WriteLine("// For now, expect a simple module name");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("var moduleName = CurrentToken.Value;");
            WriteLine("Advance(); // consume module name");
            WriteLine($"var importStmt = new {returnType}();");
            WriteLine("importStmt.StatementType = \"import\";");
            WriteLine("importStmt.Value = new { Module = moduleName, Alias = (string?)null };");
            WriteLine("return importStmt;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'from' import statement");
            WriteLine("if (ExpectKeyword(\"from\"))");
            WriteLine("{");
            Indent();
            WriteLine("// For now, expect 'from module import name'");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("var moduleName = CurrentToken.Value;");
            WriteLine("Advance(); // consume module name");
            WriteLine("if (ExpectKeyword(\"import\"))");
            WriteLine("{");
            Indent();
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("var importName = CurrentToken.Value;");
            WriteLine("Advance(); // consume import name");
            WriteLine($"var fromImportStmt = new {returnType}();");
            WriteLine("fromImportStmt.StatementType = \"from_import\";");
            WriteLine("fromImportStmt.Value = new { Module = moduleName, Name = importName };");
            WriteLine("return fromImportStmt;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for import statements");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"import\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParsePegStmt: Found import statement, calling ParseImportStatement\");");
            WriteLine("    var result = ParseImportStatement();");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParsePegStmt: ParseImportStatement returned: {result}\");");
            WriteLine("    return result;");
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for from-import statements");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"from\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParsePegStmt: Found from-import statement, calling ParseFromImportStatement\");");
            WriteLine("    var result = ParseFromImportStatement();");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParsePegStmt: ParseFromImportStatement returned: {result}\");");
            WriteLine("    return result;");
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for raise statements");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"raise\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParsePegStmt: Found raise statement, calling ParseRaiseStatement\");");
            WriteLine("    var result = ParseRaiseStatement();");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParsePegStmt: ParseRaiseStatement returned: {result}\");");
            WriteLine("    return result;");
            WriteLine("}");
            WriteLine();
            WriteLine("// TODO: Add other simple statement alternatives");
            WriteLine("// - type_alias, star_expressions, yield_stmt, assert_stmt");
            WriteLine("// - function calls, complex expressions");
            WriteLine();
            WriteLine("// No match found");
            WriteLine($"return default({returnType});");
        }

        private void GenerateAwaitPrimaryMethod(string returnType)
        {
            Console.WriteLine($"[DEBUG] GenerateAwaitPrimaryMethod called with returnType: {returnType}");
            WriteLine("// await_primary[expr_ty] (memo): AWAIT a=primary | primary");
            WriteLine("// Generated method for await_primary rule");
            WriteLine($"public {returnType} AwaitPrimary()");
            WriteLine("{");
            Indent();
            WriteLine();

            WriteLine("// Check for AWAIT token");
            WriteLine("if (CurrentToken?.Type.ToString() == \"AWAIT\" && CurrentToken?.Value == \"await\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume 'await'");
            WriteLine("var primary = ParsePrimary();");
            WriteLine("if (primary != null)");
            WriteLine("{");
            Indent();
            WriteLine("return _PyAST_Await(primary);");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("throw new InvalidOperationException(\"Expected primary expression after 'await'\");");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("// Not an await expression, delegate to primary");
            WriteLine("return ParsePrimary();");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
        }

        private void GenerateAugAssignMethod(string returnType)
        {
            Console.WriteLine($"[DEBUG] GenerateAugAssignMethod called with returnType: {returnType}");
            WriteLine("// augassign[AugOperator*]: '+=' | '-=' | '*=' | '@=' | '/=' | '%=' | '&=' | '|=' | '^=' | '<<=' | '>>=' | '**=' | '//='");
            WriteLine("// Generated method for augassign rule");
            WriteLine($"public {returnType} Augassign()");
            WriteLine("{");
            Indent();
            WriteLine();

            WriteLine("if (CurrentToken?.Type == TokenType.OP)");
            WriteLine("{");
            Indent();
            WriteLine("var op = CurrentToken.Value;");
            WriteLine("switch (op)");
            WriteLine("{");
            Indent();
            WriteLine("case \"+=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"Add\");");
            Dedent();
            WriteLine("case \"-=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"Sub\");");
            Dedent();
            WriteLine("case \"*=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"Mult\");");
            Dedent();
            WriteLine("case \"@=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"MatMult\");");
            Dedent();
            WriteLine("case \"/=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"Div\");");
            Dedent();
            WriteLine("case \"%=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"Mod\");");
            Dedent();
            WriteLine("case \"&=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"BitAnd\");");
            Dedent();
            WriteLine("case \"|=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"BitOr\");");
            Dedent();
            WriteLine("case \"^=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"BitXor\");");
            Dedent();
            WriteLine("case \"<<=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"LShift\");");
            Dedent();
            WriteLine("case \">>=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"RShift\");");
            Dedent();
            WriteLine("case \"**=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"Pow\");");
            Dedent();
            WriteLine("case \"//=\":");
            Indent();
            WriteLine("Advance();");
            WriteLine("return CreateAugOperator(\"FloorDiv\");");
            Dedent();
            WriteLine("default:");
            Indent();
            WriteLine("return null;");
            Dedent();
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
        }

        private void GenerateAtomMethod(string returnType)
        {
            // atom[expr_ty]: NAME | 'True' | 'False' | 'None' | strings | NUMBER | ...
            WriteLine("// atom[expr_ty]: Basic expressions (NAME, NUMBER, True/False/None, STRING)");
            WriteLine();
            WriteLine("// Try NAME token (variable names)");
            WriteLine("// CPython 3.12: NAME tokens cannot be keywords");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NAME\")");
            WriteLine("{");
            Indent();
            WriteLine("var nameValue = CurrentToken.Value;");
            WriteLine();
            WriteLine("// CPython 3.12: Keywords cannot be used as identifiers");
            WriteLine("if (IsKeyword(nameValue))");
            WriteLine("{");
            Indent();
            WriteLine("// Keyword found - this is not a valid NAME in this context");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("Advance();");
            WriteLine($"var nameExpr = new {returnType}();");
            WriteLine("// TODO: Set name expression properties when structure is defined");
            WriteLine("return nameExpr;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try NUMBER token");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NUMBER\")");
            WriteLine("{");
            Indent();
            WriteLine("var numberValue = CurrentToken.Value;");
            WriteLine("Advance();");
            WriteLine($"var numberExpr = new {returnType}();");
            WriteLine("// TODO: Set number expression properties when structure is defined");
            WriteLine("return numberExpr;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try STRING token");
            WriteLine("if (CurrentToken?.Type.ToString() == \"STRING\")");
            WriteLine("{");
            Indent();
            WriteLine("var stringValue = CurrentToken.Value;");
            WriteLine("Advance();");
            WriteLine($"var stringExpr = new {returnType}();");
            WriteLine("// TODO: Set string expression properties when structure is defined");
            WriteLine("return stringExpr;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try literal keywords");
            WriteLine("if (ExpectKeyword(\"True\"))");
            WriteLine("{");
            Indent();
            WriteLine("// 'True' { _PyAST_Constant(Py_True, NULL, EXTRA) }");
            WriteLine($"var trueExpr = new {returnType}();");
            WriteLine("// TODO: Set True constant properties when structure is defined");
            WriteLine("return trueExpr;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("if (ExpectKeyword(\"False\"))");
            WriteLine("{");
            Indent();
            WriteLine("// 'False' { _PyAST_Constant(Py_False, NULL, EXTRA) }");
            WriteLine($"var falseExpr = new {returnType}();");
            WriteLine("// TODO: Set False constant properties when structure is defined");
            WriteLine("return falseExpr;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("if (ExpectKeyword(\"None\"))");
            WriteLine("{");
            Indent();
            WriteLine("// 'None' { _PyAST_Constant(Py_None, NULL, EXTRA) }");
            WriteLine($"var noneExpr = new {returnType}();");
            WriteLine("// TODO: Set None constant properties when structure is defined");
            WriteLine("return noneExpr;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// TODO: Add other atom alternatives");
            WriteLine("// - strings (complex string handling)");
            WriteLine("// - tuple, list, dict literals");
            WriteLine("// - '...' (Ellipsis)");
            WriteLine();
            WriteLine("// No match found");
            WriteLine($"return default({returnType});");
        }

        private void GenerateStatementMethod(string returnType)
        {
            // statement[asdl_stmt_seq*]: a=compound_stmt { (asdl_stmt_seq*)_PyPegen_singleton_seq(p, a) } | simple_stmts
            WriteLine("// statement[asdl_stmt_seq*]: compound_stmt | simple_stmts");
            WriteLine();
            WriteLine("// Try compound_stmt first");
            WriteLine("var compoundStmt = CompoundStmt();");
            WriteLine("if (compoundStmt != null)");
            WriteLine("{");
            Indent();
            WriteLine("// Convert single compound statement to statement sequence");
            WriteLine($"var stmtSeq = new {returnType}();");
            WriteLine("stmtSeq.Add(compoundStmt);");
            WriteLine("return stmtSeq;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try simple_stmts");
            WriteLine("var simpleStmts = PegStmts();");
            WriteLine("if (simpleStmts != null)");
            WriteLine("{");
            Indent();
            WriteLine("return simpleStmts;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// No match found");
            WriteLine($"return default({returnType});");
        }

        private void GeneratePegStmtsMethod(string returnType)
        {
            // simple_stmts[asdl_stmt_seq*]: a=simple_stmt b=newline { (asdl_stmt_seq*)_PyPegen_singleton_seq(p, a) }
            WriteLine("// simple_stmts[asdl_stmt_seq*]: simple_stmt NEWLINE | simple_stmt $$");
            WriteLine();
            WriteLine("var stmt = PegStmt();");
            WriteLine("if (stmt != null)");
            WriteLine("{");
            Indent();
            WriteLine("// Skip optional newline");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NEWLINE\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Create statement sequence with single statement");
            WriteLine($"var stmtSeq = new {returnType}();");
            WriteLine("stmtSeq.Add(stmt);");
            WriteLine("return stmtSeq;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// No statement found");
            WriteLine($"return default({returnType});");
        }

        private void GenerateAlternative(Alternative alt, string returnType, string ruleName)
        {
            // Phase 1: Minimal implementation - just return default
            WriteLine($"// Alternative implementation simplified for Phase 1");
            WriteLine($"return default({returnType});");
        }

        private string GenerateAtomCode(Atom atom)
        {
            // Phase 1: Minimal implementation - everything returns null for compilation
            return "null";
        }

        private bool IsToken(string name)
        {
            return _tokens.Any(t => t.Name == name);
        }

        private string ProcessAction(string action, Dictionary<string, string> variables)
        {
            // Simple variable substitution and C++ to C# conversion
            var result = action;

            // Replace variables
            foreach (var (name, varName) in variables)
            {
                result = result.Replace(name, varName);
            }

            // Convert C++ pointer access to C# property access
            result = result.Replace("->lineno", ".Line");
            result = result.Replace("->col_offset", ".Column");

            // Handle common CPython function calls
            result = result.Replace("RAISE_SYNTAX_ERROR", "throw new PySyntaxError");
            result = result.Replace("RAISE_INDENTATION_ERROR", "throw new PyIndentationError");

            return result;
        }

        public string ToCSharpMethodName(string grammarName)
        {
            // Convert grammar rule name to C# method name
            // CPython 3.12: Handle artificial rules like _loop0_N, _gather_N
            var parts = grammarName.Split('_');
            return string.Join("", parts.Select(p =>
                string.IsNullOrEmpty(p) ? "" : char.ToUpper(p[0]) + p.Substring(1).ToLower()));
        }

        public string GetRuleReturnType(Rule rule)
        {
            // Check cache first
            if (_ruleReturnTypeCache.TryGetValue(rule.Name, out string cachedType))
            {
                return cachedType;
            }

            string returnType;

            // CPython 3.12: Use declared type annotation if available
            if (!string.IsNullOrEmpty(rule.ReturnType))
            {
                returnType = TranslatePegTypeToCS(rule.ReturnType) + "?";
            }
            else
            {
                // No type annotation: use GeneratedPtr? (for invalid_* rules, lookaheads, etc.)
                returnType = "GeneratedPtr?";
            }

            _ruleReturnTypeCache[rule.Name] = returnType;
            return returnType;
        }


        /// <summary>
        /// Get return type for a rule by name
        /// </summary>
        public string GetRuleReturnType(string ruleName)
        {
            var rule = _grammar.Rules.FirstOrDefault(r => r.Name == ruleName);
            if (rule != null)
            {
                return GetRuleReturnType(rule);
            }
            // Unknown rule - return base AST node type
            // CPython 3.12: All AST nodes inherit from GeneratedAstNode
            return "GeneratedAstNode?";
        }


        // Output helper methods
        public void WriteLine(string line = "")
        {
            if (!string.IsNullOrEmpty(line))
            {
                _output.Append(new string(' ', _indentLevel * 4));
                _output.AppendLine(line);
            }
            else
            {
                _output.AppendLine();
            }
        }

        public void Indent() => _indentLevel++;
        public void Dedent() => _indentLevel--;

        /// <summary>
        /// Generate standard alternative failure code
        /// CPython 3.12: Clear pending error when alternative fails
        /// </summary>
        public void WriteAlternativeFailure()
        {
            WriteLine("_position = _mark;");
            WriteLine("_pendingSyntaxError = null;  // CPython 3.12: Clear error when alternative fails");
            WriteLine("_res = null;");
            WriteLine("break;  // Exit this alternative");
        }

        private string EscapeForCSharp(string value)
        {
            // Remove outer quotes if present and escape for C#
            var cleanValue = value;
            if ((cleanValue.StartsWith("\"") && cleanValue.EndsWith("\"")) ||
                (cleanValue.StartsWith("'") && cleanValue.EndsWith("'")))
            {
                cleanValue = cleanValue.Substring(1, cleanValue.Length - 2);
            }
            return cleanValue.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private string SanitizeVariableName(string name)
        {
            // List of C# keywords to avoid
            var csharpKeywords = new HashSet<string>
            {
                "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
                "checked", "class", "const", "continue", "decimal", "default", "delegate",
                "do", "double", "else", "enum", "event", "explicit", "extern", "false",
                "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
                "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
                "new", "null", "object", "operator", "out", "override", "params", "private",
                "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
                "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
                "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
                "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "while"
            };

            return csharpKeywords.Contains(name) ? $"@{name}" : name;
        }

        private void GenerateCompoundStmtMethod(string returnType)
        {
            // compound_stmt[stmt_ty]: if_stmt | while_stmt | for_stmt | with_stmt | try_stmt | function_def | class_def | etc.
            WriteLine("// compound_stmt[stmt_ty]: if_stmt | while_stmt | for_stmt | with_stmt | try_stmt | function_def | class_def");
            WriteLine();
            WriteLine("// Try 'if' statement");
            WriteLine("if (ExpectKeyword(\"if\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Simplified if statement: 'if' condition ':' body");
            WriteLine("// For now, expect 'True' as condition");
            WriteLine("if (ExpectKeyword(\"True\"))");
            WriteLine("{");
            Indent();
            WriteLine("if (Expect(\"COLON\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Skip any NEWLINE tokens");
            WriteLine("while (CurrentToken?.Type.ToString() == \"NEWLINE\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine("// For now, expect just a simple pass statement in the body");
            WriteLine("if (ExpectKeyword(\"pass\"))");
            WriteLine("{");
            Indent();
            WriteLine($"var ifStmt = new {returnType}();");
            WriteLine("ifStmt.StatementType = \"if\";");
            WriteLine("ifStmt.Value = new { Condition = \"True\", Body = \"pass\" };");
            WriteLine("return ifStmt;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'while' statement");
            WriteLine("if (ExpectKeyword(\"while\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Simplified while statement: 'while' condition ':' body");
            WriteLine("// For now, expect 'True' as condition");
            WriteLine("if (ExpectKeyword(\"True\"))");
            WriteLine("{");
            Indent();
            WriteLine("if (Expect(\"COLON\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Skip any NEWLINE tokens");
            WriteLine("while (CurrentToken?.Type.ToString() == \"NEWLINE\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine("// For now, expect either pass or break statement in the body");
            WriteLine("if (ExpectKeyword(\"pass\"))");
            WriteLine("{");
            Indent();
            WriteLine($"var whileStmt = new {returnType}();");
            WriteLine("whileStmt.StatementType = \"while\";");
            WriteLine("whileStmt.Value = new { Condition = \"True\", Body = \"pass\" };");
            WriteLine("return whileStmt;");
            Dedent();
            WriteLine("}");
            WriteLine("else if (ExpectKeyword(\"break\"))");
            WriteLine("{");
            Indent();
            WriteLine($"var whileStmt = new {returnType}();");
            WriteLine("whileStmt.StatementType = \"while\";");
            WriteLine("whileStmt.Value = new { Condition = \"True\", Body = \"break\" };");
            WriteLine("return whileStmt;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'def' function definition");
            WriteLine("if (ExpectKeyword(\"def\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Simplified function definition: 'def' name '(' ')' ':' body");
            WriteLine("// For now, expect a simple function name");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NAME\")");
            WriteLine("{");
            Indent();
            WriteLine("var functionName = CurrentToken.Value;");
            WriteLine("Advance(); // consume function name");
            WriteLine("if (Expect(\"LPAR\")) // '('");
            WriteLine("{");
            Indent();
            WriteLine("if (Expect(\"RPAR\")) // ')'");
            WriteLine("{");
            Indent();
            WriteLine("if (Expect(\"COLON\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Skip any NEWLINE tokens");
            WriteLine("while (CurrentToken?.Type.ToString() == \"NEWLINE\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine("// For now, expect just a simple pass statement in the body");
            WriteLine("if (ExpectKeyword(\"pass\"))");
            WriteLine("{");
            Indent();
            WriteLine($"var funcDef = new {returnType}();");
            WriteLine("funcDef.StatementType = \"function_def\";");
            WriteLine("funcDef.Value = new { Name = functionName, Params = new string[0], Body = \"pass\" };");
            WriteLine("return funcDef;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'class' definition");
            WriteLine("if (ExpectKeyword(\"class\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Simplified class definition: 'class' name ':' body");
            WriteLine("// For now, expect a simple class name");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NAME\")");
            WriteLine("{");
            Indent();
            WriteLine("var className = CurrentToken.Value;");
            WriteLine("Advance(); // consume class name");
            WriteLine("if (Expect(\"COLON\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Skip any NEWLINE tokens");
            WriteLine("while (CurrentToken?.Type.ToString() == \"NEWLINE\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine("// For now, expect just a simple pass statement in the body");
            WriteLine("if (ExpectKeyword(\"pass\"))");
            WriteLine("{");
            Indent();
            WriteLine($"var classDef = new {returnType}();");
            WriteLine("classDef.StatementType = \"class_def\";");
            WriteLine("classDef.Value = new { Name = className, Bases = new string[0], Body = \"pass\" };");
            WriteLine("return classDef;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'for' statement");
            WriteLine("if (ExpectKeyword(\"for\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Simplified for statement: 'for' name 'in' iterable ':' body");
            WriteLine("// For now, expect a simple variable name");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NAME\")");
            WriteLine("{");
            Indent();
            WriteLine("var varName = CurrentToken.Value;");
            WriteLine("Advance(); // consume variable name");
            WriteLine("if (ExpectKeyword(\"in\"))");
            WriteLine("{");
            Indent();
            WriteLine("// For now, expect 'range' as the iterable");
            WriteLine("if (ExpectKeyword(\"range\"))");
            WriteLine("{");
            Indent();
            WriteLine("if (Expect(\"LPAR\")) // '('");
            WriteLine("{");
            Indent();
            WriteLine("// For now, expect a single number argument");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NUMBER\")");
            WriteLine("{");
            Indent();
            WriteLine("var rangeValue = CurrentToken.Value;");
            WriteLine("Advance(); // consume number");
            WriteLine("if (Expect(\"RPAR\")) // ')'");
            WriteLine("{");
            Indent();
            WriteLine("if (Expect(\"COLON\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Skip any NEWLINE tokens");
            WriteLine("while (CurrentToken?.Type.ToString() == \"NEWLINE\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine("// For now, expect just a simple pass statement in the body");
            WriteLine("if (ExpectKeyword(\"pass\"))");
            WriteLine("{");
            Indent();
            WriteLine($"var forStmt = new {returnType}();");
            WriteLine("forStmt.StatementType = \"for\";");
            WriteLine("forStmt.Value = new { Variable = varName, Iterable = \"range\", RangeValue = rangeValue, Body = \"pass\" };");
            WriteLine("return forStmt;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'try' statement");
            WriteLine("if (ExpectKeyword(\"try\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Simplified try statement: 'try' ':' body 'except' ':' handler");
            WriteLine("if (Expect(\"COLON\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Skip any NEWLINE tokens");
            WriteLine("while (CurrentToken?.Type.ToString() == \"NEWLINE\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine("// For now, expect just a simple pass statement in try body");
            WriteLine("if (ExpectKeyword(\"pass\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Skip any NEWLINE tokens");
            WriteLine("while (CurrentToken?.Type.ToString() == \"NEWLINE\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine("// Expect 'except' clause");
            WriteLine("if (ExpectKeyword(\"except\"))");
            WriteLine("{");
            Indent();
            WriteLine("if (Expect(\"COLON\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Skip any NEWLINE tokens");
            WriteLine("while (CurrentToken?.Type.ToString() == \"NEWLINE\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine("// For now, expect just a simple pass statement in except body");
            WriteLine("if (ExpectKeyword(\"pass\"))");
            WriteLine("{");
            Indent();
            WriteLine($"var tryStmt = new {returnType}();");
            WriteLine("tryStmt.StatementType = \"try\";");
            WriteLine("tryStmt.Value = new { TryBody = \"pass\", ExceptType = (string?)null, ExceptBody = \"pass\", FinallyBody = (string?)null };");
            WriteLine("return tryStmt;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Try 'match' statement (Python 3.10+ pattern matching)");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NAME\" && CurrentToken?.Value == \"match\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume 'match'");
            WriteLine("// Parse subject expression (simplified)");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NAME\")");
            WriteLine("{");
            Indent();
            WriteLine("var subject = CurrentToken.Value;");
            WriteLine("Advance(); // consume subject");
            WriteLine("if (Expect(\"COLON\"))");
            WriteLine("{");
            Indent();
            WriteLine("if (Expect(\"NEWLINE\"))");
            WriteLine("{");
            Indent();
            WriteLine("if (Expect(\"INDENT\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Parse case blocks (simplified)");
            WriteLine("var cases = new List<object>();");
            WriteLine("while (CurrentToken?.Type.ToString() == \"NAME\" && CurrentToken?.Value == \"case\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume 'case'");
            WriteLine("// Parse pattern (simplified - just expect NAME)");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NAME\")");
            WriteLine("{");
            Indent();
            WriteLine("var pattern = CurrentToken.Value;");
            WriteLine("Advance(); // consume pattern");
            WriteLine("if (Expect(\"COLON\"))");
            WriteLine("{");
            Indent();
            WriteLine("// For now, expect just a simple pass statement");
            WriteLine("if (ExpectKeyword(\"pass\"))");
            WriteLine("{");
            Indent();
            WriteLine("cases.Add(new { pattern = pattern, body = \"pass\" });");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("if (Expect(\"DEDENT\"))");
            WriteLine("{");
            Indent();
            WriteLine($"var matchStmt = new {returnType}();");
            WriteLine("matchStmt.StatementType = \"match\";");
            WriteLine("matchStmt.Value = new { subject = subject, cases = cases };");
            WriteLine("return matchStmt;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// TODO: Add other compound statements");
            WriteLine("// - with_stmt");
            WriteLine();
            WriteLine("// No match found");
            WriteLine($"return default({returnType});");
        }

        private void GenerateEmbeddedTypes()
        {
            WriteLine("// ========================================");
            WriteLine("// Embedded PEG Interpreter - Complete Copy");
            WriteLine("// This makes PyParser.cs independent from SharpPy.PegGenerator");
            WriteLine("// ========================================");
            WriteLine();

            // CPython 3.12: Generated parser is independent - no embedded interpreter
            // CopyPegInterpreterSource(); // REMOVED - PegInterpreter only used during generation
            // CopyEmbeddedGrammarSource(); // REMOVED - Grammar only used during generation
        }

        private void CopyPegInterpreterSource()
        {
            // Read actual PegInterpreter.cs and copy it
            // Get the solution root directory - assembly is in bin/Debug/net8.0 or similar
            var assemblyLocation = typeof(CSharpCodeGenerator).Assembly.Location;
            var assemblyDir = Path.GetDirectoryName(assemblyLocation) ?? "";
            var interpreterPath = Path.Combine(assemblyDir, "..", "..", "..", "Interpreter", "PegInterpreter.cs");
            interpreterPath = Path.GetFullPath(interpreterPath);

            if (!File.Exists(interpreterPath))
            {
                Console.WriteLine($"[WARNING] PegInterpreter.cs not found at {interpreterPath}");
                Console.WriteLine("[WARNING] Generating minimal stub instead");
                GenerateMinimalPegInterpreterStub();
                return;
            }

            Console.WriteLine($"[CODEGEN] Copying PegInterpreter from {interpreterPath}");
            var source = File.ReadAllText(interpreterPath);

            // Replace all Grammar type references with Embedded types
            // First replace qualified names (Grammar.X), then standalone type names
            source = source.Replace("using SharpPy.PegGenerator.Grammar;", "// Grammar types embedded below");
            source = source.Replace("using SharpPy.Generated;", "// Already in SharpPy.Generated namespace");
            source = source.Replace("namespace SharpPy.PegGenerator.Interpreter", "// Part of SharpPy.Generated namespace");

            // Replace qualified names first (Grammar.X)
            source = source.Replace("Grammar.Grammar", "EmbeddedGrammar");
            source = source.Replace("Grammar.Rule", "EmbeddedRule");
            source = source.Replace("Grammar.Alternative", "EmbeddedAlternative");
            source = source.Replace("Grammar.Item", "EmbeddedItem");
            source = source.Replace("Grammar.Atom", "EmbeddedAtom");
            source = source.Replace("Grammar.RuleRef", "EmbeddedRuleRef");
            source = source.Replace("Grammar.StringLiteral", "EmbeddedStringLiteral");
            source = source.Replace("Grammar.Group", "EmbeddedGroup");
            source = source.Replace("Grammar.Optional", "EmbeddedOptional");
            source = source.Replace("Grammar.ZeroOrMore", "EmbeddedZeroOrMore");
            source = source.Replace("Grammar.OneOrMore", "EmbeddedOneOrMore");
            source = source.Replace("Grammar.PositiveLookahead", "EmbeddedPositiveLookahead");
            source = source.Replace("Grammar.NegativeLookahead", "EmbeddedNegativeLookahead");
            source = source.Replace("Grammar.Cut", "EmbeddedCut");

            // Replace standalone type names ONLY when used as types (after specific keywords)
            // This avoids replacing property names like "item.Atom"
            var typeReplacements = new[] {
                ("Rule", "EmbeddedRule"),
                ("Alternative", "EmbeddedAlternative"),
                ("Item", "EmbeddedItem"),
                ("Atom", "EmbeddedAtom"),
                ("RuleRef", "EmbeddedRuleRef"),
                ("StringLiteral", "EmbeddedStringLiteral"),
                ("Group", "EmbeddedGroup"),
                ("Optional", "EmbeddedOptional"),
                ("ZeroOrMore", "EmbeddedZeroOrMore"),
                ("OneOrMore", "EmbeddedOneOrMore"),
                ("PositiveLookahead", "EmbeddedPositiveLookahead"),
                ("NegativeLookahead", "EmbeddedNegativeLookahead"),
                ("Cut", "EmbeddedCut")
            };

            foreach (var (oldType, newType) in typeReplacements)
            {
                // Replace in type declarations: "private Rule", "List<Rule>", "Rule rule", "(Rule r)"
                source = System.Text.RegularExpressions.Regex.Replace(source, $@"(private|public|protected|internal|static|readonly|List<|IEnumerable<|var|return|new|\(|\s){oldType}(\s|\)|\>|,)", m => m.Groups[1].Value + newType + m.Groups[2].Value);
                // Replace in switch patterns:  "case Rule:"
                source = System.Text.RegularExpressions.Regex.Replace(source, $@"(case\s+){oldType}(\s+)", m => m.Groups[1].Value + newType + m.Groups[2].Value);
                //  Replace in is/as patterns: "is Rule", "as Rule"
                source = System.Text.RegularExpressions.Regex.Replace(source, $@"(\sis\s+|\sas\s+){oldType}(\s|\))", m => m.Groups[1].Value + newType + m.Groups[2].Value);
            }

            // CPython 3.12: object → GeneratedPtr conversion is done in source files directly
            // No regex replacement needed - source files already use GeneratedPtr

            // Write PegParseResult types header (ITokenInfo already defined in PyTokenizer.cs)
            WriteLine("// ========================================");
            WriteLine("// PegParseResult Types - from IPegParseResult.cs");
            WriteLine("// Note: ITokenInfo is defined in PyTokenizer.cs");
            WriteLine("// ========================================");
            WriteLine();

            // Copy all parse result types from IPegParseResult.cs
            var parseResultPath = Path.Combine(assemblyDir, "..", "..", "..", "Interpreter", "IPegParseResult.cs");
            parseResultPath = Path.GetFullPath(parseResultPath);

            if (File.Exists(parseResultPath))
            {
                Console.WriteLine($"[CODEGEN] Copying parse result types from {parseResultPath}");
                var parseResultSource = File.ReadAllText(parseResultPath);
                // Remove namespace and using statements
                parseResultSource = parseResultSource.Replace("using System;", "");
                parseResultSource = parseResultSource.Replace("using System.Collections.Generic;", "");
                parseResultSource = parseResultSource.Replace("namespace SharpPy.PegGenerator.Interpreter", "// Part of SharpPy.Generated namespace");

                // CPython 3.12: object → GeneratedPtr conversion is done in source file directly
                // No regex replacement needed - IPegParseResult.cs already uses GeneratedPtr

                // Extract only the class/interface definitions
                var parseResultStartIdx = parseResultSource.IndexOf("/// <summary>");
                if (parseResultStartIdx >= 0)
                {
                    var parseResultContent = parseResultSource.Substring(parseResultStartIdx);
                    // Remove namespace closing brace
                    var parseResultLastBrace = parseResultContent.LastIndexOf('}');
                    if (parseResultLastBrace > 0 && parseResultContent.Substring(parseResultLastBrace).Trim() == "}")
                    {
                        parseResultContent = parseResultContent.Substring(0, parseResultLastBrace).TrimEnd();
                    }

                    using (var reader = new StringReader(parseResultContent))
                    {
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            WriteLine(line);
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine($"[WARNING] IPegParseResult.cs not found at {parseResultPath}");
                // Fallback minimal implementation
                WriteLine("public class PegParseResult : IPegParseResult");
                WriteLine("{");
                Indent();
                WriteLine("public bool IsSuccess { get; set; }");
                WriteLine("public object? Value { get; set; }");
                WriteLine("public int Position { get; set; }");
                Dedent();
                WriteLine("}");
            }
            WriteLine();

            // Write PegInterpreter class and supporting types
            WriteLine("// ========================================");
            WriteLine("// PegInterpreter Supporting Types");
            WriteLine("// ========================================");
            WriteLine();

            // Include PegModule, PegStmt, PegExpr classes
            WriteLine("public class PegModule");
            WriteLine("{");
            Indent();
            WriteLine("public List<object>? Body { get; set; }");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class PegStmt");
            WriteLine("{");
            Indent();
            WriteLine("public string? Type { get; set; }");
            WriteLine("public object? Data { get; set; }");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class PegExpr");
            WriteLine("{");
            Indent();
            WriteLine("public string? Type { get; set; }");
            WriteLine("public object? Data { get; set; }");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// ========================================");
            WriteLine("// PegInterpreter - Complete Copy");
            WriteLine("// Note: ContextType and ParserContext are defined in PyTokenizer.cs");
            WriteLine("// ========================================");
            WriteLine();

            // Extract only PegInterpreter class (ContextType and ParserContext are in PyTokenizer.cs)
            var startIdx = source.IndexOf("public class PegInterpreter");

            if (startIdx >= 0)
            {
                var content = source.Substring(startIdx);
                // Remove namespace closing brace if present
                var lastBrace = content.LastIndexOf('}');
                if (lastBrace > 0 && content.Substring(lastBrace).Trim() == "}")
                {
                    content = content.Substring(0, lastBrace).TrimEnd();
                }

                // Write line by line
                using (var reader = new StringReader(content))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        WriteLine(line);
                    }
                }
            }
        }

        private void CopyEmbeddedGrammarSource()
        {
            // Generate complete EmbeddedGrammar structure from _grammar (CPython 3.12 PEG compatible)
            WriteLine("// ========================================");
            WriteLine("// Embedded Grammar Types - from python.gram");
            WriteLine("// These mirror Grammar.Grammar structure for PegInterpreter");
            WriteLine("// ========================================");
            WriteLine();

            // Base classes and types
            WriteLine("public abstract class EmbeddedAtom { }");
            WriteLine();

            WriteLine("public class EmbeddedRuleRef : EmbeddedAtom");
            WriteLine("{");
            Indent();
            WriteLine("public string Name { get; set; } = \"\";");
            WriteLine("public override string ToString() => Name;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class EmbeddedStringLiteral : EmbeddedAtom");
            WriteLine("{");
            Indent();
            WriteLine("public string Value { get; set; } = \"\";");
            WriteLine("public override string ToString() => $\"'{Value}'\";");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class EmbeddedGroup : EmbeddedAtom");
            WriteLine("{");
            Indent();
            WriteLine("public List<EmbeddedAlternative> Alternatives { get; set; } = new();");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class EmbeddedOptional : EmbeddedAtom");
            WriteLine("{");
            Indent();
            WriteLine("public EmbeddedAtom Expression { get; set; } = null!;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class EmbeddedZeroOrMore : EmbeddedAtom");
            WriteLine("{");
            Indent();
            WriteLine("public EmbeddedAtom Expression { get; set; } = null!;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class EmbeddedOneOrMore : EmbeddedAtom");
            WriteLine("{");
            Indent();
            WriteLine("public EmbeddedAtom Expression { get; set; } = null!;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class EmbeddedPositiveLookahead : EmbeddedAtom");
            WriteLine("{");
            Indent();
            WriteLine("public EmbeddedAtom Expression { get; set; } = null!;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class EmbeddedNegativeLookahead : EmbeddedAtom");
            WriteLine("{");
            Indent();
            WriteLine("public EmbeddedAtom Expression { get; set; } = null!;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class EmbeddedCut : EmbeddedAtom");
            WriteLine("{");
            Indent();
            WriteLine("public EmbeddedAtom Expression { get; set; } = null!;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class EmbeddedGather : EmbeddedAtom");
            WriteLine("{");
            Indent();
            WriteLine("public EmbeddedAtom Separator { get; set; } = null!;");
            WriteLine("public EmbeddedAtom Item { get; set; } = null!;");
            WriteLine("public bool IsOneOrMore { get; set; }");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class EmbeddedItem");
            WriteLine("{");
            Indent();
            WriteLine("public string? Name { get; set; }");
            WriteLine("public EmbeddedAtom Atom { get; set; } = null!;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class EmbeddedAlternative");
            WriteLine("{");
            Indent();
            WriteLine("public List<EmbeddedItem> Items { get; set; } = new();");
            WriteLine("public string? Action { get; set; }");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("public class EmbeddedRule");
            WriteLine("{");
            Indent();
            WriteLine("public string Name { get; set; } = \"\";");
            WriteLine("public string? ReturnType { get; set; }");
            WriteLine("public List<EmbeddedAlternative> Alternatives { get; set; } = new();");
            WriteLine("public bool IsMemoized { get; set; } = false;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Main EmbeddedGrammar class
            WriteLine("public class EmbeddedGrammar");
            WriteLine("{");
            Indent();
            WriteLine("public List<EmbeddedRule> Rules { get; } = new();");
            WriteLine();
            WriteLine("public static EmbeddedGrammar GetGrammar()");
            WriteLine("{");
            Indent();
            WriteLine("var grammar = new EmbeddedGrammar();");
            WriteLine();

            // Generate all rules with complete structure
            foreach (var rule in _grammar.Rules)
            {
                WriteLine($"// Rule: {rule.Name}");
                WriteLine($"var rule_{EscapeRuleName(rule.Name)} = new EmbeddedRule");
                WriteLine("{");
                Indent();
                WriteLine($"Name = \"{rule.Name}\",");
                WriteLine($"ReturnType = {(rule.ReturnType != null ? $"\"{rule.ReturnType}\"" : "null")},");
                WriteLine($"IsMemoized = {rule.IsMemoized.ToString().ToLower()},");
                WriteLine("Alternatives = new()");
                WriteLine("{");
                Indent();

                // Generate alternatives
                foreach (var alt in rule.Alternatives)
                {
                    WriteLine("new EmbeddedAlternative");
                    WriteLine("{");
                    Indent();
                    WriteLine($"Action = {(alt.Action != null ? $"\"{EscapeString(alt.Action)}\"" : "null")},");
                    WriteLine("Items = new()");
                    WriteLine("{");
                    Indent();

                    // Generate items
                    foreach (var item in alt.Items)
                    {
                        WriteLine($"new EmbeddedItem {{ Name = {(item.Name != null ? $"\"{item.Name}\"" : "null")}, Atom = {GenerateEmbeddedAtom(item.Atom)} }},");
                    }

                    Dedent();
                    WriteLine("}");
                    Dedent();
                    WriteLine("},");
                }

                Dedent();
                WriteLine("}");
                Dedent();
                WriteLine("};");
                WriteLine($"grammar.Rules.Add(rule_{EscapeRuleName(rule.Name)});");
                WriteLine();
            }

            WriteLine("return grammar;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        private string EscapeRuleName(string name)
        {
            return name.Replace("-", "_").Replace(".", "_");
        }

        public string EscapeString(string str)
        {
            // Escape backslashes first, then quotes, then newlines
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        /// <summary>
        /// CPython 3.12: Get keyword token type number, or 0 if not a keyword
        /// Like CPython's self.keywords.get(keyword_str, 0)
        /// </summary>
        public int GetKeywordTokenType(string value)
        {
            if (_keywords.TryGetValue(value, out int tokenType))
            {
                return tokenType;
            }
            return 0;
        }

        private string GenerateEmbeddedAtom(Grammar.Atom atom)
        {
            return atom switch
            {
                Grammar.RuleRef ruleRef => $"new EmbeddedRuleRef {{ Name = \"{ruleRef.Name}\" }}",
                Grammar.StringLiteral str => $"new EmbeddedStringLiteral {{ Value = \"{EscapeString(str.Value)}\" }}",
                Grammar.Group group => GenerateEmbeddedGroup(group),
                Grammar.Optional opt => $"new EmbeddedOptional {{ Expression = {GenerateEmbeddedAtom(opt.Expression)} }}",
                Grammar.ZeroOrMore zero => $"new EmbeddedZeroOrMore {{ Expression = {GenerateEmbeddedAtom(zero.Expression)} }}",
                Grammar.OneOrMore one => $"new EmbeddedOneOrMore {{ Expression = {GenerateEmbeddedAtom(one.Expression)} }}",
                Grammar.Gather gather => $"new EmbeddedGather {{ Separator = {GenerateEmbeddedAtom(gather.Separator)}, Item = {GenerateEmbeddedAtom(gather.Item)}, IsOneOrMore = {(gather.IsOneOrMore ? "true" : "false")} }}",
                Grammar.PositiveLookahead pos => $"new EmbeddedPositiveLookahead {{ Expression = {GenerateEmbeddedAtom(pos.Expression)} }}",
                Grammar.NegativeLookahead neg => $"new EmbeddedNegativeLookahead {{ Expression = {GenerateEmbeddedAtom(neg.Expression)} }}",
                Grammar.Cut cut => $"new EmbeddedCut {{ Expression = {GenerateEmbeddedAtom(cut.Expression)} }}",
                _ => throw new NotImplementedException($"Unknown atom type: {atom.GetType().Name}")
            };
        }

        private string GenerateEmbeddedGroup(Grammar.Group group)
        {
            var sb = new StringBuilder();
            sb.Append("new EmbeddedGroup { Alternatives = new() { ");

            foreach (var alt in group.Alternatives)
            {
                sb.Append("new EmbeddedAlternative { Items = new() { ");
                foreach (var item in alt.Items)
                {
                    sb.Append($"new EmbeddedItem {{ Name = {(item.Name != null ? $"\"{item.Name}\"" : "null")}, Atom = {GenerateEmbeddedAtom(item.Atom)} }}, ");
                }
                sb.Append("} }, ");
            }

            sb.Append("} }");
            return sb.ToString();
        }

        private void GenerateMinimalPegInterpreterStub()
        {
            // IEmbeddedTokenInfo interface
            WriteLine("public interface IEmbeddedTokenInfo");
            WriteLine("{");
            Indent();
            WriteLine("object Type { get; }");
            WriteLine("string Value { get; }");
            WriteLine("int Line { get; }");
            WriteLine("int Column { get; }");
            Dedent();
            WriteLine("}");
            WriteLine();

            // EmbeddedTokenInfoAdapter class
            WriteLine("public class EmbeddedTokenInfoAdapter : IEmbeddedTokenInfo");
            WriteLine("{");
            Indent();
            WriteLine("private readonly object _token;");
            WriteLine();
            WriteLine("public EmbeddedTokenInfoAdapter(object token)");
            WriteLine("{");
            Indent();
            WriteLine("_token = token;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("public object Type => _token.GetType().GetProperty(\"Type\")?.GetValue(_token);");
            WriteLine("public string Value => _token.GetType().GetProperty(\"Value\")?.GetValue(_token)?.ToString() ?? \"\";");
            WriteLine("public int Line => (int)(_token.GetType().GetProperty(\"Line\")?.GetValue(_token) ?? 0);");
            WriteLine("public int Column => (int)(_token.GetType().GetProperty(\"Column\")?.GetValue(_token) ?? 0);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // EmbeddedGrammar class
            WriteLine("public class EmbeddedGrammar");
            WriteLine("{");
            Indent();
            WriteLine("// Hard-coded Python grammar rules for basic assignment");
            WriteLine("public Dictionary<string, string> Rules { get; } = new()");
            WriteLine("{");
            Indent();
            WriteLine("{ \"assignment\", \"NAME '=' expr\" },");
            WriteLine("{ \"expr\", \"NUMBER | NAME\" },");
            WriteLine("{ \"simple_stmt\", \"assignment\" },");
            WriteLine("{ \"stmt\", \"simple_stmt\" }");
            Dedent();
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            // EmbeddedPegInterpreter class
            WriteLine("public class EmbeddedPegInterpreter");
            WriteLine("{");
            Indent();
            WriteLine("private readonly EmbeddedGrammar _grammar;");
            WriteLine("private readonly List<IEmbeddedTokenInfo> _tokens;");
            WriteLine("private readonly GeneratedPyParser _parser;");
            WriteLine("private bool _isFirstPass = true;");
            WriteLine("private int _firstPassFailurePosition = -1;");
            WriteLine();
            WriteLine("public EmbeddedPegInterpreter(EmbeddedGrammar grammar, List<IEmbeddedTokenInfo> tokens, GeneratedPyParser parser)");
            WriteLine("{");
            Indent();
            WriteLine("_grammar = grammar;");
            WriteLine("_tokens = tokens;");
            WriteLine("_parser = parser;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("/// <summary>");
            WriteLine("/// Parse a rule with 2-pass support for invalid rules");
            WriteLine("/// </summary>");
            WriteLine("public object ParseRuleWithTwoPass(string ruleName)");
            WriteLine("{");
            Indent();
            WriteLine("// First pass: exclude invalid rules");
            WriteLine("_isFirstPass = true;");
            WriteLine("_firstPassFailurePosition = -1;");
            WriteLine("var firstPassPosition = _parser._position;");
            WriteLine();
            WriteLine("var result = ParseRule(ruleName);");
            WriteLine("if (result != null)");
            WriteLine("{");
            Indent();
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// First pass failed - record failure position");
            WriteLine("_firstPassFailurePosition = _parser._position;");
            WriteLine();
            WriteLine("// Reset position for second pass");
            WriteLine("_parser._position = firstPassPosition;");
            WriteLine();
            WriteLine("// Second pass: include invalid rules for better error messages");
            WriteLine("_isFirstPass = false;");
            WriteLine("result = ParseRule(ruleName);");
            WriteLine("if (result != null)");
            WriteLine("{");
            Indent();
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Both passes failed - use first pass failure position for more accurate error location");
            WriteLine("if (_firstPassFailurePosition >= 0)");
            WriteLine("{");
            Indent();
            WriteLine("_parser._position = _firstPassFailurePosition;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("/// <summary>");
            WriteLine("/// Check if a rule should be excluded in the first pass");
            WriteLine("/// </summary>");
            WriteLine("private bool ShouldExcludeRuleInFirstPass(string ruleName)");
            WriteLine("{");
            Indent();
            WriteLine("return _isFirstPass && ruleName.StartsWith(\"invalid_\");");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("public object ParseRule(string ruleName)");
            WriteLine("{");
            Indent();
            WriteLine("// Check if this rule should be excluded in first pass");
            WriteLine("if (ShouldExcludeRuleInFirstPass(ruleName))");
            WriteLine("{");
            Indent();
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("switch (ruleName)");
            WriteLine("{");
            Indent();
            WriteLine("case \"file\":");
            Indent();
            WriteLine("return ParseFile();");
            Dedent();
            WriteLine("case \"assignment\":");
            Indent();
            WriteLine("return ParseAssignment();");
            Dedent();
            WriteLine("case \"expr\":");
            Indent();
            WriteLine("return ParseExpr();");
            Dedent();
            WriteLine("case \"simple_stmt\":");
            Indent();
            WriteLine("return ParsePegStmt();");
            Dedent();
            WriteLine("case \"stmt\":");
            Indent();
            WriteLine("return ParseStmt();");
            Dedent();
            WriteLine("case \"expression_stmt\":");
            Indent();
            WriteLine("return _parser.ParseExpressionStmt();");
            Dedent();
            WriteLine("case \"call\":");
            Indent();
            WriteLine("return _parser.ParseExpressionStmt();");
            Dedent();
            WriteLine("default:");
            Indent();
            WriteLine("return null;");
            Dedent();
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("private object ParseFile()");
            WriteLine("{");
            Indent();
            WriteLine("// Parse: statements ENDMARKER");
            // WriteLine("Console.WriteLine($\"[DEBUG] ParseFile starting: {_parser._position} / {_tokens.Count}\");");
            WriteLine("var statements = new List<object>();");
            WriteLine("while (_parser._position < _tokens.Count && _tokens[_parser._position].Type.ToString() != \"ENDMARKER\")");
            WriteLine("{");
            Indent();
            // WriteLine("Console.WriteLine($\"[DEBUG] ParseFile loop: position={_parser._position}, token={_tokens[_parser._position].Type}:{_tokens[_parser._position].Value}\");");
            WriteLine("// Skip NEWLINE tokens");
            WriteLine("if (_tokens[_parser._position].Type.ToString() == \"NEWLINE\")");
            WriteLine("{");
            Indent();
            WriteLine("_parser._position++;");
            WriteLine("continue;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("var initialPos = _parser._position;");
            WriteLine("var stmt = ParseStmt();");
            // WriteLine("Console.WriteLine($\"[DEBUG] ParseStmt returned: {(stmt != null ? stmt.GetType().Name : \"null\")}\");");
            WriteLine("if (stmt != null)");
            WriteLine("{");
            Indent();
            WriteLine("statements.Add(stmt);");
            WriteLine("// Ensure position advanced after successful parsing");
            WriteLine("if (_parser._position == initialPos)");
            WriteLine("{");
            Indent();
            // WriteLine("Console.WriteLine($\"[DEBUG] Warning: ParseStmt succeeded but position not advanced, forcing advance\");");
            WriteLine("_parser._position++;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            // WriteLine("Console.WriteLine($\"[DEBUG] ParseStmt failed, advancing position to prevent infinite loop\");");
            WriteLine("_parser._position++; // Skip unknown token");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            // WriteLine("Console.WriteLine($\"[DEBUG] ParseFile collected {statements.Count} statements\");");
            WriteLine("var module = new GeneratedModule();");
            WriteLine("module.Body = new GeneratedStmtSeq();");
            WriteLine("foreach (var stmt in statements)");
            WriteLine("{");
            Indent();
            WriteLine("if (stmt is GeneratedStmt generatedStmt)");
            WriteLine("{");
            Indent();
            // WriteLine("Console.WriteLine($\"[DEBUG] Adding statement: {generatedStmt.StatementType}\");");
            WriteLine("module.Body.Add(generatedStmt);");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            // WriteLine("Console.WriteLine($\"[DEBUG] Skipping non-GeneratedStmt: {stmt?.GetType().Name}\");");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("return module;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("private object ParseAssignment()");
            WriteLine("{");
            Indent();
            WriteLine("// Parse: NAME '=' expr");
            WriteLine("var startPos = _parser._position;");
            WriteLine("if (_parser._position >= _tokens.Count) return null;");
            WriteLine();
            WriteLine("// Check for NAME token");
            WriteLine("var nameToken = _tokens[_parser._position];");
            WriteLine("if (nameToken.Type.ToString() != \"NAME\") return null;");
            WriteLine("_parser._position++;");
            WriteLine();
            WriteLine("// Check for '=' operator");
            WriteLine("if (_parser._position >= _tokens.Count)");
            WriteLine("{");
            Indent();
            WriteLine("_parser._position = startPos; // Backtrack on failure");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine("var opToken = _tokens[_parser._position];");
            WriteLine("if (opToken.Type.ToString() != \"OP\" || opToken.Value != \"=\")");
            WriteLine("{");
            Indent();
            WriteLine("_parser._position = startPos; // Backtrack on failure");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine("_parser._position++;");
            WriteLine();
            WriteLine("// Parse expression");
            WriteLine("var expr = ParseExpr();");
            WriteLine("if (expr == null)");
            WriteLine("{");
            Indent();
            WriteLine("_parser._position = startPos; // Backtrack on failure");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Create assignment statement");
            WriteLine("var targetExpr = new GeneratedNameExpr { Id = nameToken.Value, Context = \"Store\" };");
            WriteLine("var targets = new GeneratedExprSeq { targetExpr };");
            WriteLine("var stmt = new GeneratedAssignStmt");
            WriteLine("{");
            Indent();
            WriteLine("Targets = targets,");
            WriteLine("Value = (GeneratedExpr)expr");
            Dedent();
            WriteLine("};");
            WriteLine("return stmt;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("private object ParseExpr()");
            WriteLine("{");
            Indent();
            WriteLine("// Parse: NUMBER | NAME");
            WriteLine("if (_parser._position >= _tokens.Count) return null;");
            WriteLine();
            WriteLine("var token = _tokens[_parser._position];");
            WriteLine("if (token.Type.ToString() == \"NUMBER\" || token.Type.ToString() == \"NAME\")");
            WriteLine("{");
            Indent();
            WriteLine("_parser._position++;");
            WriteLine("return token.Value;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("private object ParsePegStmt()");
            WriteLine("{");
            Indent();
            WriteLine("// Try assignment first");
            WriteLine("var assignment = ParseAssignmentEnhanced();");
            WriteLine("if (assignment != null) return assignment;");
            WriteLine("");
            WriteLine("// Try expression statement (function calls, etc.)");
            WriteLine("var expressionStmt = _parser.ParseExpressionStmt();");
            WriteLine("if (expressionStmt != null) return expressionStmt;");
            WriteLine("");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("private object ParseStmt()");
            WriteLine("{");
            Indent();
            WriteLine("// Check for compound statements first (if, while, for, etc.)");
            WriteLine("if (_parser._position < _tokens.Count)");
            WriteLine("{");
            Indent();
            WriteLine("var token = _tokens[_parser._position];");
            WriteLine("if (token.Type.ToString() == \"NAME\")");
            WriteLine("{");
            Indent();
            WriteLine("var tokenValue = token.Value?.ToString();");
            WriteLine("if (tokenValue == \"if\")");
            WriteLine("{");
            Indent();
            WriteLine("var ifStmt = _parser.ParseIfStatement();");
            WriteLine("if (ifStmt != null) return ifStmt;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("return ParsePegStmt();");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Close the EmbeddedPegInterpreter class
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generates proper PEG expression hierarchy with left recursion support
        /// Following python.gram structure: atom → primary → ... → expression
        /// </summary>
        private void GenerateExpressionHierarchy()
        {
            Console.WriteLine("[DEBUG] GenerateExpressionHierarchy called");
            WriteLine();
            WriteLine("// === PEG Expression Hierarchy with Left Recursion Support ===");
            WriteLine();

            // Generate core parsing methods from python.gram
            GenerateAtomParser();
            GeneratePrimaryParser();
            GeneratePowerParser();
            GenerateFactorParser();  // CPython 3.12: factor handles unary operators
            GenerateTermParser();
            GenerateSumParser();
            GenerateExpressionParser();

            // Generate star_expressions parser (for statement expressions)
            GenerateStarExpressionsParser();

            // Generate simple expression statement parser
            GenerateExpressionStmtParser();

            // CPython 3.12: if/while/for statements are now parsed via grammar-based compound_stmt
            // No need for manual generators - PegInterpreter handles them

            // Generate lambda parser
            GenerateLambdaParser();

            WriteLine("// === End Expression Hierarchy ===");
            WriteLine();
        }

        /// <summary>
        /// Generate atom parser: NAME | NUMBER | STRING | '(' expression ')'
        /// </summary>
        private void GenerateAtomParser()
        {
            Console.WriteLine("[DEBUG] GenerateAtomParser called");
            WriteLine("// atom: NAME | NUMBER | STRING | '(' expression ')'");
            WriteLine("protected GeneratedExpr? ParseAtom()");
            WriteLine("{");
            Indent();
            // Debug logging removed for performance
            WriteLine();
            WriteLine("if (CurrentToken == null) return null;");
            WriteLine();
            // Remove NL handling since NL tokens don't exist in our tokenizer
            // Only NEWLINE tokens exist and should be handled at statement level

            WriteLine("// Check for lambda expression first");
            WriteLine("if (CurrentToken.Type.ToString() == \"NAME\" && CurrentToken.Value == \"lambda\")");
            WriteLine("{");
            Indent();
            WriteLine("return ParseLambda();");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// NAME or special constants (True, False, None)");
            WriteLine("// CPython 3.12: True/False/None are keywords but also constant expressions");
            WriteLine("if (CurrentToken.Type.ToString() == \"NAME\")");
            WriteLine("{");
            Indent();
            WriteLine("var name = CurrentToken.Value;");
            WriteLine();
            WriteLine("// CPython 3.12: True, False, None are special constant keywords");
            WriteLine("if (name == \"True\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            WriteLine("return new GeneratedConstantExpr { Value = PyBool.True };");
            Dedent();
            WriteLine("}");
            WriteLine("if (name == \"False\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            WriteLine("return new GeneratedConstantExpr { Value = PyBool.False };");
            Dedent();
            WriteLine("}");
            WriteLine("if (name == \"None\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            WriteLine("return new GeneratedConstantExpr { Value = PyNone.Instance };");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: Other keywords cannot be used as identifiers");
            WriteLine("if (IsKeyword(name))");
            WriteLine("{");
            Indent();
            WriteLine("// Keyword found - not a valid identifier in this context");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("Advance();");
            WriteLine("return new GeneratedNameExpr { Id = name };");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// NUMBER - CPython 3.12: Parse to PyInt or PyFloat");
            WriteLine("if (CurrentToken.Type.ToString() == \"NUMBER\")");
            WriteLine("{");
            Indent();
            WriteLine("var numberStr = CurrentToken.Value;");
            WriteLine("Advance();");
            WriteLine("// CPython 3.12: Convert string to PyObject");
            WriteLine("PyObject numberValue;");
            WriteLine("if (int.TryParse(numberStr, out int intVal))");
            WriteLine("    numberValue = new PyInt(intVal);");
            WriteLine("else if (double.TryParse(numberStr, out double floatVal))");
            WriteLine("    numberValue = new PyFloat(floatVal);");
            WriteLine("else");
            WriteLine("    numberValue = new PyString(numberStr); // Fallback");
            WriteLine("return new GeneratedConstantExpr { Value = numberValue, Kind = \"number\" };");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// F-STRING (check before regular STRING)");
            WriteLine("if (CurrentToken.Type.ToString() == \"FSTRING_START\")");
            WriteLine("{");
            Indent();
            WriteLine("return Fstring();");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// STRING - CPython 3.12: Create PyString");
            WriteLine("if (CurrentToken.Type.ToString() == \"STRING\")");
            WriteLine("{");
            Indent();
            WriteLine("var str = CurrentToken.Value;");
            WriteLine("Advance();");
            WriteLine("return new GeneratedConstantExpr { Value = new PyString(str), Kind = \"string\" };");
            Dedent();
            WriteLine("}");
            WriteLine();


            WriteLine("// '[' [star_named_expressions] ']' - List literal or list comprehension");
            WriteLine("if (CurrentToken.Type.ToString() == \"OP\" && CurrentToken.Value == \"[\")");
            WriteLine("{");
            Indent();
            WriteLine("return ParseListOrListComp();");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// '{' [double_starred_kvpairs] '}' - Dictionary literal, set literal, or comprehensions");
            WriteLine("if (CurrentToken.Type.ToString() == \"OP\" && CurrentToken.Value == \"{\")");
            WriteLine("{");
            Indent();
            WriteLine("return ParseDictSetOrComp();");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// '(' [star_named_expressions] ')' - Tuple literal or parenthesized expression");
            WriteLine("// Note: Single element tuple requires comma: (x,)");
            WriteLine("if (CurrentToken.Type.ToString() == \"OP\" && CurrentToken.Value == \"(\")");
            WriteLine("{");
            Indent();
            WriteLine("var mark = Mark();");
            WriteLine("Advance(); // consume '('");
            WriteLine("var elements = new List<GeneratedExpr>();");
            WriteLine("bool hasComma = false;");
            WriteLine();
            WriteLine("// Handle empty tuple");
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \")\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume ')'");
            WriteLine("var seq = new GeneratedExprSeq();");
            WriteLine("seq.AddRange(elements);");
            WriteLine("return new GeneratedTupleExpr { Elements = seq };");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Parse tuple elements or single parenthesized expression");
            WriteLine("while (CurrentToken != null && !(CurrentToken.Type.ToString() == \"OP\" && CurrentToken.Value == \")\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Try named_expression (assignment_expression | expression !':=')");
            WriteLine("GeneratedExpr? element = AssignmentExpression();");
            WriteLine("if (element == null)");
            WriteLine("{");
            Indent();
            WriteLine("// Fallback to regular expression if not assignment expression");
            WriteLine("element = Expression();");
            Dedent();
            WriteLine("}");
            WriteLine("if (element != null)");
            WriteLine("{");
            Indent();
            WriteLine("elements.Add(element);");
            WriteLine();
            WriteLine("// CPython 3.12: Check for 'for' keyword → Generator Expression");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NAME\" && CurrentToken?.Value == \"for\")");
            WriteLine("{");
            Indent();
            WriteLine("// This is a generator expression: (element for ...)");
            WriteLine("var generators = ParseForIfClauses();");
            WriteLine("if (generators != null)");
            WriteLine("{");
            Indent();
            WriteLine("// Consume closing ')'");
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \")\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            WriteLine("return new GeneratedGeneratorExpExpr { Element = element, Generators = (List<object>)generators };");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for comma separator");
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \",\")");
            WriteLine("{");
            Indent();
            WriteLine("hasComma = true;");
            WriteLine("Advance(); // consume ','");
            WriteLine("// Allow trailing comma before ')'");
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \")\")");
            WriteLine("{");
            Indent();
            WriteLine("break;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("// No comma, expect end of tuple/expression");
            WriteLine("break;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Consume closing ')'");
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \")\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            WriteLine("// Return tuple only if multiple elements or trailing comma");
            WriteLine("if (elements.Count > 1 || hasComma)");
            WriteLine("{");
            Indent();
            WriteLine("var seq = new GeneratedExprSeq();");
            WriteLine("seq.AddRange(elements);");
            WriteLine("return new GeneratedTupleExpr { Elements = seq };");
            Dedent();
            WriteLine("}");
            WriteLine("// Single element without comma is parenthesized expression");
            WriteLine("else if (elements.Count == 1)");
            WriteLine("{");
            Indent();
            WriteLine("return elements[0];");
            Dedent();
            WriteLine("}");
            WriteLine("// Empty parentheses is empty tuple");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("return new GeneratedTupleExpr { Elements = new GeneratedExprSeq() };");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("Reset(mark);");
            WriteLine("return null; // Malformed tuple/parenthesized expression");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate primary parser with left recursion: primary '.' NAME | primary '(' args ')' | atom
        /// Uses iterative expansion to handle left recursion properly
        /// </summary>
        private void GeneratePrimaryParser()
        {
            WriteLine("// primary: primary '.' NAME | primary '[' slices ']' | primary '(' [arguments] ')' | atom");
            WriteLine("// Implemented with left recursion support");
            WriteLine("protected GeneratedExpr? ParsePrimary()");
            WriteLine("{");
            Indent();
            // WriteLine("Console.WriteLine($\"[DEBUG] ParsePrimary at position {_position}\");");
            WriteLine();
            WriteLine("// Check for tokens that should stop parsing");
            WriteLine("if (CurrentToken == null) return null;");
            WriteLine("if (CurrentToken.Type == TokenType.DEDENT) return null;");
            WriteLine("if (CurrentToken.Type == TokenType.ENDMARKER) return null;");
            WriteLine();

            WriteLine("// Check memoization for left recursion");
            WriteLine("var memoKey = \"primary\";");
            WriteLine("var memoResult = GetMemo<GeneratedExpr>(memoKey);");
            WriteLine("if (memoResult != null)");
            WriteLine("{");
            Indent();
            // WriteLine("Console.WriteLine($\"[DEBUG] Primary: Using memoized result\");");
            WriteLine("return memoResult;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Start with atom (base case)");
            WriteLine("var result = ParseAtom();");
            WriteLine("if (result == null)");
            WriteLine("{");
            Indent();
            WriteLine("SetMemo<object>(memoKey, null);");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Iteratively expand with left-recursive rules");
            WriteLine("bool expanded = true;");
            WriteLine("while (expanded)");
            WriteLine("{");
            Indent();
            WriteLine("expanded = false;");
            WriteLine();

            WriteLine("// Try: primary genexp (Grammar line 819: primary genexp → Call)");
            WriteLine("// Distinguish from function call: after '(', check if 'for' follows first expression");
            WriteLine("if (!expanded && CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"(\")");
            WriteLine("{");
            Indent();
            WriteLine("var mark = Mark();");
            WriteLine("Advance(); // consume '('");
            WriteLine();
            WriteLine("// Parse first expression");
            WriteLine("var firstExpr = Expression();");
            WriteLine();
            WriteLine("// Check if this is genexp pattern: expr 'for' ...");
            WriteLine("if (firstExpr != null && CurrentToken?.Type.ToString() == \"NAME\" && CurrentToken?.Value == \"for\")");
            WriteLine("{");
            Indent();
            WriteLine("// This is genexp: (expr for ...)");
            WriteLine("var generators = ParseForIfClauses();");
            WriteLine();
            WriteLine("if (generators != null && CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \")\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume ')'");
            WriteLine();
            WriteLine("// Create GeneratorExp node");
            WriteLine("var genexp = new GeneratedGeneratorExpExpr { Element = firstExpr, Generators = (List<object>)generators };");
            WriteLine();
            WriteLine("// Create Call(primary, [genexp]) - Grammar line 819");
            WriteLine("var callArgs = new GeneratedExprSeq { genexp };");
            WriteLine("result = new GeneratedCallExpr { Func = result, Args = callArgs };");
            WriteLine();
            WriteLine("expanded = true;");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("Reset(mark); // parse error, backtrack");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("Reset(mark); // not genexp, try function call next");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Try: primary '(' [arguments] ')' (function call)");
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"(\")");
            WriteLine("{");
            Indent();
            WriteLine("var mark = Mark();");
            WriteLine("Advance(); // consume '('");
            WriteLine();
            WriteLine("var args = new List<object>();");
            WriteLine("// Parse arguments (simplified)");
            WriteLine("while (CurrentToken != null && !(CurrentToken.Type.ToString() == \"OP\" && CurrentToken.Value == \")\"))");
            WriteLine("{");
            Indent();
            WriteLine("var arg = Expression();");
            WriteLine("if (arg != null)");
            WriteLine("{");
            Indent();
            WriteLine("args.Add(arg);");
            WriteLine("// Skip comma if present");
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \",\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("else break;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \")\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume ')'");
            WriteLine("// Convert List<object> to GeneratedExprSeq");
            WriteLine("var exprArgs = new GeneratedExprSeq();");
            WriteLine("exprArgs.AddRange(args.Cast<GeneratedExpr>());");
            WriteLine("result = new GeneratedCallExpr { Func = result, Args = exprArgs };");
            WriteLine("expanded = true;");
            // WriteLine("Console.WriteLine($\"[DEBUG] Primary: Created function call\");");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("Reset(mark); // backtrack on failure");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Try: primary '.' NAME (attribute access)");
            WriteLine("if (!expanded && CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \".\")");
            WriteLine("{");
            Indent();
            WriteLine("var mark = Mark();");
            WriteLine("Advance(); // consume '.'");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NAME\")");
            WriteLine("{");
            Indent();
            WriteLine("var attr = CurrentToken.Value;");
            WriteLine("Advance();");
            WriteLine("result = new GeneratedAttributeExpr { Value = result, Attr = attr };");
            WriteLine("expanded = true;");
            // WriteLine("Console.WriteLine($\"[DEBUG] Primary: Created attribute access\");");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("Reset(mark); // backtrack on failure");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Try: primary '[' slices ']' (subscript access)");
            WriteLine("if (!expanded && CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"[\")");
            WriteLine("{");
            Indent();
            WriteLine("var mark = Mark();");
            WriteLine("Advance(); // consume '['");
            WriteLine();
            WriteLine("// Parse slice/index expression");
            WriteLine("var slice = Expression();");
            WriteLine("if (slice != null && CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"]\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume ']'");
            WriteLine("result = new GeneratedSubscriptExpr { Value = result, Slice = slice };");
            WriteLine("expanded = true;");
            // WriteLine("Console.WriteLine($\"[DEBUG] Primary: Created subscript access\");");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("Reset(mark); // backtrack on failure");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("SetMemo<object>(memoKey, result);");
            // WriteLine("Console.WriteLine($\"[DEBUG] Primary result: {result?.GetType().Name}\");");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate star_expressions parser: expression (',' expression)* [',']
        /// This handles expression statements like function calls
        /// </summary>
        private void GenerateStarExpressionsParser()
        {
            WriteLine("// star_expressions: expression (',' expression)* [',']");
            WriteLine("private GeneratedExpr? ParseStarExpressions()");
            WriteLine("{");
            Indent();
            // WriteLine("Console.WriteLine($\"[DEBUG] ParseStarExpressions at position {_position}\");");
            WriteLine();
            WriteLine("var expr = Expression();");
            WriteLine("if (expr == null) return null;");
            WriteLine();
            WriteLine("// For now, just return single expression");
            WriteLine("// TODO: Handle multiple expressions separated by commas");
            WriteLine("return expr;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate comparison parser: comparison op sum | sum
        /// Handles comparison operations with proper precedence
        /// </summary>
        private void GenerateComparisonParser()
        {
            WriteLine("// comparison: comparison ('=='|'!='|'<'|'<='|'>'|'>=') sum | sum");
            WriteLine("private GeneratedExpr? ParseComparison()");
            WriteLine("{");
            Indent();
            WriteLine("return ParseComparisonTemplate();");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate Comparison method for expression hierarchy
        /// Based on grammar rule: comparison[expr_ty]: bitwise_or (('==' | '!=' | '<=' | '>=' | '<' | '>' | 'in' | 'not' 'in' | 'is' | 'is' 'not') bitwise_or)*
        /// </summary>
        private void GenerateComparisonMethod()
        {
            WriteLine("/// <summary>");
            WriteLine("/// comparison[expr_ty]: bitwise_or (('==' | '!=' | '<=' | '>=' | '<' | '>' | 'in' | 'not' 'in' | 'is' | 'is' 'not') bitwise_or)*");
            WriteLine("/// Handles comparison operations");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? Comparison()");
            WriteLine("{");
            Indent();

            WriteLine("var left = ParseSum(); // Start with sum for arithmetic precedence");
            WriteLine("if (left == null) return null;");
            WriteLine();

            WriteLine("var comparisons = new List<object>();");
            WriteLine("var operators = new List<string>();");
            WriteLine();

            WriteLine("// Look for comparison operators");
            WriteLine("while (CurrentToken != null)");
            WriteLine("{");
            Indent();

            WriteLine("string? op = null;");
            WriteLine();

            WriteLine("if (CurrentToken.Type == TokenType.OP)");
            WriteLine("{");
            Indent();
            WriteLine("switch (CurrentToken.Value)");
            WriteLine("{");
            Indent();
            WriteLine("case \"==\": op = \"Eq\"; Advance(); goto operator_found;");
            WriteLine("case \"!=\": op = \"NotEq\"; Advance(); goto operator_found;");
            WriteLine("case \"<\": op = \"Lt\"; Advance(); goto operator_found;");
            WriteLine("case \"<=\": op = \"LtE\"; Advance(); goto operator_found;");
            WriteLine("case \">\": op = \"Gt\"; Advance(); goto operator_found;");
            WriteLine("case \">=\": op = \"GtE\"; Advance(); goto operator_found;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("else if (CurrentToken.Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("// CPython 3.12: Handle 'not in' and 'is not' as 2-token sequences");
            WriteLine("if (CurrentToken.Value == \"not\")");
            WriteLine("{");
            Indent();
            WriteLine("var nextPos = _position + 1;");
            WriteLine("if (nextPos < _tokens.Count && _tokens[nextPos].Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("if (_tokens[nextPos].Value == \"in\")");
            WriteLine("{");
            Indent();
            WriteLine("op = \"NotIn\";");
            WriteLine("Advance(); // consume 'not'");
            WriteLine("Advance(); // consume 'in'");
            WriteLine("goto operator_found;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("else if (CurrentToken.Value == \"is\")");
            WriteLine("{");
            Indent();
            WriteLine("var nextPos = _position + 1;");
            WriteLine("if (nextPos < _tokens.Count && _tokens[nextPos].Type == TokenType.NAME && _tokens[nextPos].Value == \"not\")");
            WriteLine("{");
            Indent();
            WriteLine("op = \"IsNot\";");
            WriteLine("Advance(); // consume 'is'");
            WriteLine("Advance(); // consume 'not'");
            WriteLine("goto operator_found;");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("op = \"Is\";");
            WriteLine("Advance(); // consume 'is'");
            WriteLine("goto operator_found;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("else if (CurrentToken.Value == \"in\")");
            WriteLine("{");
            Indent();
            WriteLine("op = \"In\";");
            WriteLine("Advance(); // consume 'in'");
            WriteLine("goto operator_found;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("operator_found:");
            WriteLine();

            WriteLine("if (op == null) break; // No more comparison operators");
            WriteLine();

            WriteLine("// Note: operator already consumed by specific handlers above");
            WriteLine("var right = ParseSum();");
            WriteLine("if (right == null)");
            WriteLine("{");
            Indent();
            WriteLine("// Error: expected expression after comparison operator");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("comparisons.Add(right);");
            WriteLine("operators.Add(op);");

            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// If we found comparison operators, create Compare expression");
            WriteLine("if (comparisons.Count > 0)");
            WriteLine("{");
            Indent();
            WriteLine("var comparatorSeq = new GeneratedExprSeq();");
            WriteLine("comparatorSeq.AddRange(comparisons.Cast<GeneratedExpr>());");
            WriteLine("return new GeneratedCompareExpr");
            WriteLine("{");
            Indent();
            WriteLine("Left = (GeneratedExpr)left,");
            WriteLine("Ops = operators.Cast<string>().ToList(),");
            WriteLine("Comparators = comparatorSeq");
            Dedent();
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// No comparison operators - return left operand");
            WriteLine("if (left is GeneratedExpr genExpr)");
            WriteLine("{");
            Indent();
            WriteLine("return genExpr;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("return (GeneratedExpr)left;");

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate expression parser following CPython 3.12 hierarchy
        /// expression: disjunction | lambdef | conditional expression
        /// </summary>
        private void GenerateExpressionParser()
        {
            WriteLine("// expression: disjunction | a=disjunction 'if' b=disjunction 'else' c=expression");
            WriteLine("public GeneratedExpr? Expression()");
            WriteLine("{");
            Indent();
            WriteLine("// Delegate to existing disjunction parser");
            WriteLine("return Disjunction();");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate power parser: await_primary '**' factor | await_primary
        /// Handles power operator with right recursion
        /// </summary>
        private void GeneratePowerParser()
        {
            WriteLine("// power[expr_ty]: a=await_primary '**' b=factor | await_primary");
            WriteLine("protected GeneratedExpr? ParsePower()");
            WriteLine("{");
            Indent();
            WriteLine();
            WriteLine("// Check for tokens that should stop parsing");
            WriteLine("if (CurrentToken == null) return null;");
            WriteLine("if (CurrentToken.Type == TokenType.DEDENT) return null;");
            WriteLine("if (CurrentToken.Type == TokenType.ENDMARKER) return null;");
            WriteLine();

            WriteLine("// Start with await_primary (base case)");
            WriteLine("var left = AwaitPrimary();");
            WriteLine("if (left == null)");
            WriteLine("{");
            Indent();
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Check for '**' power operator");
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"**\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '**'");
            WriteLine("var right = ParsePower(); // Simplified - will be fixed in full factor implementation");
            WriteLine("if (right != null)");
            WriteLine("{");
            Indent();
            WriteLine("return new GeneratedBinOpExpr");
            WriteLine("{");
            WriteLine("    Left = (GeneratedExpr)left,");
            WriteLine("    Op = \"Pow\",");
            WriteLine("    Right = (GeneratedExpr)right");
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("throw new InvalidOperationException(\"Expected factor after '**'\");");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// No power operator, return await_primary result");
            WriteLine("return left;");

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate factor parser: '+' factor | '-' factor | '~' factor | power
        /// CPython 3.12: Handles unary operators
        /// </summary>
        private void GenerateFactorParser()
        {
            WriteLine("// factor: '+' factor | '-' factor | '~' factor | power");
            WriteLine("protected GeneratedExpr? ParseFactor()");
            WriteLine("{");
            Indent();
            WriteLine();
            WriteLine("// Check for tokens that should stop parsing");
            WriteLine("if (CurrentToken == null) return null;");
            WriteLine("if (CurrentToken.Type == TokenType.DEDENT) return null;");
            WriteLine("if (CurrentToken.Type == TokenType.ENDMARKER) return null;");
            WriteLine();
            WriteLine("// Check for unary operators");
            WriteLine("if (CurrentToken?.Type == TokenType.OP)");
            WriteLine("{");
            Indent();
            WriteLine("if (CurrentToken.Value == \"+\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '+'");
            WriteLine("var operand = ParseFactor();");
            WriteLine("if (operand != null)");
            WriteLine("{");
            Indent();
            WriteLine("return new GeneratedUnaryOpExpr");
            WriteLine("{");
            WriteLine("    Op = \"UAdd\",");
            WriteLine("    Operand = (GeneratedExpr)operand");
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("if (CurrentToken.Value == \"-\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '-'");
            WriteLine("var operand = ParseFactor();");
            WriteLine("if (operand != null)");
            WriteLine("{");
            Indent();
            WriteLine("return new GeneratedUnaryOpExpr");
            WriteLine("{");
            WriteLine("    Op = \"USub\",");
            WriteLine("    Operand = (GeneratedExpr)operand");
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("if (CurrentToken.Value == \"~\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '~'");
            WriteLine("var operand = ParseFactor();");
            WriteLine("if (operand != null)");
            WriteLine("{");
            Indent();
            WriteLine("return new GeneratedUnaryOpExpr");
            WriteLine("{");
            WriteLine("    Op = \"Invert\",");
            WriteLine("    Operand = (GeneratedExpr)operand");
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// No unary operator, parse power");
            WriteLine("return ParsePower();");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate term parser: term '*' factor | term '/' factor | factor
        /// Handles multiplication and division with left recursion
        /// </summary>
        private void GenerateTermParser()
        {
            WriteLine("// term: term '*' factor | term '/' factor | factor  (CPython 3.12)");
            WriteLine("protected GeneratedExpr? ParseTerm()");
            WriteLine("{");
            Indent();
            WriteLine();
            WriteLine("// Check for tokens that should stop parsing");
            WriteLine("if (CurrentToken == null) return null;");
            WriteLine("if (CurrentToken.Type == TokenType.DEDENT) return null;");
            WriteLine("if (CurrentToken.Type == TokenType.ENDMARKER) return null;");
            WriteLine();

            // Start with factor (base case) - CPython 3.12
            WriteLine("var result = ParseFactor();");
            WriteLine("if (result == null) return null;");
            WriteLine();

            // Iteratively handle left recursion for * and /
            WriteLine("// Handle left recursion iteratively");
            WriteLine("while (true)");
            WriteLine("{");
            Indent();
            WriteLine("var mark = Mark();");
            WriteLine();

            // Try: term '*' factor (CPython 3.12)
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"*\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '*'");
            WriteLine("var right = ParseFactor();");
            WriteLine("if (right != null)");
            WriteLine("{");
            Indent();
            WriteLine("result = new GeneratedBinOpExpr");
            WriteLine("{");
            WriteLine("    Left = (GeneratedExpr)result,");
            WriteLine("    Op = \"Mult\",");
            WriteLine("    Right = (GeneratedExpr)right");
            WriteLine("};");
            // WriteLine("Console.WriteLine($\"[DEBUG] ParseTerm: Created multiplication\");");
            WriteLine("continue;");
            Dedent();
            WriteLine("}");
            WriteLine("Reset(mark);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Try: term '/' factor (CPython 3.12)
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"/\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '/'");
            WriteLine("var right = ParseFactor();");
            WriteLine("if (right != null)");
            WriteLine("{");
            Indent();
            WriteLine("result = new GeneratedBinOpExpr");
            WriteLine("{");
            WriteLine("    Left = (GeneratedExpr)result,");
            WriteLine("    Op = \"Div\",");
            WriteLine("    Right = (GeneratedExpr)right");
            WriteLine("};");
            // WriteLine("Console.WriteLine($\"[DEBUG] ParseTerm: Created division\");");
            WriteLine("continue;");
            Dedent();
            WriteLine("}");
            WriteLine("Reset(mark);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Try: term '//' factor (floor division, CPython 3.12)
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"//\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '//'");
            WriteLine("var right = ParseFactor();");
            WriteLine("if (right != null)");
            WriteLine("{");
            Indent();
            WriteLine("result = new GeneratedBinOpExpr");
            WriteLine("{");
            WriteLine("    Left = (GeneratedExpr)result,");
            WriteLine("    Op = \"FloorDiv\",");
            WriteLine("    Right = (GeneratedExpr)right");
            WriteLine("};");
            WriteLine("continue;");
            Dedent();
            WriteLine("}");
            WriteLine("Reset(mark);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Try: term '%' factor (modulo, CPython 3.12)
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"%\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '%'");
            WriteLine("var right = ParseFactor();");
            WriteLine("if (right != null)");
            WriteLine("{");
            Indent();
            WriteLine("result = new GeneratedBinOpExpr");
            WriteLine("{");
            WriteLine("    Left = (GeneratedExpr)result,");
            WriteLine("    Op = \"Mod\",");
            WriteLine("    Right = (GeneratedExpr)right");
            WriteLine("};");
            WriteLine("continue;");
            Dedent();
            WriteLine("}");
            WriteLine("Reset(mark);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Try: term '@' factor (matrix multiplication, CPython 3.12)
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"@\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '@'");
            WriteLine("var right = ParseFactor();");
            WriteLine("if (right != null)");
            WriteLine("{");
            Indent();
            WriteLine("result = new GeneratedBinOpExpr");
            WriteLine("{");
            WriteLine("    Left = (GeneratedExpr)result,");
            WriteLine("    Op = \"MatMult\",");
            WriteLine("    Right = (GeneratedExpr)right");
            WriteLine("};");
            WriteLine("continue;");
            Dedent();
            WriteLine("}");
            WriteLine("Reset(mark);");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// No more operations");
            WriteLine("break;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // WriteLine("Console.WriteLine($\"[DEBUG] ParseTerm result: {result?.GetType().Name}\");");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate sum parser: sum '+' term | sum '-' term | term
        /// Handles arithmetic addition and subtraction with left recursion
        /// </summary>
        private void GenerateSumParser()
        {
            WriteLine("// sum: sum '+' term | sum '-' term | term");
            WriteLine("protected GeneratedExpr? ParseSum()");
            WriteLine("{");
            Indent();
            // WriteLine("Console.WriteLine($\"[DEBUG] ParseSum at position {_position}\");");
            WriteLine();
            WriteLine("// Check for tokens that should stop parsing");
            WriteLine("if (CurrentToken == null) return null;");
            WriteLine("if (CurrentToken.Type == TokenType.DEDENT) return null;");
            WriteLine("if (CurrentToken.Type == TokenType.ENDMARKER) return null;");
            WriteLine();

            // Start with term (base case)
            WriteLine("var result = ParseTerm();");
            WriteLine("if (result == null) return null;");
            WriteLine();

            // Iteratively handle left recursion for + and -
            WriteLine("// Handle left recursion iteratively");
            WriteLine("while (true)");
            WriteLine("{");
            Indent();
            WriteLine("var mark = Mark();");
            WriteLine();

            // Try: sum '+' term
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"+\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '+'");
            WriteLine("var right = ParseTerm();");
            WriteLine("if (right != null)");
            WriteLine("{");
            Indent();
            WriteLine("result = new GeneratedBinOpExpr");
            WriteLine("{");
            WriteLine("    Left = (GeneratedExpr)result,");
            WriteLine("    Op = \"Add\",");
            WriteLine("    Right = (GeneratedExpr)right");
            WriteLine("};");
            // WriteLine("Console.WriteLine($\"[DEBUG] ParseSum: Created addition\");");
            WriteLine("continue;");
            Dedent();
            WriteLine("}");
            WriteLine("Reset(mark);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Try: sum '-' term
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"-\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '-'");
            WriteLine("var right = ParseTerm();");
            WriteLine("if (right != null)");
            WriteLine("{");
            Indent();
            WriteLine("result = new GeneratedBinOpExpr");
            WriteLine("{");
            WriteLine("    Left = (GeneratedExpr)result,");
            WriteLine("    Op = \"Sub\",");
            WriteLine("    Right = (GeneratedExpr)right");
            WriteLine("};");
            // WriteLine("Console.WriteLine($\"[DEBUG] ParseSum: Created subtraction\");");
            WriteLine("continue;");
            Dedent();
            WriteLine("}");
            WriteLine("Reset(mark);");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// No more operations");
            WriteLine("break;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // WriteLine("Console.WriteLine($\"[DEBUG] ParseSum result: {result?.GetType().Name}\");");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }


        /// <summary>
        /// Generate expression statement parser following PEG grammar
        /// </summary>
        private void GenerateExpressionStmtParser()
        {
            WriteLine("// Parse expression statement following PEG grammar");
            WriteLine("public GeneratedStmt? ParseExpressionStmt()");
            WriteLine("{");
            Indent();
            // WriteLine("Console.WriteLine($\"[DEBUG] ParseExpressionStmt at position {_position}\");");
            WriteLine();
            WriteLine("var expr = ParseStarExpressions();");
            WriteLine("if (expr != null)");
            WriteLine("{");
            Indent();
            WriteLine("var stmt = new GeneratedExprStmt");
            WriteLine("{");
            Indent();
            WriteLine("Value = (GeneratedExpr)expr");
            Dedent();
            WriteLine("};");
            // WriteLine("Console.WriteLine($\"[DEBUG] Created expression statement: {expr}\");");
            WriteLine("return stmt;");
            Dedent();
            WriteLine("}");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        // CPython 3.12: if/while/for statements removed - now handled by grammar-based compound_stmt parsing
        // ParseIfStatement(), ParseWhileStatement(), ParseForStatement() are no longer generated
        // PegInterpreter processes python.gram rules: if_stmt, while_stmt, for_stmt

        /// <summary>
        /// Convert parsed Grammar AST to dictionary format for EmbeddedGrammar
        /// </summary>
        private Dictionary<string, string> ConvertGrammarToDictionary(Grammar.Grammar grammar)
        {
            var rules = new Dictionary<string, string>();

            foreach (var rule in grammar.Rules)
            {
                var alternatives = new List<string>();

                foreach (var alt in rule.Alternatives)
                {
                    var items = new List<string>();
                    foreach (var item in alt.Items)
                    {
                        items.Add(ConvertAtomToString(item.Atom));
                    }
                    alternatives.Add(string.Join(" ", items));
                }

                rules[rule.Name] = string.Join(" | ", alternatives);
            }

            return rules;
        }

        /// <summary>
        /// Convert a single Atom to its string representation
        /// </summary>
        private string ConvertAtomToString(Atom atom)
        {
            switch (atom)
            {
                case RuleRef ruleRef:
                    return ruleRef.Name;
                case StringLiteral stringLit:
                    return $"'{stringLit.Value}'";
                case Grammar.Group group:
                    var alts = group.Alternatives.Select(alt =>
                        string.Join(" ", alt.Items.Select(item => ConvertAtomToString(item.Atom))));
                    return $"({string.Join(" | ", alts)})";
                case Optional optional:
                    return $"[{ConvertAtomToString(optional.Expression)}]";
                case ZeroOrMore zeroOrMore:
                    return $"{ConvertAtomToString(zeroOrMore.Expression)}*";
                case OneOrMore oneOrMore:
                    return $"{ConvertAtomToString(oneOrMore.Expression)}+";
                case PositiveLookahead posLook:
                    return $"&{ConvertAtomToString(posLook.Expression)}";
                case NegativeLookahead negLook:
                    return $"!{ConvertAtomToString(negLook.Expression)}";
                case Cut cut:
                    return $"~{ConvertAtomToString(cut.Expression)}";
                default:
                    return atom.ToString();
            }
        }

        /// <summary>
        /// Generate Python grammar parsing methods based on python.gram
        /// </summary>
        private void GeneratePythonGrammarMethods()
        {
            WriteLine();
            WriteLine("// ========================================");
            WriteLine("// Generated Python Grammar Parsing Methods");
            WriteLine("// ========================================");
            WriteLine();

            // Generate helper methods first (including SkipNL)
            GenerateHelperMethods();

            // Generate entry point methods from @trailer
            GenerateTrailerMethods();

            // CPython 3.12: ALL rules are auto-generated from grammar
            // No manual expression hierarchy generation needed
            // GenerateExpressionHierarchy();

            // CPython 3.12: All grammar rules are already generated in GenerateParserMethods()
            // No need for separate GenerateSpecificGrammarRules()
            // GenerateSpecificGrammarRules();

            // CPython 3.12: ALL methods are auto-generated from grammar
            // No manual method generation needed
            // GenerateFunctionDefMethods();
            // GenerateStatementMethods();
            // GenerateBasicMethods();

            // CPython 3.12: Generate embedded types (PegInterpreter, EmbeddedGrammar, etc.)
            // This makes PyParser.cs completely independent from SharpPy.PegGenerator
            Console.WriteLine("[CODEGEN] Generating embedded types (PegInterpreter, Grammar, etc.)");
            GenerateEmbeddedTypes();
        }

        /// <summary>
        /// Generate specific grammar rules that are missing from the hardcoded expression hierarchy
        /// </summary>
        private void GenerateSpecificGrammarRules()
        {
            WriteLine();
            WriteLine("// === All Grammar Rules from python.gram (CPython 3.12 PEG Parser) ===");
            WriteLine();

            // CPython 3.12: Generate ALL rules from python.gram
            Console.WriteLine($"[CODEGEN] Generating all {_grammar.Rules.Count} rules from python.gram");

            int generatedCount = 0;
            int skippedCount = 0;

            foreach (var rule in _grammar.Rules)
            {
                try
                {
                    Console.WriteLine($"[CODEGEN] Generating rule {generatedCount + 1}/{_grammar.Rules.Count}: {rule.Name}");
                    GenerateRuleMethod(rule);
                    generatedCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to generate rule '{rule.Name}': {ex.Message}");
                    skippedCount++;

                    // Generate fallback method that uses PegInterpreter
                    WriteLine($"// ERROR: Failed to generate {rule.Name} - using fallback");
                    WriteLine($"// Error: {ex.Message}");
                }
            }

            Console.WriteLine($"[CODEGEN] Completed: {generatedCount} rules generated, {skippedCount} skipped");
            WriteLine();
            WriteLine($"// Summary: {generatedCount}/{_grammar.Rules.Count} rules generated successfully");
            WriteLine();

            // Generate assignment_expression rule directly for walrus operator
            Console.WriteLine("[CODEGEN] Generating assignment_expression rule for walrus operator");
            GenerateAssignmentExpressionMethod();

            // Generate expression hierarchy: disjunction, conjunction, inversion, comparison
            Console.WriteLine("[CODEGEN] Generating expression hierarchy: disjunction, conjunction, inversion, comparison");
            GenerateDisjunctionMethod();
            GenerateConjunctionMethod();
            GenerateInversionMethod();
            GenerateComparisonMethod();

            // Generate f-string support methods
            Console.WriteLine("[CODEGEN] Generating f-string support methods");
            GenerateFStringMethods();

            // Generate await_primary method for async/await support
            Console.WriteLine("[CODEGEN] Generating await_primary method for async/await support");
            GenerateAwaitPrimaryGrammarMethod();

            // Generate augassign method for augmented assignment operators
            Console.WriteLine("[CODEGEN] Generating augassign method for augmented assignment operators");
            GenerateAugAssignMethod("object?");

            // Note: ParseAssignment is implemented in PyParserBase.cs (CPython 3.12 compatible)
            // Not generated here to avoid confusion and ensure proper context handling

            WriteLine("// === End Specific Grammar Rules ===");
            WriteLine();
        }

        /// <summary>
        /// Generate assignment_expression method for walrus operator (:=)
        /// Based on grammar rule: assignment_expression[expr_ty]: | a=NAME ':=' ~ b=expression
        /// </summary>
        private void GenerateAssignmentExpressionMethod()
        {
            WriteLine("/// <summary>");
            WriteLine("/// assignment_expression[expr_ty]: | a=NAME ':=' ~ b=expression");
            WriteLine("/// Handles the walrus operator (:=) for assignment expressions");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? AssignmentExpression()");
            WriteLine("{");
            Indent();

            WriteLine("// Try to parse walrus operator: NAME := expression");
            WriteLine("var startPos = _position;");
            WriteLine();

            WriteLine("// Handle parenthesized assignment expression: (NAME := expression)");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \"(\")");
            WriteLine("{");
            Indent();
            WriteLine("var parenStart = _position;");
            WriteLine("Advance(); // consume '('");
            WriteLine();
            WriteLine("// Check if this is an assignment expression inside parentheses");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("var namePos = _position;");
            WriteLine("var name = CurrentToken.Value;");
            WriteLine("Advance(); // consume NAME");
            WriteLine();
            WriteLine("// Check for ':=' operator");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \":=\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume ':='");
            WriteLine();
            WriteLine("// Parse the expression on the right side");
            WriteLine("var rightExpr = ParseSum();");
            WriteLine("if (rightExpr != null)");
            WriteLine("{");
            Indent();
            WriteLine("// Expect closing parenthesis");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \")\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume ')'");
            WriteLine("// Create named expression AST node");
            WriteLine("var namedExpr = new GeneratedNamedExprExpr");
            WriteLine("{");
            Indent();
            WriteLine("Target = new GeneratedNameExpr { Id = name },");
            WriteLine("Value = (GeneratedExpr)rightExpr");
            Dedent();
            WriteLine("};");
            WriteLine("return namedExpr;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Not assignment expression in parentheses - backtrack to start");
            WriteLine("_position = startPos;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Check for NAME token (original logic)
            WriteLine("// Direct assignment expression: a=NAME ':=' ~ b=expression");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("var name = CurrentToken.Value;");
            WriteLine("var nameToken = CurrentToken;");
            WriteLine("Advance(); // consume NAME");
            WriteLine();

            // Check for ':=' operator
            WriteLine("// Check for ':=' operator");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \":=\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume ':='");
            WriteLine();

            // Parse expression on the right side
            WriteLine("// Parse the expression on the right side (b=expression)");
            WriteLine("// Note: To avoid circular call, parse at disjunction level (lower than assignment_expression)");
            WriteLine("var rightExpr = ParseSum(); // Use ParseSum for now, should be disjunction in full implementation");
            WriteLine("if (rightExpr != null)");
            WriteLine("{");
            Indent();
            WriteLine("// Create named expression AST node");
            WriteLine("var namedExpr = new GeneratedNamedExprExpr");
            WriteLine("{");
            Indent();
            WriteLine("Target = new GeneratedNameExpr { Id = name },");
            WriteLine("Value = (GeneratedExpr)rightExpr");
            Dedent();
            WriteLine("};");
            WriteLine("return namedExpr;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Right side expression failed - backtrack");
            WriteLine("_position = startPos;");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Not ':=' operator - backtrack");
            WriteLine("_position = startPos;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Fall back to regular expression
            WriteLine("// Fallback: if not walrus operator, parse at lower level of expression hierarchy");
            WriteLine("// This handles the case where assignment_expression -> expression (non-assignment case)");
            WriteLine("var fallbackExpr = Disjunction();");
            WriteLine("if (fallbackExpr != null)");
            WriteLine("{");
            Indent();
            WriteLine("// Wrap the result in a GeneratedExpr if it's not already one");
            WriteLine("if (fallbackExpr is GeneratedExpr genExpr)");
            WriteLine("{");
            Indent();
            WriteLine("return genExpr;");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("// Return the fallbackExpr directly without wrapping");
            WriteLine("return fallbackExpr;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("return null;");

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate disjunction method for 'or' expressions
        /// Based on grammar rule: disjunction[expr_ty] (memo): | a=conjunction b=('or' c=conjunction { c })+ | conjunction
        /// </summary>
        private void GenerateDisjunctionMethod()
        {
            WriteLine("/// <summary>");
            WriteLine("/// disjunction[expr_ty] (memo): | a=conjunction b=('or' c=conjunction { c })+ | conjunction");
            WriteLine("/// Handles 'or' Boolean operations");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? Disjunction()");
            WriteLine("{");
            Indent();

            WriteLine("// Try to parse: a=conjunction b=('or' c=conjunction { c })+");
            WriteLine("var left = Conjunction();");
            WriteLine("if (left == null) return null;");
            WriteLine();

            WriteLine("var orExpressions = new List<object>();");
            WriteLine("orExpressions.Add(left);");
            WriteLine();

            WriteLine("// Look for 'or' followed by conjunction");
            WriteLine("while (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"or\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume 'or'");
            WriteLine("var right = Conjunction();");
            WriteLine("if (right == null)");
            WriteLine("{");
            Indent();
            WriteLine("// Error: expected conjunction after 'or'");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine("orExpressions.Add(right);");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// If we found 'or' expressions, create BoolOp");
            WriteLine("if (orExpressions.Count > 1)");
            WriteLine("{");
            Indent();
            WriteLine("var valueSeq = new GeneratedExprSeq();");
            WriteLine("valueSeq.AddRange(orExpressions.Cast<GeneratedExpr>());");
            WriteLine("return new GeneratedBoolOpExpr");
            WriteLine("{");
            Indent();
            WriteLine("Op = \"Or\",");
            WriteLine("Values = valueSeq");
            Dedent();
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Single conjunction - return it");
            WriteLine("return left;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate conjunction method for 'and' expressions
        /// Based on grammar rule: conjunction[expr_ty] (memo): | a=inversion b=('and' c=inversion { c })+ | inversion
        /// </summary>
        private void GenerateConjunctionMethod()
        {
            WriteLine("/// <summary>");
            WriteLine("/// conjunction[expr_ty] (memo): | a=inversion b=('and' c=inversion { c })+ | inversion");
            WriteLine("/// Handles 'and' Boolean operations");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? Conjunction()");
            WriteLine("{");
            Indent();

            WriteLine("// Try to parse: a=inversion b=('and' c=inversion { c })+");
            WriteLine("var left = Inversion();");
            WriteLine("if (left == null) return null;");
            WriteLine();

            WriteLine("var andExpressions = new List<object>();");
            WriteLine("andExpressions.Add(left);");
            WriteLine();

            WriteLine("// Look for 'and' followed by inversion");
            WriteLine("while (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"and\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume 'and'");
            WriteLine("var right = Inversion();");
            WriteLine("if (right == null)");
            WriteLine("{");
            Indent();
            WriteLine("// Error: expected inversion after 'and'");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine("andExpressions.Add(right);");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// If we found 'and' expressions, create BoolOp");
            WriteLine("if (andExpressions.Count > 1)");
            WriteLine("{");
            Indent();
            WriteLine("var valueSeq = new GeneratedExprSeq();");
            WriteLine("valueSeq.AddRange(andExpressions.Cast<GeneratedExpr>());");
            WriteLine("return new GeneratedBoolOpExpr");
            WriteLine("{");
            Indent();
            WriteLine("Op = \"And\",");
            WriteLine("Values = valueSeq");
            Dedent();
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Single inversion - return it");
            WriteLine("return left;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate inversion method for 'not' expressions
        /// Based on grammar rule: inversion[expr_ty] (memo): | 'not' a=inversion | comparison
        /// </summary>
        private void GenerateInversionMethod()
        {
            WriteLine("/// <summary>");
            WriteLine("/// inversion[expr_ty] (memo): | 'not' a=inversion | comparison");
            WriteLine("/// Handles 'not' unary operations");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? Inversion()");
            WriteLine("{");
            Indent();

            WriteLine("// Try to parse: 'not' a=inversion");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"not\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume 'not'");
            WriteLine("var operand = Inversion(); // recursive call for 'not not x'");
            WriteLine("if (operand == null)");
            WriteLine("{");
            Indent();
            WriteLine("// Error: expected expression after 'not'");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("return new GeneratedUnaryOpExpr");
            WriteLine("{");
            Indent();
            WriteLine("Op = \"Not\",");
            WriteLine("Operand = (GeneratedExpr)operand");
            Dedent();
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Fall back to comparison");
            WriteLine("return Comparison();");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate f-string support methods for PEP 701 Enhanced f-strings
        /// Based on grammar rules: fstring, fstring_middle, fstring_replacement_field
        /// </summary>
        private void GenerateFStringMethods()
        {
            WriteLine();
            WriteLine("// === F-String Support Methods ===");
            WriteLine();

            GenerateFStringMethod();
            GenerateFStringMiddleMethod();
            GenerateFStringReplacementFieldMethod();
            GenerateStringsMethod();

            WriteLine("// === End F-String Support Methods ===");
            WriteLine();
        }

        /// <summary>
        /// fstring[expr_ty]: | a=FSTRING_START b=fstring_middle* c=FSTRING_END
        /// </summary>
        private void GenerateFStringMethod()
        {
            WriteLine("/// <summary>");
            WriteLine("/// fstring[expr_ty]: | a=FSTRING_START b=fstring_middle* c=FSTRING_END");
            WriteLine("/// Handles f-string literals with embedded expressions");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? Fstring()");
            WriteLine("{");
            Indent();

            WriteLine("// Check for FSTRING_START token");
            WriteLine("if (CurrentToken?.Type != TokenType.FSTRING_START)");
            WriteLine("{");
            Indent();
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("var startToken = CurrentToken;");
            WriteLine("Advance(); // consume FSTRING_START");
            WriteLine();

            WriteLine("// Parse fstring_middle* (zero or more)");
            WriteLine("var middleParts = new GeneratedExprSeq();");
            WriteLine("while (CurrentToken?.Type == TokenType.FSTRING_MIDDLE || ");
            WriteLine("       (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \"{\"))");
            WriteLine("{");
            Indent();
            WriteLine("var middle = FstringMiddle();");
            WriteLine("if (middle != null)");
            WriteLine("{");
            Indent();
            WriteLine("middleParts.Add(middle);");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("break;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Check for FSTRING_END token");
            WriteLine("if (CurrentToken?.Type != TokenType.FSTRING_END)");
            WriteLine("{");
            Indent();
            WriteLine("return null; // Error: expected FSTRING_END");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("var endToken = CurrentToken;");
            WriteLine("Advance(); // consume FSTRING_END");
            WriteLine();

            WriteLine("// Create JoinedStr AST node");
            WriteLine("var joinedStr = new GeneratedJoinedStrExpr();");
            WriteLine("joinedStr.Values.AddRange(middleParts);");
            WriteLine("return joinedStr;");

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// fstring_middle[expr_ty]: | fstring_replacement_field | FSTRING_MIDDLE
        /// </summary>
        private void GenerateFStringMiddleMethod()
        {
            WriteLine("/// <summary>");
            WriteLine("/// fstring_middle[expr_ty]: | fstring_replacement_field | FSTRING_MIDDLE");
            WriteLine("/// Handles literal text and replacement fields in f-strings");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? FstringMiddle()");
            WriteLine("{");
            Indent();

            WriteLine("// Try fstring_replacement_field first");
            WriteLine("var replacementField = FstringReplacementField();");
            WriteLine("if (replacementField != null)");
            WriteLine("{");
            Indent();
            WriteLine("return replacementField;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Try FSTRING_MIDDLE literal text");
            WriteLine("if (CurrentToken?.Type == TokenType.FSTRING_MIDDLE)");
            WriteLine("{");
            Indent();
            WriteLine("var value = CurrentToken.Value;");
            WriteLine("Advance(); // consume FSTRING_MIDDLE");
            WriteLine();
            WriteLine("return new GeneratedConstantExpr");
            WriteLine("{");
            Indent();
            WriteLine("Value = new PyString(value),");
            WriteLine("Kind = \"string\"");
            Dedent();
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("return null;");

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// fstring_replacement_field[expr_ty]: | '{' a=(yield_expr | star_expressions) debug_expr='='? conversion=[fstring_conversion] format=[fstring_full_format_spec] rbrace='}'
        /// </summary>
        private void GenerateFStringReplacementFieldMethod()
        {
            WriteLine("/// <summary>");
            WriteLine("/// fstring_replacement_field[expr_ty]: | '{' a=(yield_expr | star_expressions) debug_expr='='? conversion=[fstring_conversion] format=[fstring_full_format_spec] rbrace='}'");
            WriteLine("/// Handles {expression} replacement fields in f-strings");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? FstringReplacementField()");
            WriteLine("{");
            Indent();

            WriteLine("// Check for opening brace");
            WriteLine("if (CurrentToken?.Type != TokenType.OP || CurrentToken.Value != \"{\")");
            WriteLine("{");
            Indent();
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("Advance(); // consume '{'");
            WriteLine();

            WriteLine("// Parse the expression (star_expressions covers most cases)");
            WriteLine("var expr = Disjunction(); // Use disjunction for full expression support");
            WriteLine("if (expr == null)");
            WriteLine("{");
            Indent();
            WriteLine("return null; // Error: expected expression");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Check for optional debug expression ('=')");
            WriteLine("bool hasDebug = false;");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \"=\")");
            WriteLine("{");
            Indent();
            WriteLine("hasDebug = true;");
            WriteLine("Advance(); // consume '='");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Skip optional conversion (!r, !s, !a) for now");
            WriteLine("// Skip optional format specification (:spec) for now");
            WriteLine();

            WriteLine("// Check for closing brace");
            WriteLine("if (CurrentToken?.Type != TokenType.OP || CurrentToken.Value != \"}\")");
            WriteLine("{");
            Indent();
            WriteLine("return null; // Error: expected '}'");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("Advance(); // consume '}'");
            WriteLine();

            WriteLine("// Create FormattedValue AST node");
            WriteLine("return new GeneratedFormattedValueExpr");
            WriteLine("{");
            Indent();
            WriteLine("Value = expr,");
            WriteLine("Conversion = -1,");
            WriteLine("FormatSpec = null");
            Dedent();
            WriteLine("};");

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// strings[expr_ty] (memo): a[asdl_expr_seq*]=(fstring|string)+ { _PyPegen_concatenate_strings(p, a, EXTRA) }
        /// </summary>
        private void GenerateStringsMethod()
        {
            WriteLine("/// <summary>");
            WriteLine("/// strings[expr_ty] (memo): a[asdl_expr_seq*]=(fstring|string)+ { _PyPegen_concatenate_strings(p, a, EXTRA) }");
            WriteLine("/// Handles string concatenation including f-strings");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? Strings()");
            WriteLine("{");
            Indent();

            WriteLine("var stringParts = new List<GeneratedExpr>();");
            WriteLine();

            WriteLine("// Parse first string/fstring");
            WriteLine("var first = Fstring() ?? ParseStringLiteral();");
            WriteLine("if (first == null)");
            WriteLine("{");
            Indent();
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine("stringParts.Add(first);");
            WriteLine();

            WriteLine("// Parse additional strings/fstrings");
            WriteLine("while (true)");
            WriteLine("{");
            Indent();
            WriteLine("var next = Fstring() ?? ParseStringLiteral();");
            WriteLine("if (next == null)");
            WriteLine("{");
            Indent();
            WriteLine("break;");
            Dedent();
            WriteLine("}");
            WriteLine("stringParts.Add(next);");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// If only one string, return it directly");
            WriteLine("if (stringParts.Count == 1)");
            WriteLine("{");
            Indent();
            WriteLine("return stringParts[0];");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// Multiple strings - concatenate");
            WriteLine("var joinedStr = new GeneratedJoinedStrExpr();");
            WriteLine("joinedStr.Values.AddRange(stringParts);");
            WriteLine("return joinedStr;");

            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// Helper method to parse regular string literals");
            WriteLine("/// </summary>");
            WriteLine("private GeneratedExpr? ParseStringLiteral()");
            WriteLine("{");
            Indent();
            WriteLine("if (CurrentToken?.Type == TokenType.STRING)");
            WriteLine("{");
            Indent();
            WriteLine("var value = CurrentToken.Value;");
            WriteLine("Advance();");
            WriteLine("return new GeneratedConstantExpr");
            WriteLine("{");
            Indent();
            WriteLine("Value = new PyString(value),");
            WriteLine("Kind = \"string\"");
            Dedent();
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// await_primary[expr_ty] (memo): AWAIT a=primary | primary
        /// </summary>
        private void GenerateAwaitPrimaryGrammarMethod()
        {
            WriteLine("/// <summary>");
            WriteLine("/// await_primary[expr_ty] (memo): AWAIT a=primary | primary");
            WriteLine("/// Handles await expressions and delegates to primary for non-await expressions");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? AwaitPrimary()");
            WriteLine("{");
            Indent();

            WriteLine("// Check for AWAIT token");
            WriteLine("if (CurrentToken?.Type == TokenType.AWAIT && CurrentToken?.Value == \"await\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume 'await'");
            WriteLine("var primary = ParsePrimary();");
            WriteLine("if (primary != null)");
            WriteLine("{");
            Indent();
            WriteLine("return _PyAST_Await(primary);");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("throw new InvalidOperationException(\"Expected primary expression after 'await'\");");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("// Not an await expression, delegate to primary");
            WriteLine("return ParsePrimary();");
            Dedent();
            WriteLine("}");

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateTrailerMethods()
        {
            // CPython 3.12: Output @trailer code from grammar file
            if (!string.IsNullOrWhiteSpace(_grammar.Trailer))
            {
                WriteLine("// ============ Entry Points from @trailer ============");
                WriteLine(_grammar.Trailer);
                WriteLine();
            }
        }

        private void GenerateStatementMethods()
        {
            WriteLine("/// <summary>");
            WriteLine("/// statements[asdl_stmt_seq*]: a=statement+ { (asdl_stmt_seq*)_PyPegen_seq_flatten(p, a) }");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmtSeq ParseStatements()");
            WriteLine("{");
            Indent();
            WriteLine("var statements = new GeneratedStmtSeq();");
            WriteLine("int iterationCount = 0;");
            WriteLine("while (_position < _tokens.Count && CurrentToken?.Type != TokenType.ENDMARKER)");
            WriteLine("{");
            Indent();
            WriteLine("iterationCount++;");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseStatements: Loop iteration \" + iterationCount);");
            // CPython 3.12: Continue parsing through DEDENT tokens - let ParseBlock handle boundary detection
            // DEDENT tokens indicate indentation level changes but don't terminate statement sequences
            WriteLine();
            WriteLine("// Skip NEWLINE, NL, COMMENT tokens at module level");
            WriteLine("if (CurrentToken?.Type == TokenType.NEWLINE || CurrentToken?.Type == TokenType.NL || CurrentToken?.Type == TokenType.COMMENT)");
            WriteLine("{");
            WriteLine("    Advance();");
            WriteLine("    continue;");
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: At module level, DEDENT tokens complete indented blocks");
            WriteLine("// We should continue parsing the next statement after DEDENT");
            WriteLine();
            WriteLine("var startPos = _position; // Track starting position");
            WriteLine("var stmt = ParseStatement();");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseStatements: ParseStatement returned: \" + (stmt != null ? \"SUCCESS\" : \"NULL\"));");
            WriteLine("if (stmt != null)");
            WriteLine("{");
            WriteLine("    if (_position == startPos)");
            WriteLine("    {");
            WriteLine("        // CRITICAL: Position didn't advance - force advance to prevent infinite loop");
            WriteLine("        Console.WriteLine($\"[WARNING] ParseStatements: Position {_position} didn't advance, forcing advance to prevent infinite loop. Token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine("        Advance();");
            WriteLine("        continue;");
            WriteLine("    }");
            WriteLine("    // ParseStatement always returns GeneratedStmtSeq");
            WriteLine("    statements.AddRange(stmt);");
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            WriteLine("    break;");
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseStatements: Loop terminated. Position: \" + _position + \", TokenCount: \" + _tokens.Count + \", CurrentToken: \" + (CurrentToken?.Type.ToString() ?? \"null\"));");
            WriteLine("return _PyPegen_seq_flatten(statements);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // CPython 3.12: ParseStatementsUntilDedent - parse statements until reaching block boundary
            WriteLine("/// <summary>");
            WriteLine("/// Parse statements until we encounter a DEDENT that closes the current indentation level");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmtSeq ParseStatementsUntilDedent()");
            WriteLine("{");
            Indent();
            WriteLine("var statements = new GeneratedStmtSeq();");
            WriteLine();
            WriteLine("while (_position < _tokens.Count && CurrentToken?.Type != TokenType.ENDMARKER)");
            WriteLine("{");
            Indent();
            WriteLine("// Skip NEWLINE, NL, COMMENT tokens");
            WriteLine("if (CurrentToken?.Type == TokenType.NEWLINE || CurrentToken?.Type == TokenType.NL || CurrentToken?.Type == TokenType.COMMENT)");
            WriteLine("{");
            WriteLine("    Advance();");
            WriteLine("    continue;");
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: DEDENT token marks the end of current indentation level");
            WriteLine("// block: NEWLINE INDENT statements DEDENT - DEDENT closes the block immediately");
            WriteLine("if (CurrentToken?.Type == TokenType.DEDENT)");
            WriteLine("{");
            WriteLine("    Advance(); // consume DEDENT");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseStatementsUntilDedent: Found DEDENT, block parsing complete with {statements.Count} statements\");");
            WriteLine("    return statements;");
            WriteLine("}");
            WriteLine();
            WriteLine("var startPos = _position;");
            WriteLine("var stmt = ParseStatement();");
            WriteLine("if (stmt != null)");
            WriteLine("{");
            WriteLine("    if (_position == startPos)");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[WARNING] ParseStatementsUntilDedent: Position didn't advance, breaking to prevent infinite loop\");");
            WriteLine("        break;");
            WriteLine("    }");
            WriteLine("    // ParseStatement always returns GeneratedStmtSeq");
            WriteLine("    statements.AddRange(stmt);");
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            WriteLine("    break;");
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseStatementsUntilDedent: Finished parsing, returning {statements.Count} statements\");");
            WriteLine("return statements;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// statement[asdl_stmt_seq*]: a=compound_stmt { (asdl_stmt_seq*)_PyPegen_singleton_seq(p, a) } | a[asdl_stmt_seq*]=simple_stmts { a }");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmtSeq ParseStatement()");
            WriteLine("{");
            Indent();
            WriteLine("var startPos = _position; // Track starting position to prevent infinite loops");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseStatement: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("// Try compound statement first");
            WriteLine("var compound = ParseCompoundStmt();");
            WriteLine("if (compound != null)");
            WriteLine("{");
            WriteLine("    if (_position == startPos)");
            WriteLine("    {");
            WriteLine("        // CRITICAL: Position didn't advance - this indicates an infinite loop");
            WriteLine("        Console.WriteLine($\"[ERROR] ParseStatement: Position {_position} didn't advance after ParseCompoundStmt, forcing advance to prevent infinite loop. Token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine("        Advance();");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseStatement: Found compound statement\");");
            WriteLine("    return _PyPegen_singleton_seq(compound);");
            WriteLine("}");
            WriteLine();
            WriteLine("// Handle unexpected INDENT tokens at module level");
            WriteLine("// INDENT tokens should not appear at module level - they indicate tokenizer/parser sync issues");
            WriteLine("while (CurrentToken?.Type == TokenType.INDENT)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseStatement: Skipping unexpected INDENT token at module level: position {_position}\");");
            WriteLine("    Advance();");
            WriteLine("}");
            WriteLine();
            WriteLine("// Try simple statements");
            WriteLine("var simple = ParsePegStmts();");
            WriteLine("if (simple != null)");
            WriteLine("{");
            WriteLine("    if (_position == startPos)");
            WriteLine("    {");
            WriteLine("        // CRITICAL: Position didn't advance - this indicates an infinite loop");
            WriteLine("        Console.WriteLine($\"[ERROR] ParseStatement: Position {_position} didn't advance after ParsePegStmts, forcing advance to prevent infinite loop. Token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine("        Advance();");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseStatement: Found simple statements\");");
            WriteLine("    return simple;");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseStatement: No statement found at position {_position}\");");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateFunctionDefMethods()
        {
            WriteLine("/// <summary>");
            WriteLine("/// compound_stmt[stmt_ty]: CPython 3.12 grammar-based parsing");
            WriteLine("/// compound_stmt[stmt_ty]:");
            WriteLine("///     | &('def' | '@' | ASYNC) function_def");
            WriteLine("///     | &'if' if_stmt");
            WriteLine("///     | &('class' | '@') class_def");
            WriteLine("///     | &('with' | ASYNC) with_stmt");
            WriteLine("///     | &('for' | ASYNC) for_stmt");
            WriteLine("///     | &'try' try_stmt");
            WriteLine("///     | &'while' while_stmt");
            WriteLine("///     | match_stmt");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParseCompoundStmt()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: pos={_position}, token={CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine();
            WriteLine("// CPython 3.12: Use PegInterpreter for grammar-based parsing");
            WriteLine("// Try to parse compound_stmt using the grammar");
            WriteLine("var startPos = _position;");
            WriteLine();
            WriteLine("// Check for decorator (function or class definition with decorators)");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \"@\")");
            WriteLine("{");
            Indent();
            WriteLine("var decorators = ParseDecorators();");
            WriteLine("if (decorators != null)");
            WriteLine("{");
            Indent();
            WriteLine("// Check what follows the decorators");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"def\")");
            WriteLine("{");
            Indent();
            WriteLine("var function = ParseFunctionDefRaw();");
            WriteLine("if (function != null)");
            WriteLine("{");
            Indent();
            WriteLine("if (function is GeneratedFunctionDefStmt funcDef)");
            WriteLine("{");
            Indent();
            WriteLine("funcDef.DecoratorList = decorators;");
            WriteLine("return funcDef;");
            Dedent();
            WriteLine("}");
            WriteLine("else if (function is GeneratedAsyncFunctionDefStmt asyncFuncDef)");
            WriteLine("{");
            Indent();
            WriteLine("asyncFuncDef.DecoratorList = decorators;");
            WriteLine("return asyncFuncDef;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("else if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"class\")");
            WriteLine("{");
            Indent();
            WriteLine("var cls = ParseClassDefRaw();");
            WriteLine("if (cls != null && cls is GeneratedClassDefStmt classDef)");
            WriteLine("{");
            Indent();
            WriteLine("classDef.DecoratorList = decorators;");
            WriteLine("return classDef;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("return null; // Invalid decorator usage");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for async (function or for/with)");
            WriteLine("if (CurrentToken?.Type == TokenType.ASYNC)");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: Found ASYNC token\");");
            WriteLine("return ParseFunctionDef();");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for function definition");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"def\")");
            WriteLine("{");
            Indent();
            WriteLine("return ParseFunctionDef();");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for class statement");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"class\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: Found class statement\");");
            WriteLine("var result = ParseClassDef() as GeneratedStmt;");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for match statement (Python 3.10+)");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"match\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: Found match statement\");");
            WriteLine("var result = ParseMatchStatement() as GeneratedStmt;");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for try statement");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"try\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: Found try statement\");");
            WriteLine("var result = ParseTryStatement() as GeneratedStmt;");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for with statement");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"with\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: Found with statement\");");
            WriteLine("var result = ParseWithStatement() as GeneratedStmt;");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: if/while/for statements use grammar-generated methods");
            WriteLine("// These methods are auto-generated from python.gram");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: Checking if/while/for\");");
            WriteLine();
            WriteLine("// Check for if statement");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"if\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: Found if, calling IfStmt\");");
            WriteLine("var result = IfStmt();");
            WriteLine("if (result != null)");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: IfStmt succeeded\");");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for while statement");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"while\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: Found while, calling WhileStmt\");");
            WriteLine("var result = WhileStmt();");
            WriteLine("if (result != null)");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: WhileStmt succeeded\");");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for for statement");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"for\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: Found for, calling ForStmt\");");
            WriteLine("var result = ForStmt();");
            WriteLine("if (result != null)");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: ForStmt succeeded\");");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCompoundStmt: No compound statement matched\");");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// function_def[stmt_ty]: decorators function_def_raw | function_def_raw");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParseFunctionDef()");
            WriteLine("{");
            Indent();
            WriteLine("// For now, just handle function_def_raw (no decorators)");
            WriteLine("return ParseFunctionDefRaw();");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// function_def_raw[stmt_ty]: 'def' n=NAME '(' params=[params] ')' ':' b=block");
            WriteLine("///                        | ASYNC 'def' n=NAME '(' params=[params] ')' ':' b=block");
            WriteLine("/// { _PyAST_FunctionDef / _PyAST_AsyncFunctionDef }");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParseFunctionDefRaw()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("var startPos = _position;");
            WriteLine("bool isAsync = false;");
            WriteLine();
            WriteLine("// Check for ASYNC 'def' or just 'def'");
            WriteLine("if (CurrentToken?.Type == TokenType.ASYNC)");
            WriteLine("{");
            Indent();
            WriteLine("isAsync = true;");
            WriteLine("Advance(); // consume ASYNC");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: Found ASYNC keyword, expecting 'def' next\");");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// 'def'");
            WriteLine("if (!ExpectKeyword(\"def\"))");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: Failed to match 'def' keyword at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// NAME");
            WriteLine("var name = ExpectName();");
            WriteLine("if (name == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: Failed to match function name at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// [type_params] - Optional type parameters for PEP 695");
            WriteLine("var typeParams = ParseTypeParams();");
            WriteLine();
            WriteLine("// '('");
            WriteLine("if (ExpectToken(TokenType.OP, \"(\") == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: Failed to match '(' at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// [params] - Parse function parameters");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: About to parse parameters at position {_position}\");");
            WriteLine("var parameters = ParseParams();");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: Parameters parsing completed\");");
            WriteLine();
            WriteLine("// ')'");
            WriteLine("if (ExpectToken(TokenType.OP, \")\") == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: Failed to match ')' at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// ['->' expression] - Optional return type annotation");
            WriteLine("object returnAnnotation = null;");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"->\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: Found '->' for return type annotation at position {_position}\");");
            WriteLine("    Advance(); // consume '->'");
            WriteLine("    returnAnnotation = Expression();");
            WriteLine("    if (returnAnnotation != null)");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: parsed return type annotation\");");
            WriteLine("    }");
            WriteLine("    else");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: failed to parse return type annotation\");");
            WriteLine("        _position = startPos;");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("// ':'");
            WriteLine("if (ExpectToken(TokenType.OP, \":\") == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: Failed to match ':' at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// block");
            WriteLine("var body = ParseBlock();");
            WriteLine("if (body == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: Failed to parse function body at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// Return the appropriate result based on async/sync");
            WriteLine("if (isAsync)");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: Successfully parsed async function '{name}' at position {_position}\");");
            WriteLine("return _PyAST_AsyncFunctionDef(name, parameters, body, type_params: typeParams);");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseFunctionDefRaw: Successfully parsed function '{name}' at position {_position}\");");
            WriteLine("return _PyAST_FunctionDef(name, parameters, body, type_params: typeParams);");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateBasicMethods()
        {
            WriteLine("/// <summary>");
            WriteLine("/// simple_stmts[asdl_stmt_seq*]: simple_stmt (';' simple_stmt)* [';'] NEWLINE");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmtSeq ParsePegStmts()");
            WriteLine("{");
            Indent();
            WriteLine("var statements = new GeneratedStmtSeq();");
            WriteLine("var stmt = ParsePegStmt();");
            WriteLine("if (stmt != null)");
            WriteLine("    statements.Add(stmt);");
            WriteLine();
            WriteLine("// Handle semicolons and additional statements");
            WriteLine("while (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \";\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume ';'");
            WriteLine();
            WriteLine("// Optional trailing semicolon before NEWLINE");
            WriteLine("if (CurrentToken?.Type == TokenType.NEWLINE)");
            WriteLine("    break;");
            WriteLine();
            WriteLine("var nextStmt = ParsePegStmt();");
            WriteLine("if (nextStmt != null)");
            WriteLine("    statements.Add(nextStmt);");
            WriteLine("else");
            WriteLine("    break;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Only expect NEWLINE if we're not at end of input and current token is NEWLINE");
            WriteLine("if (CurrentToken?.Type == TokenType.NEWLINE)");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume NEWLINE");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("return statements;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// simple_stmt[stmt_ty]: CPython 3.12 order - assignment | star_expressions | 'pass' | 'return' | ...");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParsePegStmt()");
            WriteLine("{");
            Indent();
            WriteLine("// CPython 3.12: assignment FIRST");
            WriteLine("var assignment = ParseAssignment();");
            WriteLine("if (assignment != null)");
            WriteLine("{");
            WriteLine("    return assignment;");
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: star_expressions (expression statement) SECOND");
            WriteLine("// CRITICAL: Do not parse compound statement keywords as expressions");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            WriteLine("    var tokenValue = CurrentToken.Value;");
            WriteLine("    // Reject compound statement keywords and simple statement keywords");
            WriteLine("    if (tokenValue == \"if\" || tokenValue == \"while\" || tokenValue == \"for\" ||");
            WriteLine("        tokenValue == \"def\" || tokenValue == \"class\" || tokenValue == \"try\" ||");
            WriteLine("        tokenValue == \"with\" || tokenValue == \"async\" || tokenValue == \"match\" ||");
            WriteLine("        tokenValue == \"return\" || tokenValue == \"import\" || tokenValue == \"from\" ||");
            WriteLine("        tokenValue == \"raise\" || tokenValue == \"pass\" || tokenValue == \"break\" ||");
            WriteLine("        tokenValue == \"continue\" || tokenValue == \"global\" || tokenValue == \"nonlocal\" ||");
            WriteLine("        tokenValue == \"del\" || tokenValue == \"yield\" || tokenValue == \"assert\")");
            WriteLine("    {");
            WriteLine("        // These are handled by their specific parsers below");
            WriteLine("    }");
            WriteLine("    else");
            WriteLine("    {");
            WriteLine("        // Try expression statement");
            WriteLine("        var expr = Expression();");
            WriteLine("        if (expr != null)");
            WriteLine("            return _PyAST_Expr(expr);");
            WriteLine("    }");
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            WriteLine("    // Not a NAME token, try expression");
            WriteLine("    var expr = Expression();");
            WriteLine("    if (expr != null)");
            WriteLine("        return _PyAST_Expr(expr);");
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: &'return' return_stmt");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"return\")");
            WriteLine("{");
            WriteLine("    return ParseReturnStmt();");
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: &('import' | 'from') import_stmt");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"import\")");
            WriteLine("{");
            WriteLine("    return ParseImportStatement();");
            WriteLine("}");
            WriteLine();
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"from\")");
            WriteLine("{");
            WriteLine("    return ParseFromImportStatement();");
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: &'raise' raise_stmt");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"raise\")");
            WriteLine("{");
            WriteLine("    return ParseRaiseStatement();");
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: 'pass'");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"pass\")");
            WriteLine("{");
            WriteLine("    Advance();");
            WriteLine("    return _PyAST_Pass();");
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: 'break'");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"break\")");
            WriteLine("{");
            WriteLine("    Advance();");
            WriteLine("    return new GeneratedBreakStmt();");
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: 'continue'");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"continue\")");
            WriteLine("{");
            WriteLine("    Advance();");
            WriteLine("    return new GeneratedContinueStmt();");
            WriteLine("}");
            WriteLine();
            WriteLine();
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// return_stmt[stmt_ty]: 'return' a=[star_expressions] { _PyAST_Return(a, EXTRA) }");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParseReturnStmt()");
            WriteLine("{");
            Indent();
            WriteLine("if (!ExpectKeyword(\"return\"))");
            WriteLine("    return null;");
            WriteLine();
            WriteLine("// Optional return value");
            WriteLine("var value = Expression(); // Simplified - should be star_expressions");
            WriteLine("return _PyAST_Return(value);");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// params[arguments_ty]: parameters");
            WriteLine("/// </summary>");
            WriteLine("public object ParseParams()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseParams called at position {_position}\");");
            WriteLine("var result = ParseParameters();");
            WriteLine("if (result != null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseParams: parameters rule succeeded\");");
            WriteLine("    return result;");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseParams: parameters rule failed, returning empty arguments\");");
            WriteLine("return _PyPegen_empty_arguments();");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Generate ParseParameters method - CPython 3.12 compatible implementation
            WriteLine("/// <summary>");
            WriteLine("/// parameters[arguments_ty]: CPython 3.12 compatible parameter parsing");
            WriteLine("/// Implements: slash_no_default | slash_with_default | param_no_default+ | param_with_default+ | star_etc");
            WriteLine("/// </summary>");
            WriteLine("public object ParseParameters()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseParameters called at position {_position}\");");
            WriteLine();
            WriteLine("var startPos = _position;");
            WriteLine();
            WriteLine("// Try each alternative in order (PEG first-match semantics)");
            WriteLine();
            WriteLine("// Alternative 1: a=slash_no_default b=param_no_default* c=param_with_default* d=[star_etc]");
            WriteLine("var slashNoDefault = ParseSlashNoDefault();");
            WriteLine("if (slashNoDefault != null)");
            WriteLine("{");
            Indent();
            WriteLine("var paramNoDefaults = new List<object>();");
            WriteLine("while (true)");
            WriteLine("{");
            WriteLine("    var param = ParseParamNoDefault();");
            WriteLine("    if (param == null) break;");
            WriteLine("    paramNoDefaults.Add(param);");
            WriteLine("}");
            WriteLine();
            WriteLine("var paramWithDefaults = new List<object>();");
            WriteLine("while (true)");
            WriteLine("{");
            WriteLine("    var param = ParseParamWithDefault();");
            WriteLine("    if (param == null) break;");
            WriteLine("    paramWithDefaults.Add(param);");
            WriteLine("}");
            WriteLine();
            WriteLine("var starEtc = ParseStarEtc();");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseParameters: Alternative 1 matched - positional-only args: {((List<object>)slashNoDefault).Count}\");");
            WriteLine("return _PyPegen_make_arguments(this, slashNoDefault, null, paramNoDefaults, paramWithDefaults.Count > 0 ? paramWithDefaults : null, starEtc);");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Reset position for next alternative");
            WriteLine("_position = startPos;");
            WriteLine();
            WriteLine("// Alternative 2: a=slash_with_default b=param_with_default* c=[star_etc]");
            WriteLine("var slashWithDefault = ParseSlashWithDefault();");
            WriteLine("if (slashWithDefault != null)");
            WriteLine("{");
            Indent();
            WriteLine("var paramWithDefaults = new List<object>();");
            WriteLine("while (true)");
            WriteLine("{");
            WriteLine("    var param = ParseParamWithDefault();");
            WriteLine("    if (param == null) break;");
            WriteLine("    paramWithDefaults.Add(param);");
            WriteLine("}");
            WriteLine();
            WriteLine("var starEtc = ParseStarEtc();");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseParameters: Alternative 2 matched - positional-only args with defaults\");");
            WriteLine("return _PyPegen_make_arguments(this, null, slashWithDefault, null, paramWithDefaults.Count > 0 ? paramWithDefaults : null, starEtc);");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Reset position for next alternative");
            WriteLine("_position = startPos;");
            WriteLine();
            WriteLine("// Alternative 3: a=param_no_default+ b=param_with_default* c=[star_etc]");
            WriteLine("var paramNoDefaults3 = new List<object>();");
            WriteLine("var param3 = ParseParamNoDefault();");
            WriteLine("if (param3 != null)");
            WriteLine("{");
            Indent();
            WriteLine("paramNoDefaults3.Add(param3);");
            WriteLine("while (true)");
            WriteLine("{");
            WriteLine("    param3 = ParseParamNoDefault();");
            WriteLine("    if (param3 == null) break;");
            WriteLine("    paramNoDefaults3.Add(param3);");
            WriteLine("}");
            WriteLine();
            WriteLine("var paramWithDefaults3 = new List<object>();");
            WriteLine("while (true)");
            WriteLine("{");
            WriteLine("    param3 = ParseParamWithDefault();");
            WriteLine("    if (param3 == null) break;");
            WriteLine("    paramWithDefaults3.Add(param3);");
            WriteLine("}");
            WriteLine();
            WriteLine("var starEtc3 = ParseStarEtc();");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseParameters: Alternative 3 matched - regular args: {paramNoDefaults3.Count}\");");
            WriteLine("return _PyPegen_make_arguments(this, null, null, paramNoDefaults3, paramWithDefaults3.Count > 0 ? paramWithDefaults3 : null, starEtc3);");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Reset position for next alternative");
            WriteLine("_position = startPos;");
            WriteLine();
            WriteLine("// Alternative 4: a=param_with_default+ b=[star_etc]");
            WriteLine("var paramWithDefaults4 = new List<object>();");
            WriteLine("var param4 = ParseParamWithDefault();");
            WriteLine("if (param4 != null)");
            WriteLine("{");
            Indent();
            WriteLine("paramWithDefaults4.Add(param4);");
            WriteLine("while (true)");
            WriteLine("{");
            WriteLine("    param4 = ParseParamWithDefault();");
            WriteLine("    if (param4 == null) break;");
            WriteLine("    paramWithDefaults4.Add(param4);");
            WriteLine("}");
            WriteLine();
            WriteLine("var starEtc4 = ParseStarEtc();");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseParameters: Alternative 4 matched - args with defaults: {paramWithDefaults4.Count}\");");
            WriteLine("return _PyPegen_make_arguments(this, null, null, null, paramWithDefaults4, starEtc4);");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Reset position for next alternative");
            WriteLine("_position = startPos;");
            WriteLine();
            WriteLine("// Alternative 5: a=star_etc");
            WriteLine("var starEtc2 = ParseStarEtc();");
            WriteLine("if (starEtc2 != null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseParameters: Alternative 5 matched - star_etc only\");");
            WriteLine("    return _PyPegen_make_arguments(this, null, null, null, null, starEtc2);");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseParameters: no parameters found\");");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            // Generate helper methods for parameter parsing - CPython 3.12 compatible

            WriteLine("/// <summary>");
            WriteLine("/// slash_no_default[asdl_arg_seq*]: param_no_default+ '/' ','");
            WriteLine("/// </summary>");
            WriteLine("public object ParseSlashNoDefault()");
            WriteLine("{");
            Indent();
            WriteLine("var startPos = _position;");
            WriteLine("var paramsList = new List<object>();");
            WriteLine();
            WriteLine("// Parse param_no_default+");
            WriteLine("var param = ParseParamNoDefault();");
            WriteLine("if (param == null) return null;");
            WriteLine("paramsList.Add(param);");
            WriteLine();
            WriteLine("while (true)");
            WriteLine("{");
            WriteLine("    param = ParseParamNoDefault();");
            WriteLine("    if (param == null) break;");
            WriteLine("    paramsList.Add(param);");
            WriteLine("}");
            WriteLine();
            WriteLine("// Expect '/'");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \"/\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume '/'");
            WriteLine("    // Expect ',' or lookahead for ')'");
            WriteLine("    if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \",\")");
            WriteLine("    {");
            WriteLine("        Advance(); // consume ','");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseSlashNoDefault: Found {paramsList.Count} positional-only parameters\");");
            WriteLine("        return paramsList;");
            WriteLine("    }");
            WriteLine("    else if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \")\")");
            WriteLine("    {");
            WriteLine("        // Lookahead for ')' - don't consume");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseSlashNoDefault: Found {paramsList.Count} positional-only parameters (end)\");");
            WriteLine("        return paramsList;");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("_position = startPos;");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// slash_with_default[SlashWithDefault*]: param_no_default* param_with_default+ '/' ','");
            WriteLine("/// </summary>");
            WriteLine("public object ParseSlashWithDefault()");
            WriteLine("{");
            Indent();
            WriteLine("var startPos = _position;");
            WriteLine("var noDefaults = new List<object>();");
            WriteLine("var withDefaults = new List<object>();");
            WriteLine();
            WriteLine("// Parse param_no_default*");
            WriteLine("while (true)");
            WriteLine("{");
            WriteLine("    var param = ParseParamNoDefault();");
            WriteLine("    if (param == null) break;");
            WriteLine("    noDefaults.Add(param);");
            WriteLine("}");
            WriteLine();
            WriteLine("// Parse param_with_default+");
            WriteLine("var paramDefault = ParseParamWithDefault();");
            WriteLine("if (paramDefault == null)");
            WriteLine("{");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("withDefaults.Add(paramDefault);");
            WriteLine();
            WriteLine("while (true)");
            WriteLine("{");
            WriteLine("    paramDefault = ParseParamWithDefault();");
            WriteLine("    if (paramDefault == null) break;");
            WriteLine("    withDefaults.Add(paramDefault);");
            WriteLine("}");
            WriteLine();
            WriteLine("// Expect '/'");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \"/\")");
            WriteLine("{");
            WriteLine("    Advance();");
            WriteLine("    if (CurrentToken?.Type == TokenType.OP && (CurrentToken.Value == \",\" || CurrentToken.Value == \")\"))");
            WriteLine("    {");
            WriteLine("        if (CurrentToken.Value == \",\") Advance();");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseSlashWithDefault: Found positional-only with defaults\");");
            WriteLine("        return _PyPegen_slash_with_default(this, noDefaults, withDefaults);");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("_position = startPos;");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// param_no_default[arg_ty]: param ',' TYPE_COMMENT? | param TYPE_COMMENT? &(')'|'/')");
            WriteLine("/// </summary>");
            WriteLine("public object ParseParamNoDefault()");
            WriteLine("{");
            Indent();
            WriteLine("var startPos = _position;");
            WriteLine("var param = ParseParam();");
            WriteLine("if (param == null) return null;");
            WriteLine();
            WriteLine("// Check for comma or lookahead for ')' or '/'");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \",\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume comma");
            WriteLine("    return param;");
            WriteLine("}");
            WriteLine("else if (CurrentToken?.Type == TokenType.OP && (CurrentToken.Value == \")\" || CurrentToken.Value == \"/\"))");
            WriteLine("{");
            WriteLine("    // Lookahead - don't consume");
            WriteLine("    return param;");
            WriteLine("}");
            WriteLine();
            WriteLine("_position = startPos;");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// param_with_default[NamedExpr]: param default ',' TYPE_COMMENT? | param default TYPE_COMMENT? &(')'|'/')");
            WriteLine("/// </summary>");
            WriteLine("public object ParseParamWithDefault()");
            WriteLine("{");
            Indent();
            WriteLine("var startPos = _position;");
            WriteLine("var param = ParseParam();");
            WriteLine("if (param == null) return null;");
            WriteLine();
            WriteLine("// Expect '='");
            WriteLine("if (CurrentToken?.Type != TokenType.OP || CurrentToken.Value != \"=\")");
            WriteLine("{");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume '='");
            WriteLine();
            WriteLine("var defaultValue = Expression();");
            WriteLine("if (defaultValue == null)");
            WriteLine("{");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for comma or lookahead");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \",\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume comma");
            WriteLine("}");
            WriteLine("else if (CurrentToken?.Type == TokenType.OP && (CurrentToken.Value == \")\" || CurrentToken.Value == \"/\"))");
            WriteLine("{");
            WriteLine("    // Lookahead - don't consume");
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("return new { param = param, defaultValue = defaultValue };");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// param[arg_ty]: NAME annotation?");
            WriteLine("/// </summary>");
            WriteLine("public object ParseParam()");
            WriteLine("{");
            Indent();
            WriteLine("if (CurrentToken?.Type != TokenType.NAME) return null;");
            WriteLine();
            WriteLine("var name = CurrentToken.Value;");
            WriteLine("Advance();");
            WriteLine();
            WriteLine("object annotation = null;");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \":\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume ':'");
            WriteLine("    annotation = Expression();");
            WriteLine("    if (annotation == null) return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("return _PyAST_arg(name, annotation, null);");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// star_etc[StarEtc*]: '*' param_no_default param_maybe_default* [kwds] | '*' ',' param_maybe_default+ [kwds] | kwds");
            WriteLine("/// </summary>");
            WriteLine("/// <summary>");
            WriteLine("/// star_etc[StarEtc*]: '*' a=param_no_default b=param_maybe_default* c=[kwds]");
            WriteLine("///                  | '*' ',' b=param_maybe_default+ c=[kwds]");
            WriteLine("///                  | a=kwds");
            WriteLine("/// CPython 3.12: Must parse actual parameter name after * or **");
            WriteLine("/// </summary>");
            WriteLine("public object ParseStarEtc()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseStarEtc: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine("var savedPos = _position;");
            WriteLine();
            WriteLine("// Alternative 1: '*' param_no_default ...");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \"*\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume '*'");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseStarEtc: Consumed '*', now at: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine("    ");
            WriteLine("    var result = new Dictionary<string, object>();");
            WriteLine("    result[\"kwonlyargs\"] = new List<object>();");
            WriteLine("    result[\"kwarg\"] = null;");
            WriteLine("    ");
            WriteLine("    // CPython 3.12: Parse NAME token for vararg parameter");
            WriteLine("    if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("    {");
            WriteLine("        var paramName = CurrentToken.Value;");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseStarEtc: Found vararg NAME: {paramName}\");");
            WriteLine("        Advance(); // consume NAME");
            WriteLine("        result[\"vararg\"] = paramName;");
            WriteLine("    }");
            WriteLine("    else if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \",\")");
            WriteLine("    {");
            WriteLine("        // '*' ',' case - bare star for keyword-only args");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseStarEtc: Bare '*' (keyword-only marker)\");");
            WriteLine("        result[\"vararg\"] = null;");
            WriteLine("    }");
            WriteLine("    else");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[ERROR] ParseStarEtc: Expected NAME or ',' after '*', got: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine("        _position = savedPos;");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine("    ");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseStarEtc: Successfully parsed '*' alternative\");");
            WriteLine("    return result;");
            WriteLine("}");
            WriteLine();
            WriteLine("// Alternative 2: '**' param_no_default");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken.Value == \"**\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume '**'");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseStarEtc: Consumed '**', now at: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine("    ");
            WriteLine("    // CPython 3.12: Parse NAME token for kwarg parameter");
            WriteLine("    if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("    {");
            WriteLine("        var paramName = CurrentToken.Value;");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseStarEtc: Found kwarg NAME: {paramName}\");");
            WriteLine("        Advance(); // consume NAME");
            WriteLine("        ");
            WriteLine("        var result = new Dictionary<string, object>();");
            WriteLine("        result[\"vararg\"] = null;");
            WriteLine("        result[\"kwonlyargs\"] = new List<object>();");
            WriteLine("        result[\"kwarg\"] = paramName;");
            WriteLine("        ");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseStarEtc: Successfully parsed '**' alternative\");");
            WriteLine("        return result;");
            WriteLine("    }");
            WriteLine("    else");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[ERROR] ParseStarEtc: Expected NAME after '**', got: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine("        _position = savedPos;");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseStarEtc: No match found\");");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// block[asdl_stmt_seq*]: NEWLINE INDENT statements DEDENT | simple_stmts");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmtSeq ParseBlock()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseBlock: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("// Use left-recursion handling for block parsing");
            WriteLine("return TryLeftRecursive<GeneratedStmtSeq>(\"ParseBlock\", () =>");
            WriteLine("{");
            WriteLine("    var statements = new GeneratedStmtSeq();");
            WriteLine();
            WriteLine("// CPython 3.12 block grammar: NEWLINE INDENT statements DEDENT | simple_stmts");
            WriteLine("// First alternative: NEWLINE INDENT statements DEDENT");
            WriteLine("if (CurrentToken?.Type == TokenType.NEWLINE)");
            WriteLine("{");
            WriteLine("    var savedPos = _position;");
            WriteLine("    Advance(); // consume NEWLINE");
            WriteLine();
            WriteLine("    if (CurrentToken?.Type == TokenType.INDENT)");
            WriteLine("    {");
            WriteLine("        Advance(); // consume INDENT");
            WriteLine();
            // CPython 3.12: Parse statements until we return to the original indentation level
            WriteLine("        var blockStatements = ParseStatementsUntilDedent();");
            WriteLine("        if (blockStatements != null)");
            WriteLine("        {");
            WriteLine("            Console.WriteLine($\"[DEBUG] ParseBlock: Successfully parsed indented block with {blockStatements.Count} statements\");");
            WriteLine("            return blockStatements;");
            WriteLine("        }");
            WriteLine("    }");
            WriteLine();
            WriteLine("    // Failed to parse indented block, restore position and try simple_stmts");
            WriteLine("    _position = savedPos;");
            WriteLine("}");
            WriteLine();
            WriteLine("// Second alternative: simple_stmts");
            WriteLine("var stmt = ParsePegStmts();");
            WriteLine("if (stmt != null)");
            WriteLine("{");
            WriteLine("    return stmt; // ParsePegStmts returns GeneratedStmtSeq directly");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseBlock: Returning result with {statements.Count} statements at position {_position}\");");
            WriteLine("return statements;");
            WriteLine("});");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add Class Body Parser (no left-recursion)
            WriteLine("/// <summary>");
            WriteLine("/// Parse class body without left-recursion");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmtSeq ParseClassBody()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseClassBody: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine("var statements = new GeneratedStmtSeq();");
            WriteLine();
            WriteLine("// CPython 3.12: Track class body indentation level for accurate parsing");
            WriteLine("int? classBodyIndentLevel = null;");
            WriteLine();
            WriteLine("if (CurrentToken?.Type == TokenType.NEWLINE)");
            WriteLine("{");
            WriteLine("    Advance(); // consume NEWLINE");
            WriteLine("    ");
            WriteLine("    // Record the class body indentation level before consuming INDENT token");
            WriteLine("    if (CurrentToken?.Type == TokenType.INDENT && CurrentToken?.Value != null)");
            WriteLine("    {");
            WriteLine("        classBodyIndentLevel = CurrentToken.Value.Length;");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseClassBody: Recorded class body indent level: {classBodyIndentLevel}\");");
            WriteLine("    }");
            WriteLine("    ");
            WriteLine("    if (ExpectToken(TokenType.INDENT) == null)");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseClassBody: Expected INDENT after class declaration\");");
            WriteLine("        return statements;");
            WriteLine("    }");
            WriteLine();
            WriteLine("    // Parse statements until class body ends");
            WriteLine("    // A class body ends when we see a final DEDENT at the class level");
            WriteLine("    while (CurrentToken != null)");
            WriteLine("    {");
            WriteLine("        // Check for end of class body - DEDENT followed by non-INDENT token");
            WriteLine("        if (CurrentToken.Type == TokenType.DEDENT)");
            WriteLine("        {");
            WriteLine("            // CPython 3.12: Check if this DEDENT ends the class body");
            WriteLine("            // Calculate current indentation level after DEDENT");
            WriteLine("            int currentIndentLevel = 0;");
            WriteLine("            int nextPos = _position + 1;");
            WriteLine("            ");
            WriteLine("            // Look ahead to find the next significant token and its indentation");
            WriteLine("            while (nextPos < _tokens.Count)");
            WriteLine("            {");
            WriteLine("                var nextToken = _tokens[nextPos];");
            WriteLine("                if (nextToken.Type == TokenType.NL)");
            WriteLine("                {");
            WriteLine("                    nextPos++;");
            WriteLine("                    continue;");
            WriteLine("                }");
            WriteLine("                else if (nextToken.Type == TokenType.INDENT)");
            WriteLine("                {");
            WriteLine("                    // Get indentation level from INDENT token");
            WriteLine("                    if (nextToken.Value != null)");
            WriteLine("                    {");
            WriteLine("                        currentIndentLevel = nextToken.Value.Length;");
            WriteLine("                    }");
            WriteLine("                    nextPos++;");
            WriteLine("                }");
            WriteLine("                else");
            WriteLine("                {");
            WriteLine("                    // Found the next significant token");
            WriteLine("                    break;");
            WriteLine("                }");
            WriteLine("            }");
            WriteLine();
            WriteLine("            bool hasMoreClassContent = false;");
            WriteLine("            if (nextPos < _tokens.Count)");
            WriteLine("            {");
            WriteLine("                var nextToken = _tokens[nextPos];");
            WriteLine("                // Check if we're still at class body level and have class-level statements");
            WriteLine("                if (classBodyIndentLevel.HasValue && currentIndentLevel == classBodyIndentLevel.Value &&");
            WriteLine("                    nextToken.Type == TokenType.NAME && ");
            WriteLine("                    (nextToken.Value == \"def\" || nextToken.Value == \"class\" || nextToken.Value == \"async\"))");
            WriteLine("                {");
            WriteLine("                    hasMoreClassContent = true;");
            WriteLine("                }");
            WriteLine("                else if (classBodyIndentLevel.HasValue && currentIndentLevel == classBodyIndentLevel.Value &&");
            WriteLine("                         nextToken.Type == TokenType.OP && nextToken.Value == \"@\")");
            WriteLine("                {");
            WriteLine("                    hasMoreClassContent = true;");
            WriteLine("                }");
            WriteLine("            }");
            WriteLine("            if (hasMoreClassContent)");
            WriteLine("            {");
            WriteLine("                Console.WriteLine($\"[DEBUG] ParseClassBody: Found DEDENT at indent level {currentIndentLevel}, same as class level {classBodyIndentLevel}, continuing class body\");");
            WriteLine("                Advance(); // Skip the DEDENT");
            WriteLine("                continue;  // Continue parsing the next statement");
            WriteLine("            }");
            WriteLine("            else");
            WriteLine("            {");
            WriteLine("                Console.WriteLine($\"[DEBUG] ParseClassBody: Found DEDENT at indent level {currentIndentLevel}, different from class level {classBodyIndentLevel}, ending class body\");");
            WriteLine("                break; // End of class body");
            WriteLine("            }");
            WriteLine("        }");
            WriteLine();
            WriteLine("        // Skip blank lines (NL tokens) in class body");
            WriteLine("        if (CurrentToken?.Type == TokenType.NL)");
            WriteLine("        {");
            WriteLine("            Console.WriteLine($\"[DEBUG] ParseClassBody: Skipping NL token at position {_position}\");");
            WriteLine("            Advance();");
            WriteLine("            continue;");
            WriteLine("        }");
            WriteLine();
            WriteLine("        // Handle INDENT tokens within class body (for method definitions after blank lines)");
            WriteLine("        if (CurrentToken?.Type == TokenType.INDENT)");
            WriteLine("        {");
            WriteLine("            Console.WriteLine($\"[DEBUG] ParseClassBody: Found INDENT token, checking if it matches class level\");");
            WriteLine("            // In a class body, INDENT tokens can appear before method definitions");
            WriteLine("            // Skip the INDENT and continue parsing the method");
            WriteLine("            Advance();");
            WriteLine("            continue;");
            WriteLine("        }");
            WriteLine();
            WriteLine("        var stmt = ParseStatement();");
            WriteLine("        if (stmt != null)");
            WriteLine("        {");
            WriteLine("            statements.AddRange(stmt); // ParseStatement returns GeneratedStmtSeq");
            WriteLine("            Console.WriteLine($\"[DEBUG] ParseClassBody: Added statement, total: {statements.Count}\");");
            WriteLine("        }");
            WriteLine("        else");
            WriteLine("        {");
            WriteLine("            // Handle parse failures more carefully");
            WriteLine("            Console.WriteLine($\"[DEBUG] ParseClassBody: Failed to parse statement at {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine("            if (CurrentToken?.Type == TokenType.DEDENT)");
            WriteLine("            {");
            WriteLine("                break; // End of class body");
            WriteLine("            }");
            WriteLine("            else if (CurrentToken != null)");
            WriteLine("            {");
            WriteLine("                Console.WriteLine($\"[DEBUG] ParseClassBody: Skipping unparseable token to prevent infinite loop\");");
            WriteLine("                Advance();");
            WriteLine("            }");
            WriteLine("            else");
            WriteLine("            {");
            WriteLine("                break; // End of input");
            WriteLine("            }");
            WriteLine("        }");
            WriteLine("    }");
            WriteLine();
            WriteLine("    if (CurrentToken?.Type == TokenType.DEDENT)");
            WriteLine("    {");
            WriteLine("        Advance(); // consume DEDENT");
            WriteLine("    }");
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            WriteLine("    // Single line class body - parse one statement");
            WriteLine("    var stmt = ParsePegStmt();");
            WriteLine("    if (stmt != null)");
            WriteLine("    {");
            WriteLine("        statements.Add(stmt);");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseClassBody: Returning {statements.Count} statements\");");
            WriteLine("return statements;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add Class Definition Parser
            WriteLine("/// <summary>");
            WriteLine("/// class_def[stmt_ty]: 'class' a=NAME [type_params] b=['(' z=[arguments] ')'] ':' c=block");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParseClassDef()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseClassDef: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("// Expect 'class' keyword");
            WriteLine("if (CurrentToken?.Type != TokenType.NAME || CurrentToken?.Value != \"class\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseClassDef: Expected 'class' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume 'class'");
            WriteLine();
            WriteLine("// Parse class name");
            WriteLine("if (CurrentToken?.Type != TokenType.NAME)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseClassDef: Expected class name but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("var className = CurrentToken.Value;");
            WriteLine("Advance(); // consume class name");
            WriteLine();
            WriteLine("// Parse optional base classes and keyword arguments '(' [arguments] ')'");
            WriteLine("var baseClasses = new GeneratedExprSeq();");
            WriteLine("var keywords = new List<object>(); // keyword arguments like metaclass=ABCMeta");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"(\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume '('");
            WriteLine("    ");
            WriteLine("    // Parse class arguments (base classes and keyword arguments like metaclass=...)");
            WriteLine("    while (CurrentToken != null && !(CurrentToken.Type == TokenType.OP && CurrentToken.Value == \")\"))");
            WriteLine("    {");
            WriteLine("        if (CurrentToken.Type == TokenType.NAME)");
            WriteLine("        {");
            WriteLine("            var startPos = _position;");
            WriteLine("            var firstToken = CurrentToken.Value;");
            WriteLine("            Advance();");
            WriteLine("            ");
            WriteLine("            // Check if this is a keyword argument (name=value)");
            WriteLine("            if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"=\")");
            WriteLine("            {");
            WriteLine("                Advance(); // consume '='");
            WriteLine("                ");
            WriteLine("                // Parse the value (expect a NAME for simple cases like metaclass=ABCMeta)");
            WriteLine("                if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("                {");
            WriteLine("                    var value = CurrentToken.Value;");
            WriteLine("                    Advance();");
            WriteLine("                    ");
            WriteLine("                    // CPython 3.12: Create keyword argument as anonymous object");
            WriteLine("                    var nameExpr = new GeneratedNameExpr { Id = value };");
            WriteLine("                    var keywordArg = new { arg = firstToken, value = (object)nameExpr };");
            WriteLine("                    keywords.Add(keywordArg);" );
            WriteLine("                    Console.WriteLine($\"[DEBUG] ParseClassDef: Added keyword argument {firstToken}={value}\");");
            WriteLine("                }");
            WriteLine("                else");
            WriteLine("                {");
            WriteLine("                    Console.WriteLine($\"[DEBUG] ParseClassDef: Expected value after '=' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("                    break;");
            WriteLine("                }");
            WriteLine("            }");
            WriteLine("            else");
            WriteLine("            {");
            WriteLine("                // Regular base class");
            WriteLine("                var baseExpr = new GeneratedNameExpr { Id = firstToken };");
            WriteLine("                baseClasses.Add(baseExpr);");
            WriteLine("                Console.WriteLine($\"[DEBUG] ParseClassDef: Added base class {firstToken}\");");
            WriteLine("            }");
            WriteLine("        }");
            WriteLine("        else");
            WriteLine("        {");
            WriteLine("            Console.WriteLine($\"[DEBUG] ParseClassDef: Unexpected token in class arguments: {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("            break;");
            WriteLine("        }");
            WriteLine("        ");
            WriteLine("        // Handle comma separator");
            WriteLine("        if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \",\")");
            WriteLine("        {");
            WriteLine("            Advance(); // consume ','");
            WriteLine("        }");
            WriteLine("        else if (!(CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \")\"))");
            WriteLine("        {");
            WriteLine("            Console.WriteLine($\"[DEBUG] ParseClassDef: Expected ',' or ')' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("            break;");
            WriteLine("        }");
            WriteLine("    }");
            WriteLine("    ");
            WriteLine("    // Expect closing ')'");
            WriteLine("    if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \")\")");
            WriteLine("    {");
            WriteLine("        Advance(); // consume ')'");
            WriteLine("    }");
            WriteLine("    else");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseClassDef: Expected ')' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("// Expect ':'");
            WriteLine("if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \":\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseClassDef: Expected ':' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume ':'");
            WriteLine();
            WriteLine("// Parse class body");
            WriteLine("var body = ParseClassBody();");
            WriteLine("if (body == null || body.Count == 0)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseClassDef: Failed to parse class body\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseClassDef: Successfully parsed class '{className}' with {baseClasses.Count} base classes and {body.Count} statements\");");
            WriteLine("return new GeneratedClassDefStmt");
            WriteLine("{");
            WriteLine("    Name = className,");
            WriteLine("    Bases = baseClasses,");
            WriteLine("    Keywords = new GeneratedExprSeq(), // Keywords stored separately in advanced parsing");
            WriteLine("    Body = body");
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add Try-Except Statement Parser
            WriteLine("/// <summary>");
            WriteLine("/// try_stmt: 'try' ':' b=block ((except_block+ except_star_block* [finally_block]) | except_star_block+ [finally_block] | finally_block)");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParseTryStatement()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseTryStatement: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("// Expect 'try' keyword");
            WriteLine("if (CurrentToken?.Type != TokenType.NAME || CurrentToken?.Value != \"try\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseTryStatement: Expected 'try' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume 'try'");
            WriteLine();
            WriteLine("// Expect ':'");
            WriteLine("if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \":\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseTryStatement: Expected ':' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume ':'");
            WriteLine();
            WriteLine("// Parse try body");
            WriteLine("var tryBody = ParseBlock();");
            WriteLine("if (tryBody == null || tryBody.Count == 0)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseTryStatement: Failed to parse try body\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// Parse except clauses (both except and except*)");
            WriteLine("var exceptClauses = new List<object>();");
            WriteLine("while (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value == \"except\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume 'except'");
            WriteLine("    var exceptionType = \"\"; // Optional exception type");
            WriteLine("    var exceptionName = \"\"; // Optional exception variable name");
            WriteLine("    var isExceptStar = false; // Track if this is except* clause");
            WriteLine();
            WriteLine("    // Check for except* syntax (PEP 654)");
            WriteLine("    if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"*\")");
            WriteLine("    {");
            WriteLine("        isExceptStar = true;");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseTryStatement: Found except* clause\");");
            WriteLine("        Advance(); // consume '*'");
            WriteLine("    }");
            WriteLine();
            WriteLine("    // Parse optional exception type");
            WriteLine("    if (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value != \":\")");
            WriteLine("    {");
            WriteLine("        exceptionType = CurrentToken.Value;");
            WriteLine("        Advance();");
            WriteLine();
            WriteLine("        // Parse optional 'as' variable");
            WriteLine("        if (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value == \"as\")");
            WriteLine("        {");
            WriteLine("            Advance(); // consume 'as'");
            WriteLine("            if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("            {");
            WriteLine("                exceptionName = CurrentToken.Value;");
            WriteLine("                Advance();");
            WriteLine("            }");
            WriteLine("        }");
            WriteLine("    }");
            WriteLine();
            WriteLine("    // Expect ':'");
            WriteLine("    if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \":\")");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseTryStatement: Expected ':' after except{(isExceptStar ? \"*\" : \"\")} but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine("    Advance(); // consume ':'");
            WriteLine();
            WriteLine("    // Parse except body");
            WriteLine("    var exceptBody = ParseBlock();");
            WriteLine("    if (exceptBody == null || exceptBody.Count == 0)");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseTryStatement: Failed to parse except{(isExceptStar ? \"*\" : \"\")} body\");");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine();
            WriteLine("    exceptClauses.Add(new { type = exceptionType, name = exceptionName, body = exceptBody, isExceptStar = isExceptStar });");
            WriteLine("}");
            WriteLine();
            WriteLine("// Parse optional finally clause");
            WriteLine("var finallyBody = new GeneratedStmtSeq();");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value == \"finally\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume 'finally'");
            WriteLine("    // Expect ':'");
            WriteLine("    if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \":\")");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseTryStatement: Expected ':' after finally but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine("    Advance(); // consume ':'");
            WriteLine();
            WriteLine("    // Parse finally body");
            WriteLine("    finallyBody = ParseBlock();");
            WriteLine("    if (finallyBody == null)");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseTryStatement: Failed to parse finally body\");");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseTryStatement: Successfully parsed try statement with {exceptClauses.Count} except clauses and {finallyBody.Count} finally statements\");");
            WriteLine("return new GeneratedTryStmt");
            WriteLine("{");
            WriteLine("    Body = tryBody,");
            WriteLine("    Handlers = exceptClauses,");
            WriteLine("    FinallyBody = finallyBody");
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add Import Statement Parser
            WriteLine("/// <summary>");
            WriteLine("/// import_name[stmt_ty]: 'import' a=dotted_as_names");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParseImportStatement()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseImportStatement: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("// Expect 'import' keyword");
            WriteLine("if (CurrentToken?.Type != TokenType.NAME || CurrentToken?.Value != \"import\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseImportStatement: Expected 'import' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume 'import'");
            WriteLine();
            WriteLine("// Parse module names (dotted_as_names)");
            WriteLine("var modules = new List<object>();");
            WriteLine("while (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            WriteLine("    var moduleName = \"\";");
            WriteLine("    var asName = \"\";");
            WriteLine();
            WriteLine("    // Parse dotted name (e.g., os.path)");
            WriteLine("    while (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("    {");
            WriteLine("        moduleName += CurrentToken.Value;");
            WriteLine("        Advance();");
            WriteLine("        if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \".\")");
            WriteLine("        {");
            WriteLine("            moduleName += \".\";");
            WriteLine("            Advance(); // consume '.'");
            WriteLine("        }");
            WriteLine("        else");
            WriteLine("        {");
            WriteLine("            break;");
            WriteLine("        }");
            WriteLine("    }");
            WriteLine();
            WriteLine("    // Parse optional 'as' alias");
            WriteLine("    if (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value == \"as\")");
            WriteLine("    {");
            WriteLine("        Advance(); // consume 'as'");
            WriteLine("        if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("        {");
            WriteLine("            asName = CurrentToken.Value;");
            WriteLine("            Advance();");
            WriteLine("        }");
            WriteLine("    }");
            WriteLine();
            WriteLine("    modules.Add(new { name = moduleName, asname = asName });");
            WriteLine();
            WriteLine("    // Check for comma separator");
            WriteLine("    if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \",\")");
            WriteLine("    {");
            WriteLine("        Advance(); // consume ','");
            WriteLine("    }");
            WriteLine("    else");
            WriteLine("    {");
            WriteLine("        break;");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseImportStatement: Successfully parsed import with {modules.Count} modules\");");
            WriteLine("return new GeneratedImportStmt");
            WriteLine("{");
            WriteLine("    Names = modules");
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// import_from[stmt_ty]: 'from' ... 'import' ...");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParseFromImportStatement()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseFromImportStatement: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("// Expect 'from' keyword");
            WriteLine("if (CurrentToken?.Type != TokenType.NAME || CurrentToken?.Value != \"from\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseFromImportStatement: Expected 'from' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume 'from'");
            WriteLine();
            WriteLine("// Parse module name (with optional relative imports)");
            WriteLine("var fromModule = \"\";");
            WriteLine("var level = 0; // relative import level");
            WriteLine();
            WriteLine("// Handle relative imports (. or ..)");
            WriteLine("while (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \".\")");
            WriteLine("{");
            WriteLine("    level++;");
            WriteLine("    fromModule += \".\";");
            WriteLine("    Advance();");
            WriteLine("}");
            WriteLine();
            WriteLine("// Parse module name");
            WriteLine("while (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            WriteLine("    fromModule += CurrentToken.Value;");
            WriteLine("    Advance();");
            WriteLine("    if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \".\")");
            WriteLine("    {");
            WriteLine("        fromModule += \".\";");
            WriteLine("        Advance(); // consume '.'");
            WriteLine("    }");
            WriteLine("    else");
            WriteLine("    {");
            WriteLine("        break;");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("// Expect 'import' keyword");
            WriteLine("if (CurrentToken?.Type != TokenType.NAME || CurrentToken?.Value != \"import\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseFromImportStatement: Expected 'import' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume 'import'");
            WriteLine();
            WriteLine("// Parse import names");
            WriteLine("var importNames = new List<object>();");
            WriteLine();
            WriteLine("// Handle 'import *'");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"*\")");
            WriteLine("{");
            WriteLine("    importNames.Add(new { Name = \"*\", AsName = (string)null });");
            WriteLine("    Advance();");
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            WriteLine("    // Parse import name list");
            WriteLine("    while (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("    {");
            WriteLine("        var importName = CurrentToken.Value;");
            WriteLine("        var asName = \"\";");
            WriteLine("        Advance();");
            WriteLine();
            WriteLine("        // Parse optional 'as' alias");
            WriteLine("        if (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value == \"as\")");
            WriteLine("        {");
            WriteLine("            Advance(); // consume 'as'");
            WriteLine("            if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("            {");
            WriteLine("                asName = CurrentToken.Value;");
            WriteLine("                Advance();");
            WriteLine("            }");
            WriteLine("        }");
            WriteLine();
            WriteLine("        importNames.Add(new { Name = importName, AsName = string.IsNullOrEmpty(asName) ? (string)null : asName });");
            WriteLine();
            WriteLine("        // Check for comma separator");
            WriteLine("        if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \",\")");
            WriteLine("        {");
            WriteLine("            Advance(); // consume ','");
            WriteLine("        }");
            WriteLine("        else");
            WriteLine("        {");
            WriteLine("            break;");
            WriteLine("        }");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseFromImportStatement: Successfully parsed 'from {fromModule} import' with {importNames.Count} names\");");
            WriteLine("return new GeneratedImportFromStmt");
            WriteLine("{");
            WriteLine("    Module = fromModule,");
            WriteLine("    Level = level,");
            WriteLine("    Names = importNames");
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add Raise Statement Parser
            WriteLine("/// <summary>");
            WriteLine("/// CPython 3.12 raise_stmt: 'raise' [expression ['from' expression]] | 'raise'");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParseRaiseStatement()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseRaiseStatement: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("// Expect 'raise' keyword");
            WriteLine("if (CurrentToken?.Type != TokenType.NAME || CurrentToken?.Value != \"raise\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseRaiseStatement: Expected 'raise' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume 'raise'");
            WriteLine();
            WriteLine("// Check for expression after 'raise'");
            WriteLine("GeneratedExpr exceptionExpr = null;");
            WriteLine("GeneratedExpr fromExpr = null;");
            WriteLine();
            WriteLine("// Parse optional exception expression");
            WriteLine("if (CurrentToken != null && CurrentToken.Type != TokenType.NEWLINE)");
            WriteLine("{");
            WriteLine("    var exprResult = Expression();");
            WriteLine("    exceptionExpr = (GeneratedExpr?)exprResult;");
            WriteLine("    if (exceptionExpr == null)");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseRaiseStatement: Failed to parse exception expression\");");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine();
            WriteLine("    // Check for optional 'from' clause");
            WriteLine("    if (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value == \"from\")");
            WriteLine("    {");
            WriteLine("        Advance(); // consume 'from'");
            WriteLine("        var fromResult = Expression();");
            WriteLine("        fromExpr = (GeneratedExpr?)fromResult;");
            WriteLine("        if (fromExpr == null)");
            WriteLine("        {");
            WriteLine("            Console.WriteLine($\"[DEBUG] ParseRaiseStatement: Failed to parse 'from' expression\");");
            WriteLine("            return null;");
            WriteLine("        }");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseRaiseStatement: Successfully parsed raise statement\");");
            WriteLine("return new GeneratedRaiseStmt");
            WriteLine("{");
            WriteLine("    Exc = exceptionExpr,");
            WriteLine("    Cause = fromExpr");
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add With Statement Parser
            WriteLine("/// <summary>");
            WriteLine("/// with_stmt: 'with' '(' a=with_item [',' with_item]* ')' ':' b=block | 'with' a=with_item [',' with_item]* ':' b=block");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParseWithStatement()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseWithStatement: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("// Expect 'with' keyword");
            WriteLine("if (CurrentToken?.Type != TokenType.NAME || CurrentToken?.Value != \"with\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseWithStatement: Expected 'with' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume 'with'");
            WriteLine();
            WriteLine("// Check for optional parentheses");
            WriteLine("bool hasParentheses = false;");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"(\")");
            WriteLine("{");
            WriteLine("    hasParentheses = true;");
            WriteLine("    Advance(); // consume '('");
            WriteLine("}");
            WriteLine();
            WriteLine("// Parse with items");
            WriteLine("var withItems = new List<object>();");
            WriteLine("while (CurrentToken != null)");
            WriteLine("{");
            WriteLine("    // Parse context expression");
            WriteLine("    var contextExpr = Expression();");
            WriteLine("    if (contextExpr == null)");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseWithStatement: Failed to parse context expression\");");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine();
            WriteLine("    var asVar = \"\";");
            WriteLine("    // Parse optional 'as' target");
            WriteLine("    if (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value == \"as\")");
            WriteLine("    {");
            WriteLine("        Advance(); // consume 'as'");
            WriteLine("        if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("        {");
            WriteLine("            asVar = CurrentToken.Value;");
            WriteLine("            Advance();");
            WriteLine("        }");
            WriteLine("    }");
            WriteLine();
            WriteLine("    withItems.Add(new { context = contextExpr, target = asVar });");
            WriteLine();
            WriteLine("    // Check for comma separator");
            WriteLine("    if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \",\")");
            WriteLine("    {");
            WriteLine("        Advance(); // consume ','");
            WriteLine("    }");
            WriteLine("    else");
            WriteLine("    {");
            WriteLine("        break;");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for closing parenthesis if we had opening one");
            WriteLine("if (hasParentheses)");
            WriteLine("{");
            WriteLine("    if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \")\")");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseWithStatement: Expected ')' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine("    Advance(); // consume ')'");
            WriteLine("}");
            WriteLine();
            WriteLine("// Expect ':'");
            WriteLine("if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \":\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseWithStatement: Expected ':' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume ':'");
            WriteLine();
            WriteLine("// Parse with body");
            WriteLine("var body = ParseBlock();");
            WriteLine("if (body == null || body.Count == 0)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseWithStatement: Failed to parse with body\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseWithStatement: Successfully parsed with statement with {withItems.Count} items and {body.Count} statements\");");
            WriteLine("return new GeneratedWithStmt");
            WriteLine("{");
            WriteLine("    Items = withItems,");
            WriteLine("    Body = body");
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate lambda expression parser: lambdef[expr_ty]: 'lambda' a=[lambda_params] ':' b=expression
        /// </summary>
        private void GenerateLambdaParser()
        {
            WriteLine("/// <summary>");
            WriteLine("/// lambdef[expr_ty]: 'lambda' a=[lambda_params] ':' b=expression");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? ParseLambda()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseLambda: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("// Expect 'lambda' keyword");
            WriteLine("if (CurrentToken?.Type != TokenType.NAME || CurrentToken?.Value != \"lambda\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseLambda: Expected 'lambda' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume 'lambda'");
            WriteLine();
            WriteLine("// Parse optional parameters (simplified - no parameters for now)");
            WriteLine("var parameters = new List<string>();");
            WriteLine("while (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value != \":\")");
            WriteLine("{");
            WriteLine("    parameters.Add(CurrentToken.Value);");
            WriteLine("    Advance();");
            WriteLine("    // Skip comma if present");
            WriteLine("    if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \",\")");
            WriteLine("    {");
            WriteLine("        Advance();");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("// Expect ':'");
            WriteLine("if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \":\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseLambda: Expected ':' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume ':'");
            WriteLine();
            WriteLine("// Parse lambda body expression");
            WriteLine("var body = Expression();");
            WriteLine("if (body == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseLambda: Failed to parse lambda body\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseLambda: Successfully parsed lambda with {parameters.Count} parameters\");");
            WriteLine("var lambdaExpr = new GeneratedLambdaExpr();");
            WriteLine("lambdaExpr.Arguments = parameters;");
            WriteLine("lambdaExpr.Body = body;");
            WriteLine("return lambdaExpr;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add comprehension parsing methods
            GenerateListOrListCompParser();
            GenerateDictSetOrCompParser();
            GenerateForIfClausesParser();
        }

        /// <summary>
        /// Generate ParseListOrListComp method for '[' [named_expression [for_if_clauses]] ']'
        /// </summary>
        private void GenerateListOrListCompParser()
        {
            WriteLine("/// <summary>");
            WriteLine("/// Parse list literal or list comprehension");
            WriteLine("/// list: '[' a=[star_named_expressions] ']'");
            WriteLine("/// listcomp: '[' a=named_expression b=for_if_clauses ']'");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? ParseListOrListComp()");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '['");
            WriteLine("SkipNL(); // skip newlines after opening bracket");
            WriteLine();
            WriteLine("// Handle empty list");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"]\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume ']'");
            WriteLine("    return new GeneratedListExpr { Elements = new GeneratedExprSeq() };");
            WriteLine("}");
            WriteLine();
            WriteLine("// Parse first expression");
            WriteLine("var firstExpr = Expression();");
            WriteLine("if (firstExpr == null) return null;");
            WriteLine();
            WriteLine("// Check if this is a comprehension (look for 'for' keyword)");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value == \"for\")");
            WriteLine("{");
            WriteLine("    // This is a list comprehension");
            WriteLine("    var forIfClauses = ParseForIfClauses();");
            WriteLine("    if (forIfClauses == null) return null;");
            WriteLine();
            WriteLine("    // Expect closing ']'");
            WriteLine("    if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \"]\")");
            WriteLine("        return null;");
            WriteLine("    Advance(); // consume ']'");
            WriteLine();
            WriteLine("    return new GeneratedListCompExpr { Element = firstExpr, Generators = (List<object>)forIfClauses };");
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            WriteLine("    // This is a regular list literal");
            WriteLine("    var elements = new List<object> { firstExpr };");
            WriteLine();
            WriteLine("    // Parse remaining elements");
            WriteLine("    while (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \",\")");
            WriteLine("    {");
            WriteLine("        Advance(); // consume ','");
            WriteLine("        SkipNL(); // skip newlines after comma");
            WriteLine("        // Allow trailing comma");
            WriteLine("        if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"]\")");
            WriteLine("            break;");
            WriteLine("        var element = Expression();");
            WriteLine("        if (element != null)");
            WriteLine("            elements.Add(element);");
            WriteLine("        SkipNL(); // skip newlines after element");
            WriteLine("    }");
            WriteLine();
            WriteLine("    // Expect closing ']'");
            WriteLine("    if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \"]\")");
            WriteLine("        return null;");
            WriteLine("    Advance(); // consume ']'");
            WriteLine();
            WriteLine("    // Convert List<object> to GeneratedExprSeq");
            WriteLine("    var exprElements = new GeneratedExprSeq();");
            WriteLine("    exprElements.AddRange(elements.Cast<GeneratedExpr>());");
            WriteLine("    return new GeneratedListExpr { Elements = exprElements };");
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add missing methods that are referenced by the parser switch statement
            WriteLine("/// <summary>");
            WriteLine("/// statements: statement+");
            WriteLine("/// </summary>");
            WriteLine("public List<object> Statements()");
            WriteLine("{");
            Indent();
            WriteLine("var statements = new List<object>();");
            WriteLine("var stmt = Statement();");
            WriteLine("if (stmt != null) statements.Add(stmt);");
            WriteLine("return statements;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// statement: compound_stmt | simple_stmts");
            WriteLine("/// </summary>");
            WriteLine("public object Statement()");
            WriteLine("{");
            Indent();
            WriteLine("return ParsePegStmt();");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// expression: disjunction | lambdef");
            WriteLine("/// </summary>");
            WriteLine("public object Expression()");
            WriteLine("{");
            Indent();
            WriteLine("return Expression();");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// star_targets: star_target ((',' star_target))* [',']");
            WriteLine("/// </summary>");
            WriteLine("public List<object> StarTargets()");
            WriteLine("{");
            Indent();
            WriteLine("var targets = new List<object>();");
            WriteLine("var target = Expression();");
            WriteLine("if (target != null) targets.Add(target);");
            WriteLine("return targets;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// star_expressions: star_expression ((',' star_expression))* [',']");
            WriteLine("/// </summary>");
            WriteLine("public List<object> StarExpressions()");
            WriteLine("{");
            Indent();
            WriteLine("var expressions = new List<object>();");
            WriteLine("var expr = Expression();");
            WriteLine("if (expr != null) expressions.Add(expr);");
            WriteLine("return expressions;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// expressions: expression ((',' expression))* [',']");
            WriteLine("/// </summary>");
            WriteLine("public List<object> Expressions()");
            WriteLine("{");
            Indent();
            WriteLine("var expressions = new List<object>();");
            WriteLine("var expr = Expression();");
            WriteLine("if (expr != null) expressions.Add(expr);");
            WriteLine("return expressions;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// primary: atom | primary '.' NAME | primary '[' slices ']' | primary '(' [arguments] ')'");
            WriteLine("/// </summary>");
            WriteLine("public object Primary()");
            WriteLine("{");
            Indent();
            WriteLine("return Atom();");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// atom: NAME | 'True' | 'False' | 'None' | '__peg_parser__' | STRING | NUMBER | ...");
            WriteLine("/// </summary>");
            WriteLine("public object Atom()");
            WriteLine("{");
            Indent();
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            WriteLine("    var name = CurrentToken.Value;");
            WriteLine("    Advance();");
            WriteLine("    return new GeneratedNameExpr { Id = name, Context = \"Load\" };");
            WriteLine("}");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// assignment: target '=' expression");
            WriteLine("/// </summary>");
            WriteLine("public object Assignment()");
            WriteLine("{");
            Indent();
            WriteLine("return ParseAssignment();");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate ParseDictSetOrComp method for '{' [key:value/element [for_if_clauses]] '}'
        /// </summary>
        private void GenerateDictSetOrCompParser()
        {
            WriteLine("/// <summary>");
            WriteLine("/// Parse dict literal, set literal, dict comprehension, or set comprehension");
            WriteLine("/// dict: '{' a=[double_starred_kvpairs] '}'");
            WriteLine("/// set: '{' a=star_named_expressions '}'");
            WriteLine("/// dictcomp: '{' a=kvpair b=for_if_clauses '}'");
            WriteLine("/// setcomp: '{' a=named_expression b=for_if_clauses '}'");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExpr? ParseDictSetOrComp()");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '{'");
            WriteLine("SkipNL(); // skip newlines after opening brace");
            WriteLine();
            WriteLine("// Handle empty dict");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"}\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume '}'");
            WriteLine("    return new GeneratedDictExpr { Keys = new GeneratedExprSeq(), Values = new GeneratedExprSeq() };");
            WriteLine("}");
            WriteLine();
            WriteLine("// Parse first expression");
            WriteLine("var firstExpr = Expression();");
            WriteLine("if (firstExpr == null) return null;");
            WriteLine();
            WriteLine("// Check if this is a key-value pair (dict)");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \":\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume ':'");
            WriteLine("    SkipNL(); // skip newlines after colon");
            WriteLine("    var value = Expression();");
            WriteLine("    if (value == null) return null;");
            WriteLine();
            WriteLine("    // Check if this is a dict comprehension");
            WriteLine("    if (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value == \"for\")");
            WriteLine("    {");
            WriteLine("        // This is a dict comprehension");
            WriteLine("        var forIfClauses = ParseForIfClauses();");
            WriteLine("        if (forIfClauses == null) return null;");
            WriteLine();
            WriteLine("        // Expect closing '}'");
            WriteLine("        if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \"}\")");
            WriteLine("            return null;");
            WriteLine("        Advance(); // consume '}'");
            WriteLine();
            WriteLine("        return new GeneratedDictCompExpr { Key = firstExpr, Value = value, Generators = (List<object>)forIfClauses };");
            WriteLine("    }");
            WriteLine("    else");
            WriteLine("    {");
            WriteLine("        // This is a regular dict literal");
            WriteLine("        var pairs = new List<object> { new { key = firstExpr, value = value } };");
            WriteLine();
            WriteLine("        // Parse remaining pairs");
            WriteLine("        while (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \",\")");
            WriteLine("        {");
            WriteLine("            Advance(); // consume ','");
            WriteLine("            SkipNL(); // skip newlines after comma");
            WriteLine("            // Allow trailing comma");
            WriteLine("            if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"}\")");
            WriteLine("                break;");
            WriteLine("            var key = Expression();");
            WriteLine("            if (key == null) break;");
            WriteLine("            SkipNL(); // skip newlines after key");
            WriteLine("            if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \":\")");
            WriteLine("                break;");
            WriteLine("            Advance(); // consume ':'");
            WriteLine("            SkipNL(); // skip newlines after colon");
            WriteLine("            var val = Expression();");
            WriteLine("            if (val != null)");
            WriteLine("                pairs.Add(new { key = key, value = val });");
            WriteLine("            SkipNL(); // skip newlines after value");
            WriteLine("        }");
            WriteLine();
            WriteLine("        // Expect closing '}'");
            WriteLine("        if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \"}\")");
            WriteLine("            return null;");
            WriteLine("        Advance(); // consume '}'");
            WriteLine();
            WriteLine("        // Convert pairs to separate keys and values");
            WriteLine("        var keys = new GeneratedExprSeq();");
            WriteLine("        var values = new GeneratedExprSeq();");
            WriteLine("        foreach (dynamic pair in pairs)");
            WriteLine("        {");
            WriteLine("            keys.Add(pair.key);");
            WriteLine("            values.Add(pair.value);");
            WriteLine("        }");
            WriteLine("        return new GeneratedDictExpr { Keys = keys, Values = values };");
            WriteLine("    }");
            WriteLine("}");
            WriteLine("else if (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value == \"for\")");
            WriteLine("{");
            WriteLine("    // This is a set comprehension");
            WriteLine("    var forIfClauses = ParseForIfClauses();");
            WriteLine("    if (forIfClauses == null) return null;");
            WriteLine();
            WriteLine("    // Expect closing '}'");
            WriteLine("    if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \"}\")");
            WriteLine("        return null;");
            WriteLine("    Advance(); // consume '}'");
            WriteLine();
            WriteLine("    return new GeneratedSetCompExpr { Element = firstExpr, Generators = (List<object>)forIfClauses };");
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            WriteLine("    // This is a regular set literal");
            WriteLine("    var elements = new List<object> { firstExpr };");
            WriteLine();
            WriteLine("    // Parse remaining elements");
            WriteLine("    while (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \",\")");
            WriteLine("    {");
            WriteLine("        Advance(); // consume ','");
            WriteLine("        // Allow trailing comma");
            WriteLine("        if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"}\")");
            WriteLine("            break;");
            WriteLine("        var element = Expression();");
            WriteLine("        if (element != null)");
            WriteLine("            elements.Add(element);");
            WriteLine("    }");
            WriteLine();
            WriteLine("    // Expect closing '}'");
            WriteLine("    if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \"}\")");
            WriteLine("        return null;");
            WriteLine("    Advance(); // consume '}'");
            WriteLine();
            WriteLine("    // Convert List<object> to GeneratedExprSeq");
            WriteLine("    var exprElements = new GeneratedExprSeq();");
            WriteLine("    exprElements.AddRange(elements.Cast<GeneratedExpr>());");
            WriteLine("    return new GeneratedSetExpr { Elements = exprElements };");
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate ParseForIfClauses method for parsing for_if_clauses
        /// </summary>
        private void GenerateForIfClausesParser()
        {
            WriteLine("/// <summary>");
            WriteLine("/// Parse for_if_clauses in comprehensions");
            WriteLine("/// for_if_clauses: for_if_clause+");
            WriteLine("/// for_if_clause: 'for' star_targets 'in' disjunction ('if' disjunction)*");
            WriteLine("/// </summary>");
            WriteLine("public object ParseForIfClauses()");
            WriteLine("{");
            Indent();
            WriteLine("var clauses = new List<object>();");
            WriteLine();
            WriteLine("// Parse at least one for clause");
            WriteLine("do");
            WriteLine("{");
            WriteLine("    // Expect 'for' keyword");
            WriteLine("    if (CurrentToken?.Type != TokenType.NAME || CurrentToken?.Value != \"for\")");
            WriteLine("        break;");
            WriteLine("    Advance(); // consume 'for'");
            WriteLine();
            WriteLine("    // Parse target variable");
            WriteLine("    if (CurrentToken?.Type != TokenType.NAME)");
            WriteLine("        return null;");
            WriteLine("    var target = CurrentToken.Value;");
            WriteLine("    Advance();");
            WriteLine();
            WriteLine("    // Expect 'in' keyword");
            WriteLine("    if (CurrentToken?.Type != TokenType.NAME || CurrentToken?.Value != \"in\")");
            WriteLine("        return null;");
            WriteLine("    Advance(); // consume 'in'");
            WriteLine();
            WriteLine("    // Parse iterable expression");
            WriteLine("    var iterable = Expression();");
            WriteLine("    if (iterable == null) return null;");
            WriteLine();
            WriteLine("    // Parse optional 'if' conditions");
            WriteLine("    var conditions = new List<object>();");
            WriteLine("    while (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value == \"if\")");
            WriteLine("    {");
            WriteLine("        Advance(); // consume 'if'");
            WriteLine("        var condition = Expression();");
            WriteLine("        if (condition != null)");
            WriteLine("            conditions.Add(condition);");
            WriteLine("    }");
            WriteLine();
            WriteLine("    clauses.Add(new { target = target, iterable = iterable, conditions = conditions });");
            WriteLine("} while (CurrentToken?.Type == TokenType.NAME && CurrentToken?.Value == \"for\");");
            WriteLine();
            WriteLine("return clauses;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add decorator parsing methods
            GenerateDecoratorParsers();
        }

        /// <summary>
        /// Generate decorator parsing methods
        /// </summary>
        private void GenerateDecoratorParsers()
        {
            WriteLine("/// <summary>");
            WriteLine("/// Parse decorators: ('@' named_expression NEWLINE)+");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedExprSeq ParseDecorators()");
            WriteLine("{");
            Indent();
            WriteLine("var decorators = new GeneratedExprSeq();");
            WriteLine();
            WriteLine("// Parse one or more decorators");
            WriteLine("while (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"@\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume '@'");
            WriteLine();
            WriteLine("    // Parse decorator expression");
            WriteLine("    var decoratorExpr = Expression();");
            WriteLine("    if (decoratorExpr == null)");
            WriteLine("        return null;");
            WriteLine();
            WriteLine("    decorators.Add(decoratorExpr);");
            WriteLine();
            WriteLine("    // Expect NEWLINE after decorator");
            WriteLine("    if (CurrentToken?.Type == TokenType.NEWLINE)");
            WriteLine("    {");
            WriteLine("        Advance(); // consume NEWLINE");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("return decorators.Count > 0 ? decorators : null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// Parse class definition without decorators");
            WriteLine("/// class_def_raw: 'class' NAME [type_params] ['(' [arguments] ')'] ':' block");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParseClassDefRaw()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseClassDefRaw: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("// Expect 'class' keyword");
            WriteLine("if (CurrentToken?.Type != TokenType.NAME || CurrentToken?.Value != \"class\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseClassDefRaw: Expected 'class' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume 'class'");
            WriteLine();
            WriteLine("// Parse class name");
            WriteLine("if (CurrentToken?.Type != TokenType.NAME)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseClassDefRaw: Expected class name but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("var className = CurrentToken.Value;");
            WriteLine("Advance(); // consume class name");
            WriteLine();
            WriteLine("// Parse optional base classes '(' [arguments] ')'");
            WriteLine("var baseClasses = new GeneratedExprSeq();");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"(\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume '('");
            WriteLine("    // Parse base class names");
            WriteLine("    while (CurrentToken != null && !(CurrentToken.Type == TokenType.OP && CurrentToken.Value == \")\"))");
            WriteLine("    {");
            WriteLine("        if (CurrentToken.Type == TokenType.NAME)");
            WriteLine("        {");
            WriteLine("            var baseExpr = new GeneratedNameExpr { Id = CurrentToken.Value };");
            WriteLine("            baseClasses.Add(baseExpr);");
            WriteLine("            Advance();");
            WriteLine("        }");
            WriteLine("        // Handle comma separator");
            WriteLine("        if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \",\")");
            WriteLine("        {");
            WriteLine("            Advance(); // consume ','");
            WriteLine("        }");
            WriteLine("        else if (!(CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \")\"))");
            WriteLine("        {");
            WriteLine("            Console.WriteLine($\"[DEBUG] ParseClassDefRaw: Unexpected token in base classes: {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("            break;");
            WriteLine("        }");
            WriteLine("    }");
            WriteLine("    if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \")\")");
            WriteLine("    {");
            WriteLine("        Advance(); // consume ')'");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("// Expect ':'");
            WriteLine("if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \":\")");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseClassDefRaw: Expected ':' but found {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("Advance(); // consume ':'");
            WriteLine();
            WriteLine("// CPython 3.12: class_def_raw: 'class' NAME ... ':' block");
            WriteLine("// block: NEWLINE INDENT statements DEDENT | simple_stmts");
            WriteLine("var body = ParseBlock();");
            WriteLine("if (body == null || body.Count == 0)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseClassDefRaw: Failed to parse class body\");");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseClassDefRaw: Successfully parsed class '{className}' with {baseClasses.Count} base classes and {body.Count} statements\");");
            WriteLine("return new GeneratedClassDefStmt");
            WriteLine("{");
            WriteLine("    Name = className,");
            WriteLine("    Bases = baseClasses,");
            WriteLine("    Keywords = new GeneratedExprSeq(),");
            WriteLine("    Body = body");
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add ParseMatchStatement for Python 3.10+ pattern matching
            WriteLine("/// <summary>");
            WriteLine("/// match_stmt[stmt_ty]: \"match\" subject_expr ':' NEWLINE INDENT cases DEDENT");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedStmt ParseMatchStatement()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseMatchStatement: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("var startPos = _position;");
            WriteLine();
            WriteLine("// 'match'");
            WriteLine("if (!ExpectKeyword(\"match\"))");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseMatchStatement: Failed to match 'match' keyword at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// Parse subject expression (simplified - just expect NAME for now)");
            WriteLine("var subjectName = ExpectName();");
            WriteLine("if (subjectName == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseMatchStatement: Failed to match subject at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine("// Create subject as GeneratedExpr");
            WriteLine("var subject = new GeneratedNameExpr { Id = subjectName };");
            WriteLine();
            WriteLine("// ':'");
            WriteLine("if (ExpectToken(TokenType.OP, \":\") == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseMatchStatement: Failed to match ':' at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// NEWLINE");
            WriteLine("if (ExpectToken(TokenType.NEWLINE) == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseMatchStatement: Failed to match NEWLINE at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// INDENT");
            WriteLine("if (ExpectToken(TokenType.INDENT) == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseMatchStatement: Failed to match INDENT at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// Parse case blocks");
            WriteLine("var cases = new List<object>();");
            WriteLine("while (CurrentToken?.Type == TokenType.NAME && CurrentToken.Value == \"case\")");
            WriteLine("{");
            WriteLine("    var caseBlock = ParseCaseBlock();");
            WriteLine("    if (caseBlock != null)");
            WriteLine("    {");
            WriteLine("        cases.Add(caseBlock);");
            WriteLine("    }");
            WriteLine("    else");
            WriteLine("    {");
            WriteLine("        break;");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("// DEDENT - consume all remaining DEDENTs to get back to match level");
            WriteLine("var dedentCount = 0;");
            WriteLine("while (CurrentToken?.Type == TokenType.DEDENT)");
            WriteLine("{");
            WriteLine("    dedentCount++;");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseMatchStatement: Consuming DEDENT #{dedentCount} at position {_position}\");");
            WriteLine("    Advance();");
            WriteLine("}");
            WriteLine("if (dedentCount == 0)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseMatchStatement: No DEDENT found at position {_position}, current token: {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseMatchStatement: Successfully parsed match statement with {cases.Count} cases at position {_position}\");");
            WriteLine("return new GeneratedMatchStmt");
            WriteLine("{");
            WriteLine("    Subject = subject,");
            WriteLine("    Cases = cases");
            WriteLine("};");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// case_block[match_case_ty]: \"case\" pattern ':' body");
            WriteLine("/// </summary>");
            WriteLine("public object ParseCaseBlock()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCaseBlock: Starting at position {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Value}'\");");
            WriteLine();
            WriteLine("var startPos = _position;");
            WriteLine();
            WriteLine("// 'case'");
            WriteLine("if (!ExpectKeyword(\"case\"))");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseCaseBlock: Failed to match 'case' keyword at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// Parse pattern (support NAME, NUMBER, STRING literals)");
            WriteLine("GeneratedExpr pattern = null;");
            WriteLine("if (CurrentToken?.Type == TokenType.NAME)");
            WriteLine("{");
            WriteLine("    // Create a NAME expression object for pattern");
            WriteLine("    pattern = new GeneratedNameExpr { Id = CurrentToken.Value };");
            WriteLine("    Advance(); // consume NAME");
            WriteLine("}");
            WriteLine("else if (CurrentToken?.Type == TokenType.NUMBER)");
            WriteLine("{");
            WriteLine("    // Create a NUMBER expression object for pattern - CPython 3.12");
            WriteLine("    var numStr = CurrentToken.Value;");
            WriteLine("    PyObject numVal = int.TryParse(numStr, out int i) ? new PyInt(i) : ");
            WriteLine("                      double.TryParse(numStr, out double d) ? new PyFloat(d) : new PyString(numStr);");
            WriteLine("    pattern = new GeneratedConstantExpr { Value = numVal, Kind = \"number\" };");
            WriteLine("    Advance(); // consume NUMBER");
            WriteLine("}");
            WriteLine("else if (CurrentToken?.Type == TokenType.STRING)");
            WriteLine("{");
            WriteLine("    // Create a STRING expression object for pattern - CPython 3.12");
            WriteLine("    pattern = new GeneratedConstantExpr");
            WriteLine("    {");
            WriteLine("        Value = new PyString(CurrentToken.Value),");
            WriteLine("        Kind = \"string\"");
            WriteLine("    };");
            WriteLine("    Advance(); // consume STRING");
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseCaseBlock: Failed to match pattern at position {_position}, token: {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// ':'");
            WriteLine("if (ExpectToken(TokenType.OP, \":\") == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseCaseBlock: Failed to match ':' at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("// CPython 3.12: case_block: 'case' pattern guard? ':' block");
            WriteLine("// block: NEWLINE INDENT statements DEDENT | simple_stmts");
            WriteLine("var body = ParseBlock();");
            WriteLine("if (body == null || body.Count == 0)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseCaseBlock: Failed to parse case body at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCaseBlock: Successfully parsed case block with {body.Count} statements\");");
            WriteLine("return new { pattern = pattern, guard = (object)null, body = body };");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseCaseBlock: Failed to parse case body at position {_position}, token: {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("_position = startPos;");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate type parameter parsing methods for PEP 695
        /// </summary>
        private void GenerateTypeParameterMethods()
        {
            // ParseTypeParams method
            WriteLine("/// <summary>");
            WriteLine("/// type_params[asdl_type_param_seq*]: '[' t=type_param_seq ']'");
            WriteLine("/// Parse optional type parameters for PEP 695");
            WriteLine("/// </summary>");
            WriteLine("public List<GeneratedTypeParam>? ParseTypeParams()");
            WriteLine("{");
            Indent();
            WriteLine("// Check for '[' - if not present, return null (no type parameters)");
            WriteLine("if (CurrentToken?.Type != TokenType.OP || CurrentToken?.Value != \"[\")");
            WriteLine("{");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("var startPos = _position;");
            WriteLine("Advance(); // consume '['");
            WriteLine();
            WriteLine("var typeParams = new List<GeneratedTypeParam>();");
            WriteLine();
            WriteLine("// Parse type_param_seq: comma-separated type parameters");
            WriteLine("while (CurrentToken != null)");
            WriteLine("{");
            WriteLine("    var typeParam = ParseTypeParam();");
            WriteLine("    if (typeParam == null)");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseTypeParams: Failed to parse type parameter at position {_position}\");");
            WriteLine("        _position = startPos;");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine();
            WriteLine("    typeParams.Add(typeParam);");
            WriteLine();
            WriteLine("    // Check for comma or end");
            WriteLine("    if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \",\")");
            WriteLine("    {");
            WriteLine("        Advance(); // consume ','");
            WriteLine("        continue;");
            WriteLine("    }");
            WriteLine("    else if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"]\")");
            WriteLine("    {");
            WriteLine("        break; // End of type parameters");
            WriteLine("    }");
            WriteLine("    else");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseTypeParams: Expected ',' or ']' at position {_position}\");");
            WriteLine("        _position = startPos;");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("// Expect ']'");
            WriteLine("if (Expect(\"]\") == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseTypeParams: Failed to match ']' at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseTypeParams: Successfully parsed {typeParams.Count} type parameters\");");
            WriteLine("return typeParams;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // ParseTypeParam method
            WriteLine("/// <summary>");
            WriteLine("/// type_param[type_param_ty]: TypeVar | TypeVarTuple | ParamSpec");
            WriteLine("/// Parse individual type parameter (T, *Ts, **P)");
            WriteLine("/// </summary>");
            WriteLine("public GeneratedTypeParam? ParseTypeParam()");
            WriteLine("{");
            Indent();
            WriteLine("var startPos = _position;");
            WriteLine("var _start_lineno = _tokens[startPos].Line;");
            WriteLine("var _start_col_offset = _tokens[startPos].Column;");
            WriteLine();
            WriteLine("// Check for TypeVarTuple: '*' NAME");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"*\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume '*'");
            WriteLine("    var nameToken = ExpectName();");
            WriteLine("    if (nameToken == null)");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseTypeParam: Failed to parse TypeVarTuple name at position {_position}\");");
            WriteLine("        _position = startPos;");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine();
            WriteLine("    var name = nameToken.Value;");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseTypeParam: Parsed TypeVarTuple '{name}'\");");
            WriteLine("    var _end_lineno = _tokens[_position - 1].EndLine;");
            WriteLine("    var _end_col_offset = _tokens[_position - 1].EndColumn;");
            WriteLine("    return _PyAST_TypeVarTuple(name, _start_lineno, _start_col_offset, _end_lineno, _end_col_offset);");
            WriteLine("}");
            WriteLine();
            WriteLine("// Check for ParamSpec: '**' NAME");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \"**\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume '**'");
            WriteLine("    var nameToken = ExpectName();");
            WriteLine("    if (nameToken == null)");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseTypeParam: Failed to parse ParamSpec name at position {_position}\");");
            WriteLine("        _position = startPos;");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine();
            WriteLine("    var name = nameToken.Value;");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseTypeParam: Parsed ParamSpec '{name}'\");");
            WriteLine("    var _end_lineno = _tokens[_position - 1].EndLine;");
            WriteLine("    var _end_col_offset = _tokens[_position - 1].EndColumn;");
            WriteLine("    return _PyAST_ParamSpec(name, _start_lineno, _start_col_offset, _end_lineno, _end_col_offset);");
            WriteLine("}");
            WriteLine();
            WriteLine("// TypeVar: NAME [':' expression]");
            WriteLine("var typeVarNameToken = ExpectName();");
            WriteLine("if (typeVarNameToken == null)");
            WriteLine("{");
            WriteLine("    Console.WriteLine($\"[DEBUG] ParseTypeParam: Failed to parse TypeVar name at position {_position}\");");
            WriteLine("    _position = startPos;");
            WriteLine("    return null;");
            WriteLine("}");
            WriteLine();
            WriteLine("var typeVarName = typeVarNameToken.Value;");
            WriteLine();
            WriteLine("// Optional bound: ':' expression");
            WriteLine("GeneratedExpr? bound = null;");
            WriteLine("if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Value == \":\")");
            WriteLine("{");
            WriteLine("    Advance(); // consume ':'");
            WriteLine("    bound = Expression();");
            WriteLine("    if (bound == null)");
            WriteLine("    {");
            WriteLine("        Console.WriteLine($\"[DEBUG] ParseTypeParam: Failed to parse TypeVar bound at position {_position}\");");
            WriteLine("        _position = startPos;");
            WriteLine("        return null;");
            WriteLine("    }");
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseTypeParam: Parsed TypeVar '{typeVarName}' with bound: {bound != null}\");");
            WriteLine("var _end_lineno2 = _tokens[_position - 1].EndLine;");
            WriteLine("var _end_col_offset2 = _tokens[_position - 1].EndColumn;");
            WriteLine("return _PyAST_TypeVar(typeVarName, bound, _start_lineno, _start_col_offset, _end_lineno2, _end_col_offset2);");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        // GenerateImprovedParseAssignmentMethod() removed
        // ParseAssignment is now implemented in PyParserBase.cs (CPython 3.12 compatible)
        // This avoids code generation confusion and ensures proper context handling

        /// <summary>
        /// CPython 3.12: Get or create a loop rule
        /// Called by ItemCodeGenerator when it encounters Repeat0/Repeat1
        /// Returns the rule name (e.g., "_Loop1_4")
        /// </summary>
        public string GetOrCreateLoopRule(string loopType, string returnType, string innerPattern, Atom innerAtom = null)
        {
            var pattern = $"{loopType}:{returnType}:{innerPattern}";

            if (_loopRules.TryGetValue(pattern, out var existingRule))
            {
                return existingRule;
            }

            // Create new loop rule - store both pattern string and Atom for generation
            var ruleName = $"_{loopType}_{_loopRuleCounter++}";
            _loopRules[pattern] = ruleName;

            // Store Atom for complex pattern generation (if provided)
            if (innerAtom != null)
            {
                _loopRuleAtoms[ruleName] = innerAtom;
            }

            Console.WriteLine($"[CODEGEN] Registered loop rule: {ruleName} for pattern: {pattern}");
            return ruleName;
        }

        /// <summary>
        /// CPython 3.12: artificial_rule_from_repeat mechanism
        /// Generate all loop rule methods (_Loop0_N, _Loop1_N)
        /// </summary>
        private void GenerateLoopRuleMethods()
        {
            WriteLine();
            WriteLine("// ========================================");
            WriteLine("// CPython 3.12: Loop Rules (artificial_rule_from_repeat)");
            WriteLine("// Generated from Repeat0/Repeat1 patterns");
            WriteLine("// ========================================");
            WriteLine();

            Console.WriteLine($"[CODEGEN] GenerateLoopRuleMethods: Total loop rules = {_loopRules.Count}");
            // Loop rules are populated by ItemCodeGenerator during rule generation
            // Now we generate the actual methods
            foreach (var (pattern, ruleName) in _loopRules)
            {
                Console.WriteLine($"[CODEGEN] Generating loop rule: {ruleName} for pattern: {pattern}");
                // Pattern format: "Loop0:ReturnType:InnerPattern" or "Loop1:ReturnType:InnerPattern"
                var parts = pattern.Split(':', 3);
                if (parts.Length < 3)
                {
                    Console.WriteLine($"[CODEGEN] WARNING: Skipping invalid pattern: {pattern}");
                    continue;
                }

                var loopType = parts[0]; // "Loop0" or "Loop1"
                var returnType = parts[1];
                var innerPattern = parts[2];

                Console.WriteLine($"[CODEGEN] Loop type: {loopType}, return type: {returnType}, inner: {innerPattern}");
                if (loopType == "Loop0")
                {
                    GenerateLoop0Rule(ruleName, returnType, innerPattern);
                }
                else if (loopType == "Loop1")
                {
                    GenerateLoop1Rule(ruleName, returnType, innerPattern);
                }
                Console.WriteLine($"[CODEGEN] Successfully generated loop rule: {ruleName}");
            }
            Console.WriteLine($"[CODEGEN] GenerateLoopRuleMethods: DONE");
        }

        /// <summary>
        /// Generate _Loop0_N rule (zero or more)
        /// CPython: while ((_item = rule()) != NULL) { _mark = p->mark; ... }
        /// Returns NULL if no items (totalSize == 0)
        /// returnType is already List<InnerType> from DetermineSequenceType
        /// </summary>
        private void GenerateLoop0Rule(string ruleName, string returnType, string innerPattern)
        {
            // CPython 3.12: returnType is non-nullable (e.g., GeneratedExprSeq)
            WriteLine($"private {returnType}? {ruleName}()");  // Add '?' for nullable return (can return null)
            WriteLine("{");
            Indent();

            // CPython pattern: Save mark after each success, restore on failure
            WriteLine($"var _items = new {returnType}();");
            WriteLine("int _loop_mark = _position;  // CPython: int _mark = p->mark");
            WriteLine("while (true)");
            WriteLine("{");
            Indent();

            // Check if complex pattern (Atom stored)
            if (_loopRuleAtoms.TryGetValue(ruleName, out var atom))
            {
                // Complex pattern: generate inline parsing code
                var innerItem = new Item { Atom = atom, Name = null };
                var innerGen = new ItemCodeGenerator(this, innerItem, "_item", "", insideRepeater: false, insideOptional: false, insideLoopRule: true);
                innerGen.Generate();
            }
            else
            {
                // Simple pattern: use innerPattern string
                WriteLine($"var _item = {innerPattern};");
            }

            WriteLine("if (_item == null)");
            WriteLine("{");
            Indent();
            WriteLine("// CPython: p->mark = _mark (restore to last success position)");
            WriteLine("_position = _loop_mark;");
            WriteLine("break;");
            Dedent();
            WriteLine("}");
            WriteLine("_items.Add(_item);");
            WriteLine("// CPython: _mark = p->mark (save position after success)");
            WriteLine("_loop_mark = _position;");
            Dedent();
            WriteLine("}");
            WriteLine("// CPython: Returns NULL if totalSize == 0");
            WriteLine("if (_items.Count == 0) return null;");
            WriteLine("return _items;");

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate _Loop1_N rule (one or more)
        /// CPython: if ((_item = rule()) == NULL) return NULL;
        /// while ((_item = rule()) != NULL) { ... }
        /// Returns NULL if no items (totalSize == 0)
        /// returnType is already List<InnerType> from DetermineSequenceType
        /// </summary>
        private void GenerateLoop1Rule(string ruleName, string returnType, string innerPattern)
        {
            // CPython 3.12: returnType is non-nullable (e.g., GeneratedExprSeq)
            WriteLine($"private {returnType}? {ruleName}()");  // Add '?' for nullable return (can return null)
            WriteLine("{");
            Indent();

            // CPython pattern: Save mark after each success, restore on failure
            WriteLine($"var _items = new {returnType}();");
            WriteLine("// CPython: First element required");

            // First element
            if (_loopRuleAtoms.TryGetValue(ruleName, out var atom))
            {
                // Complex pattern: generate inline parsing code
                var innerItem = new Item { Atom = atom, Name = null };
                var innerGen = new ItemCodeGenerator(this, innerItem, "_first", "", insideRepeater: false, insideOptional: false, insideLoopRule: true);
                innerGen.Generate();
            }
            else
            {
                // Simple pattern: use innerPattern string
                WriteLine($"var _first = {innerPattern};");
            }

            WriteLine("if (_first == null) return null;");
            WriteLine("_items.Add(_first);");
            WriteLine("int _loop_mark = _position;  // CPython: int _mark = p->mark");
            WriteLine("while (true)");
            WriteLine("{");
            Indent();

            // Loop body
            if (_loopRuleAtoms.TryGetValue(ruleName, out atom))
            {
                // Complex pattern: generate inline parsing code
                var innerItem = new Item { Atom = atom, Name = null };
                var innerGen = new ItemCodeGenerator(this, innerItem, "_item", "", insideRepeater: false, insideOptional: false, insideLoopRule: true);
                innerGen.Generate();
            }
            else
            {
                // Simple pattern: use innerPattern string
                WriteLine($"var _item = {innerPattern};");
            }
            WriteLine("if (_item == null)");
            WriteLine("{");
            Indent();
            WriteLine("// CPython: p->mark = _mark (restore to last success position)");
            WriteLine("_position = _loop_mark;");
            WriteLine("break;");
            Dedent();
            WriteLine("}");
            WriteLine("_items.Add(_item);");
            WriteLine("// CPython: _mark = p->mark (save position after success)");
            WriteLine("_loop_mark = _position;");
            Dedent();
            WriteLine("}");
            WriteLine("// CPython: Always has at least 1 item");
            WriteLine("return _items;");

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateStringHelperMethods()
        {
            WriteLine("// ========== CPython 3.12: String parsing helper methods ==========");
            WriteLine();

            // _PyPegen_constant_from_string
            WriteLine("// CPython: _PyPegen_constant_from_string");
            WriteLine("// Convert STRING token to Constant AST node - accepts both token and already-converted constant");
            WriteLine("private GeneratedExpr _PyPegen_constant_from_string(GeneratedPtr tokenOrConstant)");
            WriteLine("{");
            Indent();
            WriteLine("// Handle if already converted to constant (from StringToken)");
            WriteLine("if (tokenOrConstant is GeneratedConstant constant)");
            Indent();
            WriteLine("return constant; // Already converted, return as-is");
            Dedent();
            WriteLine();
            WriteLine("// Handle raw token");
            WriteLine("if (tokenOrConstant is GeneratedTokenInfo token)");
            WriteLine("{");
            Indent();
            WriteLine("// STRING token value includes quotes (e.g., \\\"hello\\\", 'world', r\\\"raw\\\")");
            WriteLine("var value = token.Value;");
            WriteLine("// Decode string literal: remove quotes and handle escape sequences");
            WriteLine("var decoded = DecodeStringLiteral(value);");
            WriteLine("var pyConstant = new GeneratedPyConstantString(decoded);");
            WriteLine("return _PyAST_Constant(pyConstant, null, token.Line, token.Column, token.EndLine, token.EndColumn);");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("throw new ArgumentException(\"Expected GeneratedTokenInfo or GeneratedConstant\", nameof(tokenOrConstant));");
            Dedent();
            WriteLine("}");
            WriteLine();

            // _PyPegen_concatenate_strings
            WriteLine("// CPython: _PyPegen_concatenate_strings");
            WriteLine("// Concatenate multiple string literals: \\\"a\\\" \\\"b\\\" -> \\\"ab\\\"");
            WriteLine("private GeneratedExpr _PyPegen_concatenate_strings(GeneratedExprSeq strings, int lineno, int col, int? end_lineno, int? end_col)");
            WriteLine("{");
            Indent();
            WriteLine("// Single string: return as-is");
            WriteLine("if (strings.Count == 1)");
            Indent();
            WriteLine("return strings[0];");
            Dedent();
            WriteLine();
            WriteLine("// Multiple strings: concatenate values");
            WriteLine("var concatenated = new System.Text.StringBuilder();");
            WriteLine("foreach (var str in strings)");
            WriteLine("{");
            Indent();
            WriteLine("if (str is GeneratedConstant constant && constant.Value is GeneratedPyConstantString strConst)");
            WriteLine("{");
            Indent();
            WriteLine("concatenated.Append(strConst.Value);");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("var pyConstant = new GeneratedPyConstantString(concatenated.ToString());");
            WriteLine("return _PyAST_Constant(pyConstant, null, lineno, col, end_lineno ?? 0, end_col ?? 0);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // DecodeStringLiteral - CPython 3.12: Parser/string_parser.c
            WriteLine("// CPython: _PyPegen_parse_string (string_parser.c)");
            WriteLine("// Decode string literal: remove quotes and handle escape sequences");
            WriteLine("private string DecodeStringLiteral(string literal)");
            WriteLine("{");
            Indent();
            WriteLine("if (string.IsNullOrEmpty(literal) || literal.Length < 2)");
            Indent();
            WriteLine("return literal;");
            Dedent();
            WriteLine();
            WriteLine("// Check for raw string prefix");
            WriteLine("bool isRawString = literal.StartsWith(\"r'\") || literal.StartsWith(\"r\\\"\") || ");
            WriteLine("                   literal.StartsWith(\"R'\") || literal.StartsWith(\"R\\\"\");");
            WriteLine("int startIdx = isRawString ? 2 : 1;");
            WriteLine("int endIdx = literal.Length - 1;");
            WriteLine();
            WriteLine("// Extract content between quotes");
            WriteLine("var content = literal.Substring(startIdx, endIdx - startIdx);");
            WriteLine();
            WriteLine("// Raw strings: no escape processing");
            WriteLine("if (isRawString)");
            Indent();
            WriteLine("return content;");
            Dedent();
            WriteLine();
            WriteLine("// Process escape sequences (CPython 3.12 compatible)");
            WriteLine("var result = new System.Text.StringBuilder(content.Length);");
            WriteLine("for (int i = 0; i < content.Length; i++)");
            WriteLine("{");
            Indent();
            WriteLine("if (content[i] == '\\\\' && i + 1 < content.Length)");
            WriteLine("{");
            Indent();
            WriteLine("char next = content[i + 1];");
            WriteLine("switch (next)");
            WriteLine("{");
            Indent();
            WriteLine("case 'n': result.Append('\\n'); i++; break;");
            WriteLine("case 't': result.Append('\\t'); i++; break;");
            WriteLine("case 'r': result.Append('\\r'); i++; break;");
            WriteLine("case '\\\\': result.Append('\\\\'); i++; break;");
            WriteLine("case '\\'': result.Append('\\''); i++; break;");
            WriteLine("case '\\\"': result.Append('\\\"'); i++; break;");
            WriteLine("case '0': result.Append('\\0'); i++; break;");
            WriteLine("default: result.Append('\\\\'); break; // Unknown escape: keep backslash");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("result.Append(content[i]);");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("return result.ToString();");
            Dedent();
            WriteLine("}");
            WriteLine();

            // _PyPegen_joined_str - CPython: Parser/pegen.c
            WriteLine("// CPython: _PyPegen_joined_str (pegen.c)");
            WriteLine("// Create JoinedStr node from f-string tokens");
            WriteLine("private GeneratedExpr _PyPegen_joined_str(GeneratedTokenInfo start, GeneratedExprSeq middle, GeneratedTokenInfo end)");
            WriteLine("{");
            Indent();
            WriteLine("if (start == null || end == null)");
            Indent();
            WriteLine("return null;");
            Dedent();
            WriteLine();
            WriteLine("// Create list of string parts and expressions");
            WriteLine("var values = middle ?? new GeneratedExprSeq();");
            WriteLine("int endLine = end.EndLine > 0 ? end.EndLine : end.Line;");
            WriteLine("int endCol = end.EndColumn > 0 ? end.EndColumn : end.Column;");
            WriteLine("return _PyAST_JoinedStr(values, start.Line, start.Column, endLine, endCol);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // _PyPegen_constant_from_token
            WriteLine("// CPython: _PyPegen_constant_from_token");
            WriteLine("// Create Constant node from FSTRING_MIDDLE token");
            WriteLine("private GeneratedExpr _PyPegen_constant_from_token(GeneratedTokenInfo token)");
            WriteLine("{");
            Indent();
            WriteLine("if (token == null)");
            Indent();
            WriteLine("return null;");
            Dedent();
            WriteLine();
            WriteLine("// FSTRING_MIDDLE contains plain text (no quotes needed)");
            WriteLine("var pyConstant = new GeneratedPyConstantString(token.Value);");
            WriteLine("int endLine = token.EndLine > 0 ? token.EndLine : token.Line;");
            WriteLine("int endCol = token.EndColumn > 0 ? token.EndColumn : token.Column;");
            WriteLine("return _PyAST_Constant(pyConstant, null, token.Line, token.Column, endLine, endCol);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // _PyPegen_check_fstring_conversion
            WriteLine("// CPython: _PyPegen_check_fstring_conversion (action_helpers.c)");
            WriteLine("// Validate f-string conversion (!s, !r, !a)");
            WriteLine("private ResultTokenWithMetadata _PyPegen_check_fstring_conversion(GeneratedTokenInfo conv_token, GeneratedExpr conv)");
            WriteLine("{");
            Indent();
            WriteLine("if (conv == null || conv_token == null)");
            Indent();
            WriteLine("return null;");
            Dedent();
            WriteLine();
            WriteLine("// conv should be a Name node");
            WriteLine("if (conv is GeneratedName name)");
            WriteLine("{");
            Indent();
            WriteLine("var conversion = name.Id;");
            WriteLine("// CPython 3.12: Only 's', 'r', 'a' are valid");
            WriteLine("if (conversion != \"s\" && conversion != \"r\" && conversion != \"a\")");
            WriteLine("{");
            Indent();
            WriteLine("RaiseErrorKnownLocation(conv_token, $\"f-string: invalid conversion character '{conversion}': expected 's', 'r', or 'a'\");");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Return metadata with conversion token");
            WriteLine("return new ResultTokenWithMetadata { Token = conv_token, Metadata = conversion };");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // _PyPegen_formatted_value
            WriteLine("// CPython: _PyPegen_formatted_value (action_helpers.c)");
            WriteLine("// Create FormattedValue node for f-string replacement field");
            WriteLine("private GeneratedExpr _PyPegen_formatted_value(GeneratedExpr expr, GeneratedTokenInfo debug_expr, GeneratedPtr conversion, GeneratedPtr format, GeneratedTokenInfo rbrace, int lineno, int col, int end_lineno, int end_col)");
            WriteLine("{");
            Indent();
            WriteLine("if (expr == null)");
            Indent();
            WriteLine("return null;");
            Dedent();
            WriteLine();
            WriteLine("#if DEBUG_FSTRING_LOG");
            WriteLine("Console.WriteLine($\"[FSTRING-FORMATTED_VALUE] _PyPegen_formatted_value called:\");");
            WriteLine("Console.WriteLine($\"  conversion is null: {conversion == null}\");");
            WriteLine("if (conversion != null)");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"  conversion.GetType(): {conversion.GetType().Name}\");");
            WriteLine("if (conversion is ResultTokenWithMetadata convDebug)");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"  conversion.Metadata: {convDebug.Metadata}\");");
            WriteLine("if (convDebug.Metadata != null)");
            Indent();
            WriteLine("Console.WriteLine($\"  conversion.Metadata.GetType(): {convDebug.Metadata.GetType().Name}\");");
            Dedent();
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("#endif");
            WriteLine();
            WriteLine("// Parse conversion character (!s, !r, !a)");
            WriteLine("int conversionChar = -1; // -1 = no conversion");
            WriteLine("if (conversion is ResultTokenWithMetadata convMeta && convMeta.Metadata is GeneratedIdentifier convId)");
            WriteLine("{");
            Indent();
            WriteLine("string convStr = convId.Value;");
            WriteLine("if (convStr.Length > 0)");
            Indent();
            WriteLine("conversionChar = convStr[0]; // 's', 'r', or 'a'");
            Dedent();
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Parse format spec");
            WriteLine("GeneratedExpr formatSpec = null;");
            WriteLine("if (format is ResultTokenWithMetadata fmtMeta && fmtMeta.Metadata is GeneratedExpr formatExpr)");
            WriteLine("{");
            Indent();
            WriteLine("formatSpec = formatExpr;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("return _PyAST_FormattedValue(expr, conversionChar, formatSpec, lineno, col, end_lineno, end_col);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // _PyPegen_decoded_constant_from_token
            WriteLine("// CPython: _PyPegen_decoded_constant_from_token");
            WriteLine("// Create Constant from token without decoding (for FSTRING_MIDDLE)");
            WriteLine("private GeneratedExpr _PyPegen_decoded_constant_from_token(GeneratedTokenInfo token)");
            WriteLine("{");
            Indent();
            WriteLine("// Same as _PyPegen_constant_from_token - FSTRING_MIDDLE is already decoded");
            WriteLine("return _PyPegen_constant_from_token(token);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // _PyPegen_setup_full_format_spec
            WriteLine("// CPython: _PyPegen_setup_full_format_spec (action_helpers.c)");
            WriteLine("// Create format spec from colon and spec parts");
            WriteLine("private ResultTokenWithMetadata _PyPegen_setup_full_format_spec(GeneratedTokenInfo colon, GeneratedExprSeq spec, int lineno, int col, int end_lineno, int end_col)");
            WriteLine("{");
            Indent();
            WriteLine("if (colon == null)");
            Indent();
            WriteLine("return null;");
            Dedent();
            WriteLine();
            WriteLine("// If no spec parts, create empty JoinedStr");
            WriteLine("if (spec == null || spec.Count == 0)");
            WriteLine("{");
            Indent();
            WriteLine("var emptySeq = new GeneratedExprSeq();");
            WriteLine("var emptyJoined = _PyAST_JoinedStr(emptySeq, lineno, col, end_lineno, end_col);");
            WriteLine("return new ResultTokenWithMetadata { Token = colon, Metadata = emptyJoined };");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Create JoinedStr from spec parts");
            WriteLine("var joinedStr = _PyAST_JoinedStr(spec, lineno, col, end_lineno, end_col);");
            WriteLine("return new ResultTokenWithMetadata { Token = colon, Metadata = joinedStr };");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        private void GeneratePyPegen_add_type_comment_to_arg()
        {
            WriteLine("// CPython 3.12: _PyPegen_add_type_comment_to_arg");
            WriteLine("// Adds type comment to argument (for Python 2 compatibility)");
            WriteLine("private GeneratedArg _PyPegen_add_type_comment_to_arg(GeneratedArg arg, GeneratedTokenInfo tc)");
            WriteLine("{");
            Indent();
            WriteLine("// If no type comment, return arg as-is");
            WriteLine("if (tc == null)");
            Indent();
            WriteLine("return arg;");
            Dedent();
            WriteLine();
            WriteLine("// Type comments are stored as strings in GeneratedArg.TypeComment");
            WriteLine("// For now, we ignore type comments (Python 2 legacy feature)");
            WriteLine("// CPython 3.12 also mostly ignores them");
            WriteLine("return arg;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// CPython 3.12: artifical_rule_from_gather
        /// Creates helper rules for gather patterns: separator.item+ or separator.item*
        /// Example: ','.expression+ generates:
        ///   - _loop0_N: ',' element (zero or more)
        ///   - _gather_N: element _loop0_N (first element + loop)
        /// </summary>
        public string ArtificialRuleFromGather(Gather gather, string typeAnnotation = null)
        {
            // Create unique pattern key
            string gatherPattern = $"{gather.Separator}:{gather.Item}:{gather.IsOneOrMore}";

            // Check if already generated
            if (_gatherRules.TryGetValue(gatherPattern, out var existingName))
            {
                Console.WriteLine($"[CODEGEN] Reusing gather rule: {existingName} for pattern: {gatherPattern}");
                return existingName;
            }

            // Generate _loop0_N rule: separator element
            // CPython 3.12: Use same counter for all artificial rules
            _loopRuleCounter++;
            string loopRuleName = $"_Loop0_{_loopRuleCounter}";

            // CPython 3.12: Create loop rule as artificial rule (not in _loopRules)
            // Loop rule collects: separator element
            var loopAlt = new Alternative
            {
                Items = new List<Item>
                {
                    new Item { Atom = gather.Separator, Name = null },
                    new Item { Atom = gather.Item, Name = "elem" }
                },
                Action = "elem"  // Return only element, discard separator
            };

            var loopRule = new Rule
            {
                Name = loopRuleName,
                // Mark as special loop rule (needs while loop, not alternatives)
                ReturnType = $"__LOOP0__:{typeAnnotation ?? "GeneratedSeq"}",
                Alternatives = new List<Alternative> { loopAlt },
                IsMemoized = false
            };

            _artificialRules.Add(loopRule);
            Console.WriteLine($"[CODEGEN] Created artificial loop rule: {loopRuleName} for gather");

            // Generate _gather_N rule: element _loop0_N
            // CPython 3.12: Use same counter for all artificial rules
            _loopRuleCounter++;
            string gatherRuleName = $"_Gather_{_loopRuleCounter}";

            var gatherAlt = new Alternative
            {
                Items = new List<Item>
                {
                    new Item { Atom = gather.Item, Name = "elem" },
                    new Item { Atom = new RuleRef { Name = loopRuleName }, Name = "seq" }
                },
                // CPython 3.12: _gather uses _PyPegen_seq_insert_in_front
                Action = "_PyPegen_seq_insert_in_front(p, elem, seq)"
            };

            var gatherRule = new Rule
            {
                Name = gatherRuleName,
                // CPython 3.12: typeAnnotation is already C# type (e.g., "GeneratedStmtSeq")
                // Store as "__CSHARP__:" prefix to indicate no translation needed
                ReturnType = typeAnnotation != null ? $"__CSHARP__:{typeAnnotation}" : null,
                Alternatives = new List<Alternative> { gatherAlt },
                IsMemoized = false
            };

            _artificialRules.Add(gatherRule);
            Console.WriteLine($"[CODEGEN] Created gather rule: {gatherRuleName} for gather pattern");

            // Register
            _gatherRules[gatherPattern] = gatherRuleName;

            return gatherRuleName;
        }

        /// <summary>
        /// Generate artificial rules (gather helpers) after all main rules
        /// Called from GenerateParserClassBody
        /// </summary>
        private void GenerateArtificialRules()
        {
            if (_artificialRules.Count == 0)
            {
                return;
            }

            WriteLine();
            WriteLine("// ========================================");
            WriteLine("// CPython 3.12: Artificial Rules (gather helpers)");
            WriteLine("// Generated from gather patterns: sep.item+ or sep.item*");
            WriteLine("// ========================================");
            WriteLine();

            Console.WriteLine($"[CODEGEN] GenerateArtificialRules: Total artificial rules = {_artificialRules.Count}");

            foreach (var rule in _artificialRules)
            {
                Console.WriteLine($"[CODEGEN] Generating artificial rule: {rule.Name}");

                // CPython 3.12: Check if this is a loop rule (_loop0_ or _loop1_)
                bool isLoop0 = !string.IsNullOrEmpty(rule.ReturnType) && rule.ReturnType.StartsWith("__LOOP0__:");
                bool isLoop1 = !string.IsNullOrEmpty(rule.ReturnType) && rule.ReturnType.StartsWith("__LOOP1__:");

                // Determine return type
                string returnType;
                if (isLoop0)
                {
                    returnType = rule.ReturnType.Substring("__LOOP0__:".Length);
                }
                else if (isLoop1)
                {
                    returnType = rule.ReturnType.Substring("__LOOP1__:".Length);
                }
                else if (!string.IsNullOrEmpty(rule.ReturnType) && rule.ReturnType.StartsWith("__CSHARP__:"))
                {
                    // Already C# type
                    returnType = rule.ReturnType.Substring("__CSHARP__:".Length);
                }
                else
                {
                    returnType = rule.ReturnType ?? "GeneratedSeq";
                }

                // Generate the rule method
                WriteLine($"// CPython 3.12: Artificial rule {rule.Name}");
                WriteLine($"private {returnType}? {rule.Name}()");
                WriteLine("{");
                Indent();

                if (isLoop0 || isLoop1)
                {
                    // CPython 3.12: Generate while loop structure for _loop0_/_loop1_
                    GenerateArtificialLoopRule(rule, returnType, isLoop0);
                }
                else
                {
                    // Regular alternative structure for _gather_ rules
                    WriteLine($"int _mark = _position;");
                    WriteLine($"{returnType}? _res = null;");
                    WriteLine();

                    // Generate each alternative
                    for (int i = 0; i < rule.Alternatives.Count; i++)
                    {
                        var altGen = new AlternativeCodeGenerator(this, rule, rule.Alternatives[i], i);
                        altGen.Generate();
                    }

                    // CPython 3.12: done label for goto statements
                    WriteLine("_res = null;");
                    WriteLine("done:");
                    WriteLine("return _res;");
                }

                Dedent();
                WriteLine("}");
                WriteLine();

                Console.WriteLine($"[CODEGEN] Successfully generated artificial rule: {rule.Name}");
            }

            Console.WriteLine($"[CODEGEN] GenerateArtificialRules: DONE");
        }

        /// <summary>
        /// Generate while loop structure for artificial _loop0_ or _loop1_ rules
        /// CPython 3.12: while loop that collects items
        /// </summary>
        private void GenerateArtificialLoopRule(Rule rule, string returnType, bool isLoop0)
        {
            // CPython 3.12 pattern: while loop collecting items
            WriteLine($"var _items = new {returnType}();");
            WriteLine("int _loop_mark = _position;");
            WriteLine("while (true)");
            WriteLine("{");
            Indent();

            // Generate parsing code for the alternative (separator + element)
            var alt = rule.Alternatives[0];
            foreach (var item in alt.Items)
            {
                string varName = item.Name ?? "_item";
                var itemGen = new ItemCodeGenerator(this, item, varName, "", insideRepeater: true, insideOptional: false, insideLoopRule: true);
                itemGen.Generate();

                WriteLine($"if ({varName} == null)");
                WriteLine("{");
                Indent();
                WriteLine("_position = _loop_mark;");
                WriteLine("break;");
                Dedent();
                WriteLine("}");

                // Only add the last item (element) to collection, discard separator
                if (item == alt.Items.Last())
                {
                    WriteLine($"_items.Add({varName});");
                }
            }

            WriteLine("_loop_mark = _position;");
            Dedent();
            WriteLine("}");

            if (!isLoop0)
            {
                // Loop1: require at least one item
                WriteLine("if (_items.Count == 0) return null;");
            }

            WriteLine("return _items;");
        }

    }
}