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
            WriteLine();
            WriteLine("using System;");
            WriteLine("using System.Collections.Generic;");
            WriteLine("using System.Linq;");
            WriteLine("using SharpPy;");
            WriteLine("using SharpPy.PegGenerator.Grammar;");
            WriteLine("using SharpPy.PegGenerator.Interpreter;");
            WriteLine("using SharpPy.Tokenizer.Generated;");
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
            WriteLine("/// Uses PegInterpreter for dynamic rule execution");
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
            WriteLine("private int _position;");
            WriteLine("private readonly Dictionary<(int, string), object?> _memoCache = new();");
            WriteLine("private readonly string _filename;");
            WriteLine("private readonly PegInterpreter _interpreter;");
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
            WriteLine("// Initialize PEG interpreter with grammar");
            WriteLine("var grammarLoader = new GrammarLoader();");
            WriteLine("var grammar = grammarLoader.LoadGrammar(\"Grammar/python.gram\");");
            WriteLine("var tokenWrappers = tokens.Select(t => (SharpPy.PegGenerator.Interpreter.ITokenInfo)new SharpPy.PegGenerator.Interpreter.PegTokenInfoAdapter(t)).ToList();");
            WriteLine("_interpreter = new PegInterpreter(grammar, tokenWrappers);");
            Dedent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateParserMethods()
        {
            // Generate helper methods
            GenerateHelperMethods();

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

            // Use PegInterpreter for all rules now that left-recursion is handled
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
    }
}