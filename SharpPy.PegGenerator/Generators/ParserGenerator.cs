using System.Text;
using SharpPy.PegGenerator.DataStructures;

namespace SharpPy.PegGenerator.Generators;

/// <summary>
/// python_py.gram → PyParser.cs 생성기
/// CPython 3.12 PEG parser generation
/// </summary>
public class ParserGenerator
{
    private readonly StringBuilder _sb = new();
    private int _indentLevel = 0;
    private HashSet<string> _hardKeywords = new();
    private HashSet<string> _softKeywords = new();

    public string Generate(PegRule[] rules, HashSet<string> hardKeywords, HashSet<string> softKeywords, TrailerCode? trailer = null)
    {
        _sb.Clear();
        _indentLevel = 0;
        _hardKeywords = hardKeywords;
        _softKeywords = softKeywords;

        // File header
        WriteLine("// Generated Parser from python_py.gram");
        WriteLine("// CPython 3.12 compatible - Auto-generated, DO NOT EDIT");
        WriteLine();
        WriteLine("using System;");
        WriteLine("using System.Collections.Generic;");
        WriteLine("using SharpPy.Generated;");
        WriteLine();
        WriteLine("namespace SharpPy.Generated");
        WriteLine("{");
        _indentLevel++;

        // Parser class
        WriteLine("/// <summary>");
        WriteLine("/// PEG Parser for Python 3.12");
        WriteLine("/// Generated from python_py.gram");
        WriteLine("/// </summary>");
        WriteLine("public partial class PyParser : PyParserBase<GeneratedPtr>");
        WriteLine("{");
        _indentLevel++;

        // Fields and constructor
        WriteLine("// ============================================================");
        WriteLine("// Parser State");
        WriteLine("// ============================================================");
        WriteLine();
        WriteLine("// Note: This is a partial class - other parts are in PyParserBase.cs");
        WriteLine("// Token list and position tracking");
        WriteLine();

        // Constructor
        WriteLine("/// <summary>");
        WriteLine("/// Constructor - CPython: _PyPegen_Parser_New");
        WriteLine("/// </summary>");
        WriteLine("public PyParser(List<GeneratedTokenInfo> tokens, string filename)");
        WriteLine("    : base(tokens, filename)");
        WriteLine("{");
        WriteLine("}");
        WriteLine();

        WriteLine("/// <summary>");
        WriteLine("/// Constructor with source code for error reporting");
        WriteLine("/// </summary>");
        WriteLine("public PyParser(List<GeneratedTokenInfo> tokens, string filename, string source)");
        WriteLine("    : base(tokens, filename, source)");
        WriteLine("{");
        WriteLine("}");
        WriteLine();

        // Parse() method implementation
        WriteLine("// ============================================================");
        WriteLine("// Entry Point");
        WriteLine("// ============================================================");
        WriteLine();
        WriteLine("/// <summary>");
        WriteLine("/// Main entry point for parsing - CPython: _PyPegen_run_parser");
        WriteLine("/// </summary>");
        WriteLine("public override GeneratedPtr Parse()");
        WriteLine("{");
        _indentLevel++;
        WriteLine("// Entry point: parse file (CPython: start rule)");
        WriteLine("var result = Parse_File();");
        WriteLine("if (result == null)");
        WriteLine("{");
        _indentLevel++;
        WriteLine("throw new Exception(\"Parse failed at top level\");");
        _indentLevel--;
        WriteLine("}");
        WriteLine("return result;");
        _indentLevel--;
        WriteLine("}");
        WriteLine();

        // Generate rule methods
        WriteLine("// ============================================================");
        WriteLine("// Grammar Rules (193 rules from python_py.gram)");
        WriteLine("// ============================================================");
        WriteLine();

        foreach (var rule in rules)
        {
            GenerateRuleMethod(rule);
        }

        // GetKeywordOrNameType implementation (abstract method from PyParserBase)
        GenerateGetKeywordOrNameType();

        // Invalid rules (for error reporting)
        GenerateInvalidRules();

        // Helper methods
        GenerateHelperMethods();

        // @trailer code (if present)
        if (trailer != null)
        {
            GenerateTrailerCode(trailer);
        }

        _indentLevel--;
        WriteLine("}");

        _indentLevel--;
        WriteLine("}");

        return _sb.ToString();
    }

    private void GenerateRuleMethod(PegRule rule)
    {
        var methodName = ToPascalCase(rule.Name);

        WriteLine($"/// <summary>");
        WriteLine($"/// Rule: {rule.Name}");
        WriteLine($"/// Alternatives: {rule.Alternatives.Count}");
        WriteLine($"/// </summary>");
        WriteLine($"private GeneratedPtr? Parse_{methodName}()");
        WriteLine("{");
        _indentLevel++;

        WriteLine("int _mark = Mark();");
        WriteLine();

        // Generate alternatives
        for (int i = 0; i < rule.Alternatives.Count; i++)
        {
            var alt = rule.Alternatives[i];

            if (i > 0)
            {
                WriteLine();
                WriteLine("// Alternative " + (i + 1));
            }

            WriteLine("Reset(_mark);");
            WriteLine("{");
            _indentLevel++;

            GenerateAlternative(alt, rule.Name);

            _indentLevel--;
            WriteLine("}");
        }

        WriteLine();
        WriteLine("Reset(_mark);");
        WriteLine("return null;");

        _indentLevel--;
        WriteLine("}");
        WriteLine();
    }

    private void GenerateAlternative(Alternative alt, string ruleName)
    {
        // CPython 3.12: Capture start position for EXTRA macro (AST location info)
        WriteLine("CaptureStart();");
        WriteLine();

        // Generate temporary variables for each named item
        var namedItems = alt.Items.Where(i => !string.IsNullOrEmpty(i.Name)).ToList();

        foreach (var item in namedItems)
        {
            WriteLine($"GeneratedPtr? {item.Name} = null;");
        }

        // Generate parsing code for each item
        WriteLine();
        for (int i = 0; i < alt.Items.Count; i++)
        {
            var item = alt.Items[i];

            if (!string.IsNullOrEmpty(item.Name))
            {
                WriteLine($"if (({item.Name} = {GenerateAtomCode(item.Atom)}) == null) return null;");
            }
            else
            {
                WriteLine($"if ({GenerateAtomCode(item.Atom)} == null) return null;");
            }
        }

        // Generate return statement
        WriteLine();
        if (!string.IsNullOrEmpty(alt.ActionCode))
        {
            // User-defined action code from python_cs.gram
            WriteLine($"// Action code from grammar");
            WriteLine($"return {alt.ActionCode};");
        }
        else
        {
            // Placeholder for rules without action code (python_py.gram)
            WriteLine($"// TODO: Return appropriate AST node for {ruleName}");
            WriteLine("return GeneratedPlaceholder.Instance; // Placeholder");
        }
    }

    private string GenerateAtomCode(Atom atom)
    {
        return atom switch
        {
            Keyword kw => GenerateKeywordCode(kw),
            Token token => $"Expect(PyToken.Type.{token.TokenType}, \"{token.TokenType}\")",
            RuleRef ruleRef => $"Parse_{ToPascalCase(ruleRef.Name)}()",
            Group group => GenerateGroupCode(group),
            Optional opt => GenerateOptionalCode(opt),
            ZeroOrMore zm => GenerateZeroOrMoreCode(zm),
            OneOrMore om => GenerateOneOrMoreCode(om),
            PositiveLookahead pl => GeneratePositiveLookaheadCode(pl),
            NegativeLookahead nl => GenerateNegativeLookaheadCode(nl),
            Gather gather => GenerateGatherCode(gather),
            _ => throw new Exception($"Unknown atom type: {atom.GetType().Name}")
        };
    }

    private string GenerateKeywordCode(Keyword kw)
    {
        if (kw.IsSoft)
        {
            // Soft keyword: check as NAME token with specific value
            return $"ExpectSoftKeyword(\"{kw.Value}\")";
        }
        else
        {
            // Hard keyword or operator
            if (_hardKeywords.Contains(kw.Value))
            {
                return $"ExpectKeyword(\"{kw.Value}\")";
            }
            else
            {
                // Operator like ',', ';', '(', etc.
                return $"ExpectOp(\"{EscapeString(kw.Value)}\")";
            }
        }
    }

    private string GenerateGroupCode(Group group)
    {
        // Group: (alt1 | alt2 | ...)
        // For now, generate inline code
        return "ParseGroup()"; // TODO: Generate inline group parsing
    }

    private string GenerateOptionalCode(Optional opt)
    {
        // Optional: [item] or item?
        return $"ParseOptional(() => {GenerateAtomCode(opt.Inner)})";
    }

    private string GenerateZeroOrMoreCode(ZeroOrMore zm)
    {
        // ZeroOrMore: item*
        return $"ParseZeroOrMore(() => {GenerateAtomCode(zm.Inner)})";
    }

    private string GenerateOneOrMoreCode(OneOrMore om)
    {
        // OneOrMore: item+
        return $"ParseOneOrMore(() => {GenerateAtomCode(om.Inner)})";
    }

    private string GeneratePositiveLookaheadCode(PositiveLookahead pl)
    {
        // Positive lookahead: &item
        return $"PositiveLookahead(() => {GenerateAtomCode(pl.Inner)})";
    }

    private string GenerateNegativeLookaheadCode(NegativeLookahead nl)
    {
        // Negative lookahead: !item
        return $"NegativeLookahead(() => {GenerateAtomCode(nl.Inner)})";
    }

    private string GenerateGatherCode(Gather gather)
    {
        // Gather: sep.item+ or sep.item*
        var sepCode = GenerateAtomCode(gather.Separator);
        var itemCode = GenerateAtomCode(gather.Item);

        if (gather.IsPlus)
        {
            return $"ParseGatherPlus(() => {sepCode}, () => {itemCode})";
        }
        else
        {
            return $"ParseGatherStar(() => {sepCode}, () => {itemCode})";
        }
    }

    private void GenerateGetKeywordOrNameType()
    {
        WriteLine("// ============================================================");
        WriteLine("// GetKeywordOrNameType - Abstract method implementation");
        WriteLine("// ============================================================");
        WriteLine();
        WriteLine("/// <summary>");
        WriteLine("/// Returns PyToken.Type for keyword or NAME");
        WriteLine("/// CPython: Parser/pegen.c - _PyPegen_keyword_or_name_type");
        WriteLine("/// CPython 3.12: All keywords are tokenized as NAME, this method distinguishes them");
        WriteLine("/// </summary>");
        WriteLine("protected override int GetKeywordOrNameType(string name, int nameLen)");
        WriteLine("{");
        _indentLevel++;

        WriteLine("// CPython 3.12: Keywords are parsed as NAME tokens");
        WriteLine("// The parser uses string comparison to identify keywords");
        WriteLine("// All keywords and names return NAME token type");
        WriteLine("return (int)PyToken.Type.NAME;");

        _indentLevel--;
        WriteLine("}");
        WriteLine();
    }

    private void GenerateInvalidRules()
    {
        WriteLine("// ============================================================");
        WriteLine("// Invalid Rules (CPython 3.12: Better error messages)");
        WriteLine("// ============================================================");
        WriteLine();
        WriteLine("// CPython 3.12: invalid_* rules are designed to always fail");
        WriteLine("// They provide better error messages for common syntax errors");
        WriteLine();
        WriteLine("private GeneratedPtr? Parse_InvalidDefault()");
        WriteLine("{");
        _indentLevel++;
        WriteLine("// This rule is designed to fail and produce a better error message");
        WriteLine("return null;");
        _indentLevel--;
        WriteLine("}");
        WriteLine();
    }

    private void GenerateHelperMethods()
    {
        WriteLine("// ============================================================");
        WriteLine("// Helper Methods (inherited from PyParserBase)");
        WriteLine("// ============================================================");
        WriteLine();
        WriteLine("// Note: All helper methods (Mark, Reset, Expect*, ParseOptional,");
        WriteLine("// ParseZeroOrMore, ParseOneOrMore, etc.) are inherited from PyParserBase");
        WriteLine();

        // IsKeyword static method for backward compatibility
        WriteLine("/// <summary>");
        WriteLine("/// Check if a string is a Python keyword - for backward compatibility");
        WriteLine("/// </summary>");
        WriteLine("public static bool IsKeyword(string name)");
        WriteLine("{");
        _indentLevel++;
        WriteLine("return _hardKeywords.Contains(name);");
        _indentLevel--;
        WriteLine("}");
        WriteLine();

        WriteLine("// Hard keywords set for IsKeyword check");
        WriteLine("private static readonly HashSet<string> _hardKeywords = new()");
        WriteLine("{");
        _indentLevel++;
        foreach (var kw in _hardKeywords.OrderBy(k => k))
        {
            WriteLine($"\"{kw}\",");
        }
        _indentLevel--;
        WriteLine("};");
        WriteLine();
    }

    private void GenerateTrailerCode(TrailerCode trailer)
    {
        WriteLine("// ============================================================");
        WriteLine("// @trailer code (Entry Points and Custom Methods)");
        WriteLine("// ============================================================");
        WriteLine();
        WriteLine("// CPython 3.12: @trailer block contains entry point methods");
        WriteLine("// that map to grammar rules (file, interactive, eval, etc.)");
        WriteLine();

        // Split trailer code into lines and output with proper indentation
        var lines = trailer.Code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                WriteLine();
            }
            else
            {
                // Preserve relative indentation from the trailer code
                WriteLine(line.TrimStart());
            }
        }

        WriteLine();
    }

    private string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var parts = name.Split('_');
        return string.Join("", parts.Select(p =>
            p.Length > 0 ? char.ToUpper(p[0]) + p.Substring(1) : ""
        ));
    }

    private void WriteLine(string line = "")
    {
        if (string.IsNullOrEmpty(line))
        {
            _sb.AppendLine();
        }
        else
        {
            _sb.AppendLine(new string(' ', _indentLevel * 4) + line);
        }
    }
}
