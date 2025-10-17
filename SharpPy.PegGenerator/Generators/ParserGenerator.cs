using System.Text;
using SharpPy.PegGenerator.DataStructures;

namespace SharpPy.PegGenerator.Generators;

/// <summary>
/// Represents information about an Atom's function call
/// Mirrors CPython's FunctionCall class in c_generator.py
/// CPython 3.12: Tools/peg_generator/pegen/c_generator.py (line 83-116)
/// </summary>
internal class AtomCallInfo
{
    public string Function { get; set; } = "";
    public string? AssignedVariable { get; set; }
    public string? AssignedVariableType { get; set; }  // Grammar type annotation
    public string? ReturnType { get; set; }            // Actual return type from rule/atom

    /// <summary>
    /// Generate C# code for this call
    /// CPython: FunctionCall.__str__() (c_generator.py line 93-116)
    /// </summary>
    public string GenerateCode()
    {
        if (AssignedVariable != null)
        {
            // CPython: if self.assigned_variable_type: parts = ["(", assigned_variable, " = ", "(", assigned_variable_type, ")", ...]
            if (AssignedVariableType != null)
            {
                // Type annotation exists
                if (AssignedVariableType == ReturnType)
                {
                    // Type matches → no cast needed
                    // (c = Parse_ImportFromTargets())
                    return $"({AssignedVariable} = {Function})";
                }
                else
                {
                    // Type differs → need cast
                    // (c = (GeneratedAliasSeq)Parse_ImportFromTargets())
                    // (a = (GeneratedTokenInfo)ParseOptional(...))  // ParseOptional returns GeneratedPtr
                    return $"({AssignedVariable} = ({AssignedVariableType}){Function})";
                }
            }
            else
            {
                // No type annotation → no cast
                return $"({AssignedVariable} = {Function})";
            }
        }

        // No assignment, just function call
        return Function;
    }
}

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
    private Dictionary<string, string> _ruleReturnTypes = new();  // rule_name → return_type

    public string Generate(PegRule[] rules, HashSet<string> hardKeywords, HashSet<string> softKeywords, TrailerCode? trailer = null)
    {
        _sb.Clear();
        _indentLevel = 0;
        _hardKeywords = hardKeywords;
        _softKeywords = softKeywords;
        _ruleReturnTypes.Clear();

        // Build rule return type mapping (rule_name → return_type)
        foreach (var rule in rules)
        {
            var returnType = "GeneratedPtr";  // default

            if (!string.IsNullOrEmpty(rule.ReturnType))
            {
                returnType = rule.ReturnType;
            }

            _ruleReturnTypes[rule.Name] = returnType;
        }

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
        // NOTE: Invalid rules are now generated from python_cs.gram (244 rules include ~50 invalid_* rules)
        // No need to generate placeholder invalid_default rule anymore

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

    /// <summary>
    /// Get return type for Atom pattern - mirrors CPython's callmakervisitor.generate_call()
    /// CPython: visit_Repeat1() → FunctionCall(return_type="asdl_seq *")
    /// SharpPy: OneOrMore → "GeneratedSeq"
    /// </summary>
    private string GetAtomReturnType(Atom atom)
    {
        return atom switch
        {
            OneOrMore _ => "GeneratedSeq",       // CPython: asdl_seq * (for +)
            ZeroOrMore _ => "GeneratedSeq",      // CPython: asdl_seq * (for *)
            Gather _ => "GeneratedSeq",          // CPython: asdl_seq * (for sep.item+)
            Optional opt => GetAtomReturnType(opt.Inner),  // Recurse to inner type
            Group _ => "GeneratedPtr",           // Group returns one of alternatives
            RuleRef ruleRef => _ruleReturnTypes.GetValueOrDefault(ruleRef.Name, "GeneratedPtr"),  // Lookup actual rule return type
            Keyword _ => "GeneratedTokenInfo",   // Keyword returns token
            Token _ => "GeneratedTokenInfo",     // Token returns token
            PositiveLookahead _ => "GeneratedPtr",  // Lookahead doesn't consume
            NegativeLookahead _ => "GeneratedPtr",  // Lookahead doesn't consume
            _ => "GeneratedPtr"                  // Default fallback
        };
    }

    private void GenerateRuleMethod(PegRule rule)
    {
        var methodName = ToPascalCase(rule.Name);

        // Use rule's return type from grammar mapping
        var returnType = _ruleReturnTypes.GetValueOrDefault(rule.Name, "GeneratedPtr");

        WriteLine($"/// <summary>");
        WriteLine($"/// Rule: {rule.Name}");
        WriteLine($"/// Alternatives: {rule.Alternatives.Count}");
        if (!string.IsNullOrEmpty(rule.ReturnType))
        {
            WriteLine($"/// Return Type: {rule.ReturnType}");
        }
        WriteLine($"/// </summary>");
        WriteLine($"private {returnType}? Parse_{methodName}()");
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
            // CPython: return_type = call.return_type if node.type is None else node.type
            // Use type annotation if provided, otherwise infer from Atom pattern
            var varType = !string.IsNullOrEmpty(item.Type)
                ? item.Type
                : GetAtomReturnType(item.Atom);
            WriteLine($"{varType}? {item.Name} = null;");
        }

        // Generate parsing code for each item
        // CPython: CParserGenerator.visit_NamedItem() (c_generator.py line 690-694)
        WriteLine();
        for (int i = 0; i < alt.Items.Count; i++)
        {
            var item = alt.Items[i];

            if (!string.IsNullOrEmpty(item.Name))
            {
                // CPython visit_NamedItem pattern:
                // 1. call = self.callmakervisitor.generate_call(node)
                var callInfo = GenerateAtomCallInfo(item.Atom);

                // 2. if node.name: call.assigned_variable = node.name
                callInfo.AssignedVariable = item.Name;

                // 3. if node.type: call.assigned_variable_type = node.type
                if (!string.IsNullOrEmpty(item.Type))
                {
                    callInfo.AssignedVariableType = item.Type;
                }
                else
                {
                    // No type annotation → use same type as variable declaration
                    // IMPORTANT: Must match GetAtomReturnType() used in variable declaration!
                    // Example: Optional<TokenInfo> → variable: GeneratedTokenInfo?, assign: (GeneratedTokenInfo)ParseOptional(...)
                    callInfo.AssignedVariableType = GetAtomReturnType(item.Atom);
                }

                // 4. Generate code using FunctionCall.__str__() pattern
                var code = callInfo.GenerateCode();
                WriteLine($"if ({code} == null) return null;");
            }
            else
            {
                // Unnamed item: just call the function
                WriteLine($"if ({GenerateAtomCode(item.Atom)} == null) return null;");
            }
        }

        // Generate return statement
        WriteLine();
        if (!string.IsNullOrEmpty(alt.ActionCode))
        {
            // User-defined action code from python_cs.gram
            WriteLine($"// Action code from grammar");

            // CPython 3.12: Expand EXTRA macro
            // C: #define EXTRA _start_lineno, _start_col_offset, _end_lineno, _end_col_offset, p->arena
            // C#: Replace EXTRA with four comma-separated arguments
            var expandedCode = alt.ActionCode.Replace("EXTRA", "_start_lineno, _start_col_offset, _end_lineno, _end_col_offset");

            // CPython 3.12: Expand enum-like singleton references
            // In CPython's python.gram: Store, Load, Add, Sub, etc. are enum VALUES
            // In C# SharpPy: These are singleton Instance properties
            // Pattern: Replace bare words that match singleton class names with ClassName.Instance
            expandedCode = ExpandSingletons(expandedCode);

            // Check if action code is a void statement (doesn't return a value)
            // CPython: Some alternatives call RaiseSyntaxError* which throws exceptions
            var trimmedCode = expandedCode.Trim();
            if (trimmedCode.StartsWith("RaiseSyntaxError", StringComparison.Ordinal) ||
                trimmedCode.StartsWith("RAISE_SYNTAX_ERROR", StringComparison.Ordinal))
            {
                // Don't add return - this code throws an exception
                WriteLine($"{expandedCode};");
            }
            else
            {
                // Normal case: add return statement
                WriteLine($"return {expandedCode};");
            }
        }
        else
        {
            // CPython 3.12: Default action for alternatives without action code
            // See: cpython-3.12/Tools/peg_generator/pegen/c_generator.py - emit_default_action()
            // Pattern: Return captured variables automatically

            if (namedItems.Count == 0)
            {
                // No captured variables: shouldn't happen in valid grammar, return null
                WriteLine($"// Default action: no captures (unexpected)");
                WriteLine("return null;");
            }
            else if (namedItems.Count == 1)
            {
                // Single variable: return it directly (CPython pattern)
                WriteLine($"// Default action: return single capture");
                WriteLine($"return {namedItems[0].Name};");
            }
            else
            {
                // Multiple variables: return first non-null (CPython _PyPegen_dummy_name pattern)
                WriteLine($"// Default action: return first non-null of {namedItems.Count} captures");
                var nullCoalescing = string.Join(" ?? ", namedItems.Select(i => i.Name));
                WriteLine($"return {nullCoalescing};");
            }
        }
    }

    // GetCastExpression() REMOVED
    // Replaced by AtomCallInfo.GenerateCode() which follows CPython FunctionCall.__str__() pattern
    // CPython does NOT have a separate GetCastExpression method
    // Type casting is handled by FunctionCall.assigned_variable_type field

    /// <summary>
    /// Generate AtomCallInfo for an Atom
    /// CPython: CCallMakerVisitor.generate_call() (c_generator.py)
    /// CPython visit_NameLeaf() → line 173-184
    /// </summary>
    private AtomCallInfo GenerateAtomCallInfo(Atom atom)
    {
        return atom switch
        {
            Keyword kw => new AtomCallInfo
            {
                Function = GenerateKeywordCode(kw),
                ReturnType = "GeneratedTokenInfo"
            },
            Token token => new AtomCallInfo
            {
                Function = $"Expect(PyToken.Type.{token.TokenType}, \"{token.TokenType}\")",
                ReturnType = "GeneratedTokenInfo"
            },
            RuleRef ruleRef => new AtomCallInfo
            {
                // CPython visit_NameLeaf: function=f"{name}_rule", return_type=rule.type
                Function = $"Parse_{ToPascalCase(ruleRef.Name)}()",
                ReturnType = _ruleReturnTypes.GetValueOrDefault(ruleRef.Name, "GeneratedPtr")
            },
            Group group => new AtomCallInfo
            {
                Function = GenerateGroupCode(group),
                ReturnType = "GeneratedPtr"
            },
            Optional opt => new AtomCallInfo
            {
                Function = GenerateOptionalCode(opt),
                ReturnType = "GeneratedPtr"  // ParseOptional always returns GeneratedPtr!
            },
            ZeroOrMore zm => new AtomCallInfo
            {
                // CPython: visit_Repeat0() → FunctionCall(return_type="asdl_seq *")
                Function = GenerateZeroOrMoreCode(zm),
                ReturnType = "GeneratedSeq"
            },
            OneOrMore om => new AtomCallInfo
            {
                // CPython: visit_Repeat1() → FunctionCall(return_type="asdl_seq *")
                Function = GenerateOneOrMoreCode(om),
                ReturnType = "GeneratedSeq"
            },
            PositiveLookahead pl => new AtomCallInfo
            {
                Function = GeneratePositiveLookaheadCode(pl),
                ReturnType = "GeneratedPtr"
            },
            NegativeLookahead nl => new AtomCallInfo
            {
                Function = GenerateNegativeLookaheadCode(nl),
                ReturnType = "GeneratedPtr"
            },
            Gather gather => new AtomCallInfo
            {
                // CPython: visit_Gather() → FunctionCall(return_type="asdl_seq *")
                Function = GenerateGatherCode(gather),
                ReturnType = "GeneratedSeq"
            },
            _ => throw new Exception($"Unknown atom type: {atom.GetType().Name}")
        };
    }

    /// <summary>
    /// Legacy method for backward compatibility - wraps GenerateAtomCallInfo
    /// </summary>
    private string GenerateAtomCode(Atom atom)
    {
        return GenerateAtomCallInfo(atom).Function;
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

    /// <summary>
    /// Expand singleton enum-like references in action code
    /// CPython: Store, Load, Add, Sub, etc. are used as enum values
    /// C#: These must be Instance properties (Store → GeneratedStore.Instance)
    /// Uses regex word boundaries to avoid replacing parts of identifiers
    /// </summary>
    private string ExpandSingletons(string code)
    {
        // CPython 3.12: List of all singleton enum-like types
        // expr_context: Load, Store, Del
        // operator: Add, Sub, Mult, MatMult, Div, Mod, Pow, LShift, RShift, BitOr, BitXor, BitAnd, FloorDiv
        // unaryop: Invert, Not, UAdd, USub
        // boolop: And, Or
        // cmpop: Eq, NotEq, Lt, LtE, Gt, GtE, Is, IsNot, In, NotIn

        var singletons = new[] {
            // expr_context
            "Load", "Store", "Del",
            // operator
            "Add", "Sub", "Mult", "MatMult", "Div", "Mod", "Pow",
            "LShift", "RShift", "BitOr", "BitXor", "BitAnd", "FloorDiv",
            // unaryop
            "Invert", "Not", "UAdd", "USub",
            // boolop
            "And", "Or",
            // cmpop
            "Eq", "NotEq", "Lt", "LtE", "Gt", "GtE", "Is", "IsNot", "In", "NotIn"
        };

        foreach (var singleton in singletons)
        {
            // Use regex word boundary to match only whole words
            // Avoid replacing "NotEq" when processing "Not" by using longer names first
            var pattern = $"\\b{singleton}\\b";
            var replacement = singleton == "Mod" ? "GeneratedMod_.Instance" : $"Generated{singleton}.Instance";
            code = System.Text.RegularExpressions.Regex.Replace(code, pattern, replacement);
        }

        return code;
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
