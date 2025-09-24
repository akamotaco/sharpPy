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
    public class CSharpCodeGenerator
    {
        private readonly Grammar.Grammar _grammar;
        private readonly List<TokenDefinition> _tokens;
        private readonly StringBuilder _output;
        private int _indentLevel;
        private int _groupCounter;
        private readonly List<PegRule> _pegRules = new();

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
            GenerateHeader();
            GenerateParserClass();
            return _output.ToString();
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
        }

        // GenerateTokenEnum method removed - tokens now generated in tokenizer only

        private void GenerateParserClass()
        {
            WriteLine("namespace SharpPy.Generated");
            WriteLine("{");
            Indent();

            // Generate AST node type definitions first
            WriteLine("// Generated AST node types for CPython 3.12 compatibility");
            WriteLine("public abstract class GeneratedAstNode { }");
            WriteLine("public class GeneratedStmt : GeneratedAstNode");
            WriteLine("{");
            Indent();
            WriteLine("public string? StatementType { get; set; }");
            WriteLine("public object? Value { get; set; }");
            Dedent();
            WriteLine("}");
            WriteLine("public class GeneratedExpr : GeneratedAstNode { }");
            WriteLine("public class GeneratedModule : GeneratedAstNode");
            WriteLine("{");
            Indent();
            WriteLine("public GeneratedStmtSeq? Body { get; set; }");
            Dedent();
            WriteLine("}");
            WriteLine("public class GeneratedSeq : List<GeneratedAstNode> { }");
            WriteLine("public class GeneratedStmtSeq : List<GeneratedStmt> { }");
            WriteLine("public class GeneratedExprSeq : List<GeneratedExpr> { }");
            WriteLine("public class GeneratedIdentifierSeq : List<string> { }");
            WriteLine("public class GeneratedPyObject { }");
            WriteLine("// GeneratedToken type defined in tokenizer");
            WriteLine();

            // TokenInfoWrapper now defined in SharpPy.Tokenizer project

            WriteLine("/// <summary>");
            WriteLine("/// Generated PEG parser for Python 3.12 grammar");
            WriteLine("/// Complete standalone parser without runtime dependencies");
            WriteLine("/// </summary>");
            WriteLine("public class GeneratedPyParser");
            WriteLine("{");
            Indent();

            GenerateParserFields();
            GenerateParserConstructor();
            GenerateParserMethods();

            Dedent();
            WriteLine("}");

            Dedent();
            WriteLine("}"); // Close namespace
        }

        private void GenerateParserFields()
        {
            WriteLine("private readonly List<GeneratedTokenInfo> _tokens;");
            WriteLine("internal int _position;");
            WriteLine("private readonly Dictionary<(int, string), object?> _memoCache = new();");
            WriteLine("private readonly string _filename;");
            WriteLine("private readonly EmbeddedPegInterpreter _interpreter;");
            WriteLine();
        }

        private void GenerateParserConstructor()
        {
            WriteLine("public GeneratedPyParser(List<GeneratedTokenInfo> tokens, string filename = \"<string>\")");
            WriteLine("{");
            Indent();
            WriteLine("_tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));");
            WriteLine("_filename = filename;");
            WriteLine("_position = 0;");
            WriteLine();
            WriteLine("// Initialize embedded PEG interpreter");
            WriteLine("var grammar = CreateEmbeddedGrammar();");
            WriteLine("var tokenWrappers = tokens.Select(t => (IEmbeddedTokenInfo)new EmbeddedTokenInfoAdapter(t)).ToList();");
            WriteLine("_interpreter = new EmbeddedPegInterpreter(grammar, tokenWrappers, this);");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateParserMethods()
        {
            // Generate helper methods
            GenerateHelperMethods();

            // Generate expression hierarchy at main parser level
            GenerateExpressionHierarchy();

            // Generate embedded types and interpreter
            GenerateEmbeddedTypes();

            // Use existing implementation for now
            foreach (var rule in _grammar.Rules)
            {
                GenerateRuleMethod(rule);
            }
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

            // Expect method
            WriteLine("private bool Expect(string expected)");
            WriteLine("{");
            Indent();
            WriteLine("if (CurrentToken?.Type.ToString() == expected)");
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

            // Expect keyword method
            WriteLine("private bool ExpectKeyword(string keyword)");
            WriteLine("{");
            Indent();
            WriteLine("if (CurrentToken?.Type.ToString() == \"NAME\" && CurrentToken?.Value == keyword)");
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

            // Mark/Reset for backtracking
            WriteLine("private int Mark() => _position;");
            WriteLine();
            WriteLine("private void Reset(int mark) => _position = mark;");
            WriteLine();

            // Memoization methods
            WriteLine("private T? GetMemo<T>(string ruleName)");
            WriteLine("{");
            Indent();
            WriteLine("var key = (_position, ruleName);");
            WriteLine("return _memoCache.TryGetValue(key, out var value) ? (T?)value : default;");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("private void SetMemo<T>(string ruleName, T? value)");
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
            WriteLine("private object? ExpectTokenType(string tokenType)");
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

            // Add TryParseGrammarItems method for pattern parsing
            WriteLine("private object? TryParseGrammarItems(string[] items)");
            WriteLine("{");
            Indent();
            WriteLine("// Parse sequence of grammar items");
            WriteLine("Console.WriteLine($\"[DEBUG] TryParseGrammarItems: {string.Join(\", \", items)}\");");
            WriteLine("var startPos = _position;");
            WriteLine("var results = new List<object?>();");
            WriteLine("var variables = new Dictionary<string, object?>();");
            WriteLine();
            WriteLine("foreach (var item in items)");
            WriteLine("{");
            Indent();
            WriteLine("var result = ParseGrammarItem(item, variables);");
            WriteLine("Console.WriteLine($\"[DEBUG]   Item '{item}' result: {(result != null ? \"SUCCESS\" : \"FAILED\")}\");");
            WriteLine("if (result == null)");
            WriteLine("{");
            Indent();
            WriteLine("_position = startPos; // Reset on failure");
            WriteLine("Console.WriteLine($\"[DEBUG] TryParseGrammarItems FAILED at item: {item}\");");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine("results.Add(result);");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Return appropriate result based on pattern");
            WriteLine("return CreateResultFromItems(items, results, variables);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add ParseGrammarItem helper method
            WriteLine("private object? ParseGrammarItem(string item, Dictionary<string, object?> variables)");
            WriteLine("{");
            Indent();
            WriteLine("// Handle variable assignments (e.g., \"a=statements\")");
            WriteLine("if (item.Contains('='))");
            WriteLine("{");
            Indent();
            WriteLine("var parts = item.Split('=', 2);");
            WriteLine("var varName = parts[0];");
            WriteLine("var pattern = parts[1];");
            WriteLine("var result = ParsePattern(pattern);");
            WriteLine("if (result != null) variables[varName] = result;");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Direct pattern matching");
            WriteLine("return ParsePattern(item);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add ParsePattern method
            WriteLine("private object? ParsePattern(string pattern)");
            WriteLine("{");
            Indent();
            WriteLine("// Remove optional markers");
            WriteLine("if (pattern.StartsWith(\"[\") && pattern.EndsWith(\"]\"))");
            WriteLine("{");
            Indent();
            WriteLine("var innerPattern = pattern.Substring(1, pattern.Length - 2);");
            WriteLine("var result = ParsePattern(innerPattern);");
            WriteLine("return result; // Optional always succeeds, may return null");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Handle grouping");
            WriteLine("if (pattern.StartsWith(\"(\") && pattern.EndsWith(\")\"))");
            WriteLine("{");
            Indent();
            WriteLine("var innerPattern = pattern.Substring(1, pattern.Length - 2);");
            WriteLine("return ParsePattern(innerPattern);");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Handle string literals (keywords/operators)");
            WriteLine("if (pattern.StartsWith('\"') && pattern.EndsWith('\"'))");
            WriteLine("{");
            Indent();
            WriteLine("var literal = pattern.Substring(1, pattern.Length - 2);");
            WriteLine("return ParseStringLiteral(literal);");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Handle quoted string literals");
            WriteLine("if (pattern.StartsWith(\"'\") && pattern.EndsWith(\"'\"))");
            WriteLine("{");
            Indent();
            WriteLine("var literal = pattern.Substring(1, pattern.Length - 2);");
            WriteLine("return ParseStringLiteral(literal);");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Handle token types");
            WriteLine("if (pattern == \"ENDMARKER\") return AtEndOfFile() ? \"ENDMARKER\" : null;");
            WriteLine("if (pattern == \"NAME\") return ExpectTokenType(\"NAME\");");
            WriteLine("if (pattern == \"NUMBER\") return ExpectTokenType(\"NUMBER\");");
            WriteLine("if (pattern == \"STRING\") return ExpectTokenType(\"STRING\");");
            WriteLine("if (pattern == \"NEWLINE\") return ExpectTokenType(\"NEWLINE\");");
            WriteLine();
            WriteLine("// Handle repetition patterns");
            WriteLine("if (pattern.EndsWith(\"+\"))");
            WriteLine("{");
            Indent();
            WriteLine("var basePattern = pattern.Substring(0, pattern.Length - 1);");
            WriteLine("var results = new List<object?>();");
            WriteLine("while (true)");
            WriteLine("{");
            Indent();
            WriteLine("var startPos = _position;");
            WriteLine("var result = ParsePattern(basePattern);");
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
            WriteLine("return results.Count > 0 ? results : null; // + requires at least one match");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("if (pattern.EndsWith(\"*\"))");
            WriteLine("{");
            Indent();
            WriteLine("var basePattern = pattern.Substring(0, pattern.Length - 1);");
            WriteLine("var results = new List<object?>();");
            WriteLine("while (true)");
            WriteLine("{");
            Indent();
            WriteLine("var startPos = _position;");
            WriteLine("var result = ParsePattern(basePattern);");
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
            WriteLine("return results; // * always succeeds, may return empty list");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Handle rule references");
            WriteLine("return ParseRuleReference(pattern);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add ParseStringLiteral helper
            WriteLine("private object? ParseStringLiteral(string literal)");
            WriteLine("{");
            Indent();
            WriteLine("// Handle operators");
            WriteLine("if (literal == \"=\") return ExpectOperator(\"=\") ? \"=\" : null;");
            WriteLine("if (literal == \"+\") return ExpectOperator(\"+\") ? \"+\" : null;");
            WriteLine("if (literal == \"-\") return ExpectOperator(\"-\") ? \"-\" : null;");
            WriteLine("if (literal == \"*\") return ExpectOperator(\"*\") ? \"*\" : null;");
            WriteLine("if (literal == \"/\") return ExpectOperator(\"/\") ? \"/\" : null;");
            WriteLine();
            WriteLine("// Handle keywords");
            WriteLine("if (ExpectKeyword(literal)) return literal;");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add ParseRuleReference method
            WriteLine("private object? ParseRuleReference(string ruleName)");
            WriteLine("{");
            Indent();
            WriteLine("// Map rule names to method calls");
            WriteLine("switch (ruleName)");
            WriteLine("{");
            Indent();
            WriteLine("case \"statements\": return Statements();");
            WriteLine("case \"statement\": return Statement();");
            WriteLine("case \"assignment\": return Assignment();");
            WriteLine("case \"star_targets\": return StarTargets();");
            WriteLine("case \"star_expressions\": return StarExpressions();");
            WriteLine("case \"expressions\": return Expressions();");
            WriteLine("case \"expression\": return Expression();");
            WriteLine("case \"primary\": return Primary();");
            WriteLine("case \"atom\": return Atom();");
            WriteLine("default: return null; // Rule not implemented");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Add CreateResultFromItems method
            WriteLine("private object? CreateResultFromItems(string[] items, List<object?> results, Dictionary<string, object?> variables)");
            WriteLine("{");
            Indent();
            WriteLine("// Create appropriate result based on the pattern context");
            WriteLine("if (items.Length == 2 && items[1] == \"ENDMARKER\")");
            WriteLine("{");
            Indent();
            WriteLine("// This is likely a file rule - create module");
            WriteLine("var statements = variables.ContainsKey(\"a\") ? variables[\"a\"] : null;");
            WriteLine("return new GeneratedModule { Body = statements as GeneratedStmtSeq };");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// For assignment patterns");
            WriteLine("if (variables.ContainsKey(\"a\") && variables.ContainsKey(\"b\"))");
            WriteLine("{");
            Indent();
            WriteLine("// Assignment: targets = expressions");
            WriteLine("return new GeneratedStmt { StatementType = \"Assign\", Value = new { targets = variables[\"a\"], value = variables[\"b\"] } };");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Return first non-null result or first variable");
            WriteLine("if (variables.Count > 0) return variables.Values.FirstOrDefault(v => v != null);");
            WriteLine("return results.FirstOrDefault(r => r != null);");
            Dedent();
            WriteLine("}");
            WriteLine();

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
                    WriteLine($"// Flatten statement sequences");
                    WriteLine($"foreach (var stmt in {varNames[0]})");
                    WriteLine($"{{");
                    WriteLine($"    result.Add(stmt);");
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
        private string TranslatePegTypeToCS(string pegType)
        {
            return pegType switch
            {
                "mod_ty" => "GeneratedModule?",
                "stmt_ty" => "GeneratedStmt?",
                "expr_ty" => "GeneratedExpr?",
                "asdl_stmt_seq*" => "GeneratedStmtSeq?",
                "asdl_expr_seq*" => "GeneratedExprSeq?",
                "asdl_identifier_seq*" => "GeneratedIdentifierSeq?",
                _ => "object?"
            };
        }

        private void GenerateRuleMethod(Rule rule)
        {
            var returnType = TranslateCTypeToCS(rule.ReturnType ?? "object?");
            var methodName = ToCSharpMethodName(rule.Name);

            WriteLine($"// Rule: {rule.Name}");
            WriteLine($"public {returnType} {methodName}()");
            WriteLine("{");
            Indent();

            // Use PegInterpreter for all rules - embedded in generated code
            WriteLine($"var result = _interpreter.ParseRule(\"{rule.Name}\");");
            // Fix nullable casting issue - use proper casting syntax
            if (returnType.EndsWith("?"))
            {
                var baseType = returnType.Substring(0, returnType.Length - 1);
                WriteLine($"return result as {baseType};");
            }
            else
            {
                WriteLine($"return ({returnType})result;");
            }

            Dedent();
            WriteLine("}");
            WriteLine();
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

        private void GenerateSimpleStmtMethod(string returnType)
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
            WriteLine("if (nameToken != null && nameToken.Type == GeneratedTokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume name");
            WriteLine("if (CurrentToken?.Type == GeneratedTokenType.EQUAL)");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '='");
            WriteLine("// For now, expect a NUMBER token for the value");
            WriteLine("var valueToken = CurrentToken;");
            WriteLine("if (valueToken?.Type == GeneratedTokenType.NUMBER)");
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
            WriteLine("if (CurrentToken?.Type == GeneratedTokenType.NUMBER)");
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
            WriteLine("if (CurrentToken?.Type == GeneratedTokenType.NUMBER)");
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
            WriteLine("if (CurrentToken?.Type == GeneratedTokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("names.Add(CurrentToken.Value);");
            WriteLine("Advance(); // consume first name");
            WriteLine("// Handle comma-separated additional names");
            WriteLine("while (CurrentToken?.Type == GeneratedTokenType.COMMA)");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume comma");
            WriteLine("if (CurrentToken?.Type == GeneratedTokenType.NAME)");
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
            WriteLine("if (CurrentToken?.Type == GeneratedTokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("names.Add(CurrentToken.Value);");
            WriteLine("Advance(); // consume first name");
            WriteLine("// Handle comma-separated additional names");
            WriteLine("while (CurrentToken?.Type == GeneratedTokenType.COMMA)");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume comma");
            WriteLine("if (CurrentToken?.Type == GeneratedTokenType.NAME)");
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
            WriteLine("if (CurrentToken?.Type == GeneratedTokenType.NAME)");
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
            WriteLine("if (CurrentToken?.Type == GeneratedTokenType.NAME)");
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
            WriteLine("if (CurrentToken?.Type == GeneratedTokenType.NAME)");
            WriteLine("{");
            Indent();
            WriteLine("var moduleName = CurrentToken.Value;");
            WriteLine("Advance(); // consume module name");
            WriteLine("if (ExpectKeyword(\"import\"))");
            WriteLine("{");
            Indent();
            WriteLine("if (CurrentToken?.Type == GeneratedTokenType.NAME)");
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
            WriteLine("// TODO: Add other simple statement alternatives");
            WriteLine("// - type_alias");
            WriteLine("// - star_expressions");
            WriteLine("// - raise_stmt, yield_stmt, assert_stmt");
            WriteLine("// - function calls, complex expressions");
            WriteLine();
            WriteLine("// No match found");
            WriteLine($"return default({returnType});");
        }

        private void GenerateAtomMethod(string returnType)
        {
            // atom[expr_ty]: NAME | 'True' | 'False' | 'None' | strings | NUMBER | ...
            WriteLine("// atom[expr_ty]: Basic expressions (NAME, NUMBER, True/False/None, STRING)");
            WriteLine();
            WriteLine("// Try NAME token (variable names)");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NAME\")");
            WriteLine("{");
            Indent();
            WriteLine("var nameValue = CurrentToken.Value;");
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
            WriteLine("var simpleStmts = SimpleStmts();");
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

        private void GenerateSimpleStmtsMethod(string returnType)
        {
            // simple_stmts[asdl_stmt_seq*]: a=simple_stmt b=newline { (asdl_stmt_seq*)_PyPegen_singleton_seq(p, a) }
            WriteLine("// simple_stmts[asdl_stmt_seq*]: simple_stmt NEWLINE | simple_stmt $$");
            WriteLine();
            WriteLine("var stmt = SimpleStmt();");
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

        private string ToCSharpMethodName(string grammarName)
        {
            // Convert grammar rule name to C# method name
            var parts = grammarName.Split('_');
            return string.Join("", parts.Select(p =>
                char.ToUpper(p[0]) + p.Substring(1).ToLower()));
        }

        private string TranslateCTypeToCS(string cType)
        {
            // Convert CPython C types to C# types - CPython 3.12 compatible
            return cType switch
            {
                "mod_ty" => "GeneratedModule", // Python module type
                "asdl_stmt_seq*" => "GeneratedStmtSeq", // Statement sequence
                "asdl_expr_seq*" => "GeneratedExprSeq", // Expression sequence
                "stmt_ty" => "GeneratedStmt", // Statement type
                "expr_ty" => "GeneratedExpr", // Expression type
                "asdl_seq*" => "GeneratedSeq", // Generic sequence
                "asdl_identifier_seq*" => "GeneratedIdentifierSeq", // Identifier sequence
                "string" => "string",
                "int" => "int",
                "void" => "object", // Changed: void cannot be used as generic type parameter in C#
                "PyObject*" => "GeneratedPyObject", // Python object
                "token*" => "GeneratedTokenInfo", // Token type
                _ when cType.EndsWith("_ty") => "GeneratedAstNode", // AST node types
                _ when cType.EndsWith("*") => "GeneratedSeq", // Generic pointer types
                _ => cType // Keep as-is for standard types
            };
        }


        // Output helper methods
        private void WriteLine(string line = "")
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

        private void Indent() => _indentLevel++;
        private void Dedent() => _indentLevel--;

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
            WriteLine("// TODO: Add other compound statements");
            WriteLine("// - with_stmt");
            WriteLine();
            WriteLine("// No match found");
            WriteLine($"return default({returnType});");
        }

        private void GenerateEmbeddedTypes()
        {
            WriteLine("// Embedded PEG Interpreter types and classes");
            WriteLine("// Note: GeneratedTokenType and GeneratedTokenInfo are defined in tokenizer");
            WriteLine();

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
            WriteLine("public object ParseRule(string ruleName)");
            WriteLine("{");
            Indent();
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
            WriteLine("return ParseSimpleStmt();");
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
            WriteLine("Console.WriteLine($\"[DEBUG] ParseFile starting: {_parser._position} / {_tokens.Count}\");");
            WriteLine("var statements = new List<object>();");
            WriteLine("while (_parser._position < _tokens.Count && _tokens[_parser._position].Type.ToString() != \"ENDMARKER\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseFile loop: position={_parser._position}, token={_tokens[_parser._position].Type}:{_tokens[_parser._position].Value}\");");
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
            WriteLine("Console.WriteLine($\"[DEBUG] ParseStmt returned: {(stmt != null ? stmt.GetType().Name : \"null\")}\");");
            WriteLine("if (stmt != null)");
            WriteLine("{");
            Indent();
            WriteLine("statements.Add(stmt);");
            WriteLine("// Ensure position advanced after successful parsing");
            WriteLine("if (_parser._position == initialPos)");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] Warning: ParseStmt succeeded but position not advanced, forcing advance\");");
            WriteLine("_parser._position++;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseStmt failed, advancing position to prevent infinite loop\");");
            WriteLine("_parser._position++; // Skip unknown token");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseFile collected {statements.Count} statements\");");
            WriteLine("var module = new GeneratedModule();");
            WriteLine("module.Body = new GeneratedStmtSeq();");
            WriteLine("foreach (var stmt in statements)");
            WriteLine("{");
            Indent();
            WriteLine("if (stmt is GeneratedStmt generatedStmt)");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] Adding statement: {generatedStmt.StatementType}\");");
            WriteLine("module.Body.Add(generatedStmt);");
            Dedent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] Skipping non-GeneratedStmt: {stmt?.GetType().Name}\");");
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
            WriteLine("var stmt = new GeneratedStmt();");
            WriteLine("stmt.StatementType = \"assignment\";");
            WriteLine("stmt.Value = new { Target = nameToken.Value, Value = expr };");
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
            WriteLine("private object ParseSimpleStmt()");
            WriteLine("{");
            Indent();
            WriteLine("// Try assignment first");
            WriteLine("var assignment = ParseAssignment();");
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
            WriteLine("return ParseSimpleStmt();");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Close the EmbeddedPegInterpreter class
            Dedent();
            WriteLine("}");
            WriteLine();

            // Generate CreateEmbeddedGrammar method for the main parser class
            GenerateEmbeddedGrammar();
        }

        /// <summary>
        /// Generates proper PEG expression hierarchy with left recursion support
        /// Following python.gram structure: atom → primary → ... → expression
        /// </summary>
        private void GenerateExpressionHierarchy()
        {
            WriteLine();
            WriteLine("// === PEG Expression Hierarchy with Left Recursion Support ===");
            WriteLine();

            // Generate atom parser (base level)
            GenerateAtomParser();

            // Generate primary parser with left recursion
            GeneratePrimaryParser();

            // Generate term parser (multiplication/division: *, /)
            GenerateTermParser();

            // Generate sum parser (arithmetic: +, -)
            GenerateSumParser();

            // Generate comparison parser (==, !=, <, >, <=, >=)
            GenerateComparisonParser();

            // Generate main expression parser
            GenerateExpressionParser();

            // Generate star_expressions parser (for statement expressions)
            GenerateStarExpressionsParser();

            // Generate simple expression statement parser
            GenerateExpressionStmtParser();

            // Generate if statement parser
            GenerateIfStatementParser();

            WriteLine("// === End Expression Hierarchy ===");
            WriteLine();
        }

        /// <summary>
        /// Generate atom parser: NAME | NUMBER | STRING | '(' expression ')'
        /// </summary>
        private void GenerateAtomParser()
        {
            WriteLine("// atom: NAME | NUMBER | STRING | '(' expression ')'");
            WriteLine("private object? ParseAtom()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseAtom at position {_position}: {CurrentToken?.Type}={CurrentToken?.Value}\");");
            WriteLine();
            WriteLine("if (CurrentToken == null) return null;");
            WriteLine();

            WriteLine("// NAME");
            WriteLine("if (CurrentToken.Type.ToString() == \"NAME\")");
            WriteLine("{");
            Indent();
            WriteLine("var name = CurrentToken.Value;");
            WriteLine("Advance();");
            WriteLine("return new { type = \"name\", value = name };");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// NUMBER");
            WriteLine("if (CurrentToken.Type.ToString() == \"NUMBER\")");
            WriteLine("{");
            Indent();
            WriteLine("var number = CurrentToken.Value;");
            WriteLine("Advance();");
            WriteLine("return new { type = \"number\", value = number };");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// STRING");
            WriteLine("if (CurrentToken.Type.ToString() == \"STRING\")");
            WriteLine("{");
            Indent();
            WriteLine("var str = CurrentToken.Value;");
            WriteLine("Advance();");
            WriteLine("return new { type = \"string\", value = str };");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("// '(' expression ')'");
            WriteLine("if (CurrentToken.Type.ToString() == \"OP\" && CurrentToken.Value == \"(\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '('");
            WriteLine("var expr = ParseExpression();");
            WriteLine("if (expr != null && CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \")\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume ')'");
            WriteLine("return expr;");
            Dedent();
            WriteLine("}");
            WriteLine("return null; // Malformed parenthesized expression");
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
            WriteLine("// primary: primary '.' NAME | primary '(' [arguments] ')' | atom");
            WriteLine("// Implemented with left recursion support");
            WriteLine("private object? ParsePrimary()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParsePrimary at position {_position}\");");
            WriteLine();

            WriteLine("// Check memoization for left recursion");
            WriteLine("var memoKey = \"primary\";");
            WriteLine("var memoResult = GetMemo<object>(memoKey);");
            WriteLine("if (memoResult != null)");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] Primary: Using memoized result\");");
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
            WriteLine("var arg = ParseExpression();");
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
            WriteLine("result = new { type = \"call\", func = result, args = args };");
            WriteLine("expanded = true;");
            WriteLine("Console.WriteLine($\"[DEBUG] Primary: Created function call\");");
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
            WriteLine("result = new { type = \"attribute\", value = result, attr = attr };");
            WriteLine("expanded = true;");
            WriteLine("Console.WriteLine($\"[DEBUG] Primary: Created attribute access\");");
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
            WriteLine("Console.WriteLine($\"[DEBUG] Primary result: {result?.GetType().Name}\");");
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
            WriteLine("private object? ParseStarExpressions()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseStarExpressions at position {_position}\");");
            WriteLine();
            WriteLine("var expr = ParseExpression();");
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
            WriteLine("private object? ParseComparison()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseComparison at position {_position}\");");
            WriteLine();

            // Start with sum (base case)
            WriteLine("var result = ParseSum();");
            WriteLine("if (result == null) return null;");
            WriteLine();

            // Handle comparison operators (non-associative, single comparison only)
            WriteLine("// Handle single comparison operation");
            WriteLine("var mark = Mark();");
            WriteLine();

            // Check for comparison operators
            WriteLine("string? op = null;");
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\")");
            WriteLine("{");
            Indent();
            WriteLine("switch (CurrentToken?.Value)");
            WriteLine("{");
            Indent();
            WriteLine("case \"==\": op = \"==\"; break;");
            WriteLine("case \"!=\": op = \"!=\"; break;");
            WriteLine("case \"<\": op = \"<\"; break;");
            WriteLine("case \"<=\": op = \"<=\"; break;");
            WriteLine("case \">\": op = \">\"; break;");
            WriteLine("case \">=\": op = \">=\"; break;");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("if (op != null)");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume operator");
            WriteLine("var right = ParseSum();");
            WriteLine("if (right != null)");
            WriteLine("{");
            Indent();
            WriteLine("result = new { type = \"compare\", op = op, left = result, right = right };");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseComparison: Created {op} comparison\");");
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

            WriteLine("Console.WriteLine($\"[DEBUG] ParseComparison result: {result?.GetType().Name}\");");
            WriteLine("return result;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate expression parser that delegates to comparison for operations
        /// </summary>
        private void GenerateExpressionParser()
        {
            WriteLine("// expression: comparison (operations with proper precedence)");
            WriteLine("private object? ParseExpression()");
            WriteLine("{");
            Indent();
            WriteLine("return ParseComparison();");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate term parser: term '*' primary | term '/' primary | primary
        /// Handles multiplication and division with left recursion
        /// </summary>
        private void GenerateTermParser()
        {
            WriteLine("// term: term '*' primary | term '/' primary | primary");
            WriteLine("private object? ParseTerm()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseTerm at position {_position}\");");
            WriteLine();

            // Start with primary (base case)
            WriteLine("var result = ParsePrimary();");
            WriteLine("if (result == null) return null;");
            WriteLine();

            // Iteratively handle left recursion for * and /
            WriteLine("// Handle left recursion iteratively");
            WriteLine("while (true)");
            WriteLine("{");
            Indent();
            WriteLine("var mark = Mark();");
            WriteLine();

            // Try: term '*' primary
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"*\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '*'");
            WriteLine("var right = ParsePrimary();");
            WriteLine("if (right != null)");
            WriteLine("{");
            Indent();
            WriteLine("result = new { type = \"binop\", op = \"*\", left = result, right = right };");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseTerm: Created multiplication\");");
            WriteLine("continue;");
            Dedent();
            WriteLine("}");
            WriteLine("Reset(mark);");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Try: term '/' primary
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \"/\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume '/'");
            WriteLine("var right = ParsePrimary();");
            WriteLine("if (right != null)");
            WriteLine("{");
            Indent();
            WriteLine("result = new { type = \"binop\", op = \"/\", left = result, right = right };");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseTerm: Created division\");");
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

            WriteLine("Console.WriteLine($\"[DEBUG] ParseTerm result: {result?.GetType().Name}\");");
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
            WriteLine("private object? ParseSum()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseSum at position {_position}\");");
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
            WriteLine("result = new { type = \"binop\", op = \"+\", left = result, right = right };");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseSum: Created addition\");");
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
            WriteLine("result = new { type = \"binop\", op = \"-\", left = result, right = right };");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseSum: Created subtraction\");");
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

            WriteLine("Console.WriteLine($\"[DEBUG] ParseSum result: {result?.GetType().Name}\");");
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
            WriteLine("public object? ParseExpressionStmt()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseExpressionStmt at position {_position}\");");
            WriteLine();
            WriteLine("var expr = ParseStarExpressions();");
            WriteLine("if (expr != null)");
            WriteLine("{");
            Indent();
            WriteLine("var stmt = new GeneratedStmt();");
            WriteLine("stmt.StatementType = \"expression\";");
            WriteLine("stmt.Value = expr;");
            WriteLine("Console.WriteLine($\"[DEBUG] Created expression statement: {expr}\");");
            WriteLine("return stmt;");
            Dedent();
            WriteLine("}");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        /// <summary>
        /// Generate if statement parser: if expression : block (elif expression : block)* (else : block)?
        /// </summary>
        private void GenerateIfStatementParser()
        {
            WriteLine("// if_stmt: 'if' expression ':' block ('elif' expression ':' block)* ['else' ':' block]");
            WriteLine("public object? ParseIfStatement()");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement at position {_position}\");");
            WriteLine();

            // Check for 'if' keyword
            WriteLine("if (CurrentToken?.Type.ToString() != \"NAME\" || CurrentToken?.Value != \"if\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: Not an if token at position {_position}\");");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: Found 'if' token, advancing\");");
            WriteLine("Advance(); // consume 'if'");
            WriteLine();

            // Parse condition
            WriteLine("// Parse condition expression");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: Parsing condition at position {_position}\");");
            WriteLine("var condition = ParseExpression();");
            WriteLine("if (condition == null)");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: Missing condition\");");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: Condition parsed successfully\");");
            WriteLine();

            // Parse ':'
            WriteLine("// Parse ':'");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: Looking for ':' at position {_position}\");");
            WriteLine("if (CurrentToken?.Type.ToString() != \"OP\" || CurrentToken?.Value != \":\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: Missing ':' after condition\");");
            WriteLine("return null;");
            Dedent();
            WriteLine("}");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: Found ':', advancing\");");
            WriteLine("Advance(); // consume ':'");
            WriteLine();

            // Parse if body
            WriteLine("// Parse if body");
            WriteLine("// Skip NEWLINE and INDENT if present");
            WriteLine("while (CurrentToken != null && (CurrentToken.Type.ToString() == \"NEWLINE\" || CurrentToken.Type.ToString() == \"INDENT\"))");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine();

            WriteLine("var ifBody = new List<object>();");
            WriteLine("// Parse if body - simplified to single statement");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: About to parse if body at position {_position}\");");
            WriteLine("var bodyStmt = ParseExpressionStmt();");
            WriteLine("if (bodyStmt != null)");
            WriteLine("{");
            Indent();
            WriteLine("ifBody.Add(bodyStmt);");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: If body parsed successfully\");");
            Dedent();
            WriteLine("}");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: If body parsing complete, at position {_position}\");");
            WriteLine();

            // Skip DEDENT if present
            WriteLine("// Skip DEDENT if present");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: Checking for DEDENT at position {_position}, token: {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("if (CurrentToken?.Type.ToString() == \"DEDENT\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: Found DEDENT, advancing\");");
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: Post-DEDENT position {_position}, token: {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine();

            // Parse elif clauses
            WriteLine("// Parse elif clauses");
            WriteLine("// Skip any remaining NEWLINE/INDENT/DEDENT tokens after if body");
            WriteLine("while (CurrentToken != null && (CurrentToken.Type.ToString() == \"NEWLINE\" || CurrentToken.Type.ToString() == \"INDENT\" || CurrentToken.Type.ToString() == \"DEDENT\"))");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine("Console.WriteLine($\"[DEBUG] Looking for elif at position {_position}, current token: {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("var elifs = new List<object>();");
            WriteLine("while (CurrentToken?.Type.ToString() == \"NAME\" && CurrentToken?.Value == \"elif\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] Found elif at position {_position}\");");
            Dedent();
            Indent();
            WriteLine("Advance(); // consume 'elif'");
            WriteLine();
            WriteLine("var elifCondition = ParseExpression();");
            WriteLine("if (elifCondition == null) break;");
            WriteLine();
            WriteLine("if (CurrentToken?.Type.ToString() != \"OP\" || CurrentToken?.Value != \":\")");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement: Missing ':' after elif condition\");");
            WriteLine("break;");
            Dedent();
            WriteLine("}");
            WriteLine("Advance(); // consume ':'");
            WriteLine();
            WriteLine("// Skip NEWLINE and INDENT");
            WriteLine("while (CurrentToken != null && (CurrentToken.Type.ToString() == \"NEWLINE\" || CurrentToken.Type.ToString() == \"INDENT\"))");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("var elifBody = new List<object>();");
            WriteLine("var elifStmt = ParseExpressionStmt();");
            WriteLine("if (elifStmt != null)");
            WriteLine("{");
            Indent();
            WriteLine("elifBody.Add(elifStmt);");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Skip DEDENT and any following NEWLINE/DEDENT tokens before next elif");
            WriteLine("while (CurrentToken != null && (CurrentToken.Type.ToString() == \"DEDENT\" || CurrentToken.Type.ToString() == \"NEWLINE\"))");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("elifs.Add(new { condition = elifCondition, body = elifBody });");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Parse else clause if present
            WriteLine("// Parse optional else clause");
            WriteLine("// Skip any remaining NEWLINE/INDENT/DEDENT tokens before else");
            WriteLine("while (CurrentToken != null && (CurrentToken.Type.ToString() == \"NEWLINE\" || CurrentToken.Type.ToString() == \"INDENT\" || CurrentToken.Type.ToString() == \"DEDENT\"))");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine("Console.WriteLine($\"[DEBUG] Looking for else at position {_position}, current token: {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("List<object>? elseBody = null;");
            WriteLine("if (CurrentToken?.Type.ToString() == \"NAME\" && CurrentToken?.Value == \"else\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume 'else'");
            WriteLine();
            WriteLine("// Parse ':'");
            WriteLine("if (CurrentToken?.Type.ToString() == \"OP\" && CurrentToken?.Value == \":\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance(); // consume ':'");
            WriteLine();
            WriteLine("// Skip NEWLINE and INDENT if present");
            WriteLine("while (CurrentToken != null && (CurrentToken.Type.ToString() == \"NEWLINE\" || CurrentToken.Type.ToString() == \"INDENT\"))");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("elseBody = new List<object>();");
            WriteLine("var elseStmt = ParseExpressionStmt();");
            WriteLine("if (elseStmt != null)");
            WriteLine("{");
            Indent();
            WriteLine("elseBody.Add(elseStmt);");
            Dedent();
            WriteLine("}");
            WriteLine();
            WriteLine("// Skip DEDENT");
            WriteLine("if (CurrentToken?.Type.ToString() == \"DEDENT\")");
            WriteLine("{");
            Indent();
            WriteLine("Advance();");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            Dedent();
            WriteLine("}");
            WriteLine();

            // Create complete if statement
            WriteLine("var ifStmt = new GeneratedStmt();");
            WriteLine("ifStmt.StatementType = \"if\";");
            WriteLine("ifStmt.Value = new { condition = condition, body = ifBody, elifs = elifs, elseBody = elseBody };");
            WriteLine("Console.WriteLine($\"[DEBUG] Created complete if statement with {elifs.Count} elif clauses, hasElse: {elseBody != null}\");");
            WriteLine("Console.WriteLine($\"[DEBUG] ParseIfStatement finished at position {_position}, current token: {CurrentToken?.Type}:{CurrentToken?.Value}\");");
            WriteLine("return ifStmt;");

            Dedent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateEmbeddedGrammar()
        {
            WriteLine("private EmbeddedGrammar CreateEmbeddedGrammar()");
            WriteLine("{");
            Indent();
            WriteLine("return new EmbeddedGrammar();");
            Dedent();
            WriteLine("}");
        }
    }
}