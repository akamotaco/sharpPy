using System.Text;
using SharpPy.PegGenerator.DataStructures;
using SharpPy.PegGenerator.Analysis;
using RegexUtils = System.Text.RegularExpressions.Regex;

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
                    // Type differs → need cast or .Cast<T>()
                    // CRITICAL: GeneratedSeq → GeneratedExprSeq/GeneratedStmtSeq requires .Cast<T>()
                    // Direct casting (GeneratedExprSeq)GeneratedSeq fails at runtime!
                    if (ReturnType == "GeneratedSeq" &&
                        (AssignedVariableType.EndsWith("Seq") && AssignedVariableType != "GeneratedSeq"))
                    {
                        // GeneratedSeq → GeneratedExprSeq/GeneratedStmtSeq/etc.
                        // Use .Cast<T>() method (CPython pattern)
                        // CRITICAL: Need null check before .Cast<T>() because it's an extension method
                        return $"({AssignedVariable} = {Function}?.Cast<{AssignedVariableType}>())";
                    }
                    else
                    {
                        // Normal cast for other types
                        // (c = (GeneratedAliasSeq)Parse_ImportFromTargets())
                        // (a = (GeneratedTokenInfo)ParseOptional(...))  // ParseOptional returns GeneratedPtr
                        return $"({AssignedVariable} = ({AssignedVariableType}){Function})";
                    }
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
    private Dictionary<string, int> _hardKeywords = new();  // keyword → token number
    private HashSet<string> _softKeywords = new();
    private Dictionary<string, string> _ruleReturnTypes = new();  // rule_name → return_type
    private int _artificialRuleCounter = 0;  // CPython: self.counter in parser_generator.py
    private List<PegRule> _artificialRules = new();  // CPython: self.all_rules (additional entries)
    private Dictionary<Group, string> _groupToRuleMap = new();  // Group → artificial rule name cache
    private HashSet<string> _leftRecursiveRules = new();  // Rules detected as left-recursive
    private HashSet<string> _leaderRules = new();  // Rules that are leaders (only these get LR wrapper)

    public string Generate(PegRule[] rules, Dictionary<string, int> hardKeywords, HashSet<string> softKeywords, TrailerCode? trailer = null)
    {
        _sb.Clear();
        _indentLevel = 0;
        _hardKeywords = hardKeywords;
        _softKeywords = softKeywords;
        _ruleReturnTypes.Clear();
        _artificialRuleCounter = 0;
        _artificialRules.Clear();
        _groupToRuleMap.Clear();
        _leftRecursiveRules.Clear();
        _leaderRules.Clear();

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

        // CPython 3.12: Detect left-recursive rules and their leaders using SCC algorithm
        // Tools/peg_generator/pegen/parser_generator.py - compute_left_recursives()
        var detector = new LeftRecursionDetector(rules);
        (_leftRecursiveRules, _leaderRules) = detector.DetectLeftRecursiveRules();

        Console.WriteLine($"[LEFT-RECURSION] Detected {_leftRecursiveRules.Count} left-recursive rules:");
        foreach (var ruleName in _leftRecursiveRules.OrderBy(r => r))
        {
            var isLeader = _leaderRules.Contains(ruleName);
            var leaderTag = isLeader ? " (leader)" : "";
            Console.WriteLine($"  - {ruleName}{leaderTag}");
        }

        // File header
        WriteLine("// Generated Parser from python_py.gram");
        WriteLine("// CPython 3.12 compatible - Auto-generated, DO NOT EDIT");
        WriteLine();
        WriteLine("using System;");
        WriteLine("using System.Collections.Generic;");
        WriteLine("using SharpPy.Generated;");
        WriteLine("using static SharpPy.Generated.PyParserRuntime;");
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
        WriteLine($"// Grammar Rules ({rules.Length} rules from python_cs.gram)");
        WriteLine("// ============================================================");
        WriteLine();

        // Generate main rules - artificial rules will be collected during generation
        foreach (var rule in rules)
        {
            GenerateRuleMethod(rule);
        }

        // Now generate artificial rules that were collected
        // Important: Artificial rules may contain Groups that generate more artificial rules
        // So we need to keep generating until no new artificial rules are added
        int processedCount = 0;
        while (processedCount < _artificialRules.Count)
        {
            var rulesToProcess = _artificialRules.Skip(processedCount).ToArray();
            foreach (var artificialRule in rulesToProcess)
            {
                GenerateRuleMethod(artificialRule);
                processedCount++;
            }
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

        // Check if this rule is left-recursive AND a leader
        // CPython 3.12: Only leaders get the growth loop wrapper
        // Non-leader rules in mutual recursion are generated as simple alternatives
        bool isLeftRecursive = _leftRecursiveRules.Contains(rule.Name);
        bool isLeader = _leaderRules.Contains(rule.Name);

        // Check if this rule has (memo) annotation
        // CPython 3.12: Memoization prevents infinite loops in mutual recursion
        bool isMemoized = rule.IsMemoized;

        WriteLine($"/// <summary>");
        WriteLine($"/// Rule: {rule.Name}");
        WriteLine($"/// Alternatives: {rule.Alternatives.Count}");
        if (!string.IsNullOrEmpty(rule.ReturnType))
        {
            WriteLine($"/// Return Type: {rule.ReturnType}");
        }
        if (isLeftRecursive && isLeader)
        {
            WriteLine($"/// Left-recursive rule (leader) - uses TryLeftRecursive wrapper");
        }
        else if (isLeftRecursive && !isLeader)
        {
            WriteLine($"/// Left-recursive rule (non-leader) - simple alternative");
        }
        if (isMemoized)
        {
            WriteLine($"/// CPython (memo) - uses TryMemoized wrapper");
        }
        WriteLine($"/// </summary>");
        WriteLine($"private {returnType}? Parse_{methodName}()");
        WriteLine("{");
        _indentLevel++;

        if (isLeftRecursive && isLeader)
        {
            // Only leaders get the TryLeftRecursive wrapper with growth loop
            // CPython: if node.left_recursive and node.leader
            WriteLine($"return ({returnType}?)TryLeftRecursive(\"{rule.Name}\", Parse_{methodName}_Raw);");
        }
        else if (isMemoized)
        {
            // CPython 3.12: Wrap with TryMemoized for (memo) rules
            WriteLine($"return ({returnType}?)TryMemoized(\"{rule.Name}\", Parse_{methodName}_Raw);");
        }
        else
        {
            // Normal rule generation
            WriteLine("int _mark = Mark();");
            WriteLine();
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine($"Console.WriteLine($\"[RULE] {rule.Name} at pos={{_position}}\");");
            WriteLine("#endif");
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

                // Pass the rule's return type to GenerateAlternative
                GenerateAlternative(alt, rule.Name, returnType);

                _indentLevel--;
                WriteLine("}");
            }

            WriteLine();
            WriteLine("Reset(_mark);");
            WriteLine("return null;");
        }

        _indentLevel--;
        WriteLine("}");
        WriteLine();

        // For leader (left-recursive) or memoized rules, generate the _Raw method that contains the actual parsing logic
        // CPython: Only leaders get the _raw() function with growth loop
        if ((isLeftRecursive && isLeader) || isMemoized)
        {
            var wrapperType = (isLeftRecursive && isLeader) ? "TryLeftRecursive" : "TryMemoized";
            var ruleType = (isLeftRecursive && isLeader) ? "left-recursive leader" : "memoized";
            WriteLine($"/// <summary>");
            WriteLine($"/// Raw parsing method for {ruleType} rule: {rule.Name}");
            WriteLine($"/// Called by {wrapperType} wrapper");
            WriteLine($"/// </summary>");
            WriteLine($"private {returnType}? Parse_{methodName}_Raw()");
            WriteLine("{");
            _indentLevel++;

            WriteLine("int _mark = Mark();");
            WriteLine();
            WriteLine("#if DEBUG_PARSE_LOG");
            WriteLine($"Console.WriteLine($\"[RULE-RAW] {rule.Name} at pos={{_position}}\");");
            WriteLine("#endif");
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

                // Pass the rule's return type to GenerateAlternative
                GenerateAlternative(alt, rule.Name, returnType);

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
    }

    private void GenerateAlternative(Alternative alt, string ruleName, string ruleReturnType)
    {
        // CPython 3.12 pattern: if (condition1 && condition2 && ...) { return success; }
        // Build list of all conditions that must succeed
        var conditions = new List<string>();
        var namedItems = alt.Items.Where(i => !string.IsNullOrEmpty(i.Name)).ToList();

        // Special case: If this alternative has no action code and only one unnamed item (e.g., "| assignment"),
        // we'll handle it differently
        bool isSingleUnnamedItem = string.IsNullOrEmpty(alt.ActionCode) &&
                                    namedItems.Count == 0 &&
                                    alt.Items.Count == 1 &&
                                    string.IsNullOrEmpty(alt.Items[0].Name);

        // Targeted debug logging for specific rules
        bool enableTargetedLog = ruleName == "file" || ruleName == "statements" ||
            ruleName == "simple_stmts" || ruleName == "simple_stmt" ||
            ruleName == "star_expressions" || ruleName == "star_expression" ||
            ruleName == "expression" ||
            ruleName == "disjunction" || ruleName == "conjunction" ||
            ruleName == "bitwise_or" || ruleName == "bitwise_xor" ||
            ruleName == "bitwise_and" || ruleName == "shift_expr" ||
            ruleName == "sum" || ruleName == "term" ||
            ruleName == "factor" || ruleName == "power" ||
            ruleName == "primary" || ruleName == "atom" ||
            // Additional rules for traceback.py debugging
            ruleName == "class_def" || ruleName == "class_def_raw" ||
            ruleName == "function_def" || ruleName == "function_def_raw" ||
            ruleName == "decorators" || ruleName == "decorated" ||
            ruleName == "parameters" || ruleName == "param" ||
            ruleName == "type_params" || ruleName == "type_param";

        // Generate variable declarations
        WriteLine("CaptureStart();");

        // Add debug logging for position/token tracking
        WriteLine("#if DEBUG_PARSE_LOG");
        WriteLine($"Console.WriteLine($\"[{ruleName.ToUpper()}] Alt start at pos={{_position}}, token={{(_position < _tokens.Count ? $\"{{_tokens[_position].Type}}('{{_tokens[_position].Value}}') @ {{_tokens[_position].Line}}:{{_tokens[_position].Column}}\" : \"EOF\")}}\");");
        WriteLine("#endif");
        WriteLine();

        foreach (var item in namedItems)
        {
            var varType = !string.IsNullOrEmpty(item.Type)
                ? item.Type
                : GetAtomReturnType(item.Atom);
            WriteLine($"{varType}? {item.Name} = null;");
        }

        if (isSingleUnnamedItem)
        {
            var item = alt.Items[0];
            bool isUnnamedNameToken = item.Atom is Token token && token.TokenType == "NAME";
            var varType = !string.IsNullOrEmpty(item.Type) ? item.Type : ruleReturnType;
            WriteLine($"{varType}? _alt_var = null;");
        }

        // CPython pattern: For alternatives without named captures, we need to capture unnamed items
        // Example: expression !':=' → capture expression result even though it's not named
        // CPython: expr_ty expression_var (uses specific type, not void*)
        // C#: Use current rule's return type (ruleReturnType), NOT atom's return type
        // Reason: In C, void* can hold any pointer. In C#, base class can't downcast to derived.
        if (namedItems.Count == 0 && !isSingleUnnamedItem)
        {
            for (int i = 0; i < alt.Items.Count; i++)
            {
                var item = alt.Items[i];
                // Skip lookaheads - they don't produce values
                if (item.Atom is PositiveLookahead || item.Atom is NegativeLookahead)
                    continue;

                // CRITICAL: Choose correct type for _item{i} variable
                // CPython: void* _res (universal), expr_ty expression_var (specific)
                // C#: Must match return type to avoid casting errors
                string varType;
                if (!string.IsNullOrEmpty(item.Type))
                {
                    // Explicit type annotation in grammar
                    varType = item.Type;
                }
                else if (item.Atom is Token || item.Atom is Keyword)
                {
                    // Token/Keyword always returns GeneratedTokenInfo
                    varType = "GeneratedTokenInfo";
                }
                else
                {
                    // Use rule's return type for other atoms (RuleRef, Group, etc.)
                    // This matches CPython's pattern where variables match function return type
                    varType = ruleReturnType;
                }
                WriteLine($"{varType}? _item{i} = null;");
            }
        }

        // Build condition list - CPython pattern: (a = rule(p)) && (b = rule(p)) && ...
        // CRITICAL: Optional items must be in the condition chain for proper evaluation order!
        // CPython: (lit = expect('(')) && (a = rule(p), true) && (lit2 = expect(')'))
        // This ensures '(' is checked BEFORE calling rule(p)
        WriteLine();
        foreach (var item in alt.Items)
        {
            if (!string.IsNullOrEmpty(item.Name))
            {
                var callInfo = GenerateAtomCallInfo(item.Atom);
                callInfo.AssignedVariable = item.Name;
                callInfo.AssignedVariableType = !string.IsNullOrEmpty(item.Type)
                    ? item.Type
                    : GetAtomReturnType(item.Atom);

                var code = callInfo.GenerateCode();
                bool isOptional = item.Atom is Optional;

                // Targeted debug: log before calling the function
                if (enableTargetedLog)
                {
                    var atomDesc = GetAtomDescription(item.Atom);
                    WriteLine($"#if DEBUG_PARSE_LOG");
                    WriteLine($"Console.WriteLine($\"[{ruleName.ToUpper()}] Calling {item.Name}={atomDesc} at pos={{_position}}, token={{(_position < _tokens.Count ? $\"{{_tokens[_position].Type}}('{{_tokens[_position].Value}}')\" : \"EOF\")}}\");");
                    WriteLine($"#endif");
                }

                if (isOptional)
                {
                    // CPython: (a = rule(p), !p->error_indicator) - comma operator
                    // The assignment happens, then we check error_indicator
                    // In C#, we use "|| true" to always succeed after assignment
                    // This ensures the assignment is part of the condition chain (proper eval order!)
                    conditions.Add($"({code} == null || true)");
                }
                else
                {
                    // Required: add null check to conditions
                    conditions.Add(code + " != null");
                }
            }
            else
            {
                // Unnamed item
                bool isOptional = item.Atom is Optional;
                bool isLookahead = item.Atom is PositiveLookahead || item.Atom is NegativeLookahead;

                // CPython pattern: For alternatives without named captures, assign to _item{i} variables
                if (namedItems.Count == 0 && !isSingleUnnamedItem && !isLookahead)
                {
                    var itemIndex = alt.Items.IndexOf(item);
                    var callInfo = GenerateAtomCallInfo(item.Atom);
                    callInfo.AssignedVariable = $"_item{itemIndex}";
                    // CRITICAL: Match variable declaration type
                    // Same logic as variable declaration above
                    string varType;
                    if (!string.IsNullOrEmpty(item.Type))
                    {
                        varType = item.Type;
                    }
                    else if (item.Atom is Token || item.Atom is Keyword)
                    {
                        varType = "GeneratedTokenInfo";
                    }
                    else
                    {
                        varType = ruleReturnType;
                    }
                    callInfo.AssignedVariableType = varType;
                    var code = callInfo.GenerateCode();

                    if (isOptional)
                    {
                        conditions.Add($"({code} == null || true)");
                    }
                    else
                    {
                        conditions.Add(code + " != null");
                    }
                }
                else
                {
                    // Regular unnamed item (not captured)
                    if (isOptional)
                    {
                        // CPython: (rule(p), !p->error_indicator)
                        // Unnamed optional must still be in condition chain for eval order!
                        conditions.Add($"({GenerateAtomCode(item.Atom)} == null || true)");
                    }
                    else
                    {
                        // Required unnamed: add to conditions
                        conditions.Add(GenerateAtomCode(item.Atom) + " != null");
                    }
                }
            }
        }

        // Handle single unnamed item specially
        if (isSingleUnnamedItem)
        {
            var item = alt.Items[0];
            bool isUnnamedNameToken = item.Atom is Token token && token.TokenType == "NAME";

            if (isUnnamedNameToken)
            {
                conditions.Clear();
                conditions.Add("(_alt_var = ExpectNameExpr()) != null");
            }
            else
            {
                var callInfo = GenerateAtomCallInfo(item.Atom);
                callInfo.AssignedVariable = "_alt_var";
                callInfo.AssignedVariableType = !string.IsNullOrEmpty(item.Type) ? item.Type : ruleReturnType;
                var code = callInfo.GenerateCode();
                conditions.Clear();
                conditions.Add(code + " != null");
            }
        }

        // CPython pattern: if (all conditions) { return action; }
        // Generate the big if statement wrapping the return
        if (conditions.Count == 0)
        {
            // No conditions means all items are optional or no items
            // This should always succeed, but we still wrap in if (true) for consistency
            WriteLine("if (true)");
        }
        else if (conditions.Count == 1)
        {
            WriteLine($"if ({conditions[0]})");
        }
        else
        {
            // Multiple conditions: chain with &&
            WriteLine($"if (");
            _indentLevel++;
            for (int i = 0; i < conditions.Count; i++)
            {
                var connector = i < conditions.Count - 1 ? " &&" : "";
                WriteLine($"{conditions[i]}{connector}");
            }
            _indentLevel--;
            WriteLine(")");
        }

        // Inside the if block: generate return statement
        WriteLine("{");
        _indentLevel++;

        if (!string.IsNullOrEmpty(alt.ActionCode))
        {
            // User-defined action code from python_cs.gram
            WriteLine($"// Action code from grammar");
            var expandedCode = alt.ActionCode.Replace("EXTRA", "_start_lineno, _start_col_offset, _end_lineno, _end_col_offset");
            expandedCode = ExpandSingletons(expandedCode);

            var trimmedCode = expandedCode.Trim();
            if (trimmedCode.StartsWith("RaiseSyntaxError", StringComparison.Ordinal) ||
                trimmedCode.StartsWith("RAISE_SYNTAX_ERROR", StringComparison.Ordinal))
            {
                WriteLine($"{expandedCode};");
            }
            else
            {
                WriteLine($"return {expandedCode};");
            }
        }
        else
        {
            // Default action
            // CPython 3.12: emit_default_action() in c_generator.py (line 732-753)
            if (isSingleUnnamedItem)
            {
                WriteLine($"// Default action: return single unnamed item");
                WriteLine($"return _alt_var;");
            }
            else if (namedItems.Count == 0)
            {
                // CPython pattern: Count non-lookahead items
                var nonLookaheadItems = alt.Items
                    .Where(i => !(i.Atom is PositiveLookahead) && !(i.Atom is NegativeLookahead))
                    .ToList();

                if (nonLookaheadItems.Count == 0)
                {
                    // All items are lookaheads (very rare case)
                    WriteLine($"// All items are lookaheads - return dummy success");
                    WriteLine($"return DummyResponse;");
                }
                else if (nonLookaheadItems.Count == 1)
                {
                    // CPython: len(local_variable_names) == 1 → return that variable
                    // Example: expression !':=' → returns expression (1 non-lookahead item)
                    var itemIndex = alt.Items.IndexOf(nonLookaheadItems[0]);
                    WriteLine($"// CPython pattern: 1 local variable → return it");
                    WriteLine($"return _item{itemIndex};");
                }
                else
                {
                    // CPython: len(local_variable_names) > 1 → _PyPegen_dummy_name()
                    // Example: '{' invalid_double_starred_kvpairs '}' → 3 items → dummy
                    WriteLine($"// CPython pattern: {nonLookaheadItems.Count} local variables → _PyPegen_dummy_name()");
                    WriteLine($"return ({ruleReturnType})DummyResponse;");
                }
            }
            else if (namedItems.Count == 1)
            {
                WriteLine($"// Default action: return single capture");
                WriteLine($"return {namedItems[0].Name};");
            }
            else
            {
                // CPython: Multiple named items → _PyPegen_dummy_name()
                WriteLine($"// CPython pattern: {namedItems.Count} named items → _PyPegen_dummy_name()");
                WriteLine($"return ({ruleReturnType})DummyResponse;");
            }
        }

        _indentLevel--;
        WriteLine("}");
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
                // CPython: NAME → _PyPegen_name_token(p) or _PyPegen_expect_token(p, NAME)
                // NUMBER → _PyPegen_expect_token(p, NUMBER)
                // STRING → _PyPegen_string_token(p)
                Function = token.TokenType == "NAME"
                    ? "ExpectName()"
                    : token.TokenType == "NUMBER"
                        ? $"ExpectToken(PyToken.Type.{token.TokenType})"
                        : token.TokenType == "STRING"
                            ? $"ExpectToken(PyToken.Type.{token.TokenType})"
                            : $"ExpectToken(PyToken.Type.{token.TokenType})",
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
            Forced forced => new AtomCallInfo
            {
                Function = GenerateForcedCode(forced),
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
        // CPython 로직 (Tools/peg_generator/pegen/c_generator.py line 186-203):
        // Soft keyword는 영숫자 패턴 [a-zA-Z_]\w* 일 때만 적용됨
        // 예: "match", "case", "type"
        //
        // Operator ("!", ":", ",")는 영숫자가 아니므로 exact_tokens에서 찾아야 함
        if (kw.IsSoft && RegexUtils.IsMatch(kw.Value, @"^[a-zA-Z_]\w*$"))
        {
            // Soft keyword: check as NAME token with specific value
            return $"ExpectSoftKeyword(\"{kw.Value}\")";
        }
        else
        {
            // Hard keyword or operator
            if (_hardKeywords.TryGetValue(kw.Value, out int tokenNumber))
            {
                // CPython 방식: keyword는 전용 토큰 번호로 처리
                // 예: 'break' → ExpectToken((PyToken.Type)508)
                return $"ExpectToken((PyToken.Type){tokenNumber})";
            }
            else
            {
                // Operator like ',', ';', '(', '!', etc.
                return $"ExpectOp(\"{EscapeString(kw.Value)}\")";
            }
        }
    }

    /// <summary>
    /// Create artificial rule from Group (on-demand, with caching)
    /// CPython: artifical_rule_from_rhs() in parser_generator.py (line 171-175)
    /// </summary>
    private string ArtificialRuleFromGroup(Group group)
    {
        _artificialRuleCounter++;
        var name = $"_tmp_{_artificialRuleCounter}";

        // CPython: self.all_rules[name] = Rule(name, None, rhs)
        // Create a new PegRule with the group's alternatives
        var artificialRule = new PegRule
        {
            Name = name,
            ReturnType = "GeneratedPtr",  // Default type for artificial rules
            Alternatives = group.Alternatives.ToList()
        };

        _artificialRules.Add(artificialRule);
        _ruleReturnTypes[name] = "GeneratedPtr";

        return name;
    }

    private string GenerateGroupCode(Group group)
    {
        // CPython pattern: visit_Group() → generate_call(node.rhs) → visit_Rhs()
        // visit_Rhs() creates artificial rule on-demand with caching (node in self.cache)

        // Check cache first to avoid duplicate rules for same group
        if (!_groupToRuleMap.TryGetValue(group, out var ruleName))
        {
            ruleName = ArtificialRuleFromGroup(group);
            _groupToRuleMap[group] = ruleName;
        }

        return $"Parse_{ToPascalCase(ruleName)}()";
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

    private string GenerateForcedCode(Forced forced)
    {
        // Forced (commit point): &&item
        // Unlike lookahead, this CONSUMES the token after checking it exists
        // CPython: This is a commit point - backtracking not allowed after this
        return GenerateAtomCode(forced.Inner);
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

        WriteLine("#if DEBUG_PARSE_LOG");
        WriteLine("Console.WriteLine($\"[GetKeywordOrNameType] name='{name}', nameLen={nameLen}, _nKeywordLists={_nKeywordLists}\");");
        WriteLine("#endif");
        WriteLine();
        WriteLine("// CPython 3.12: Check if name_len is within keyword lists bounds");
        WriteLine("if (nameLen >= _nKeywordLists || _reservedKeywords[nameLen] == null)");
        WriteLine("{");
        _indentLevel++;
        WriteLine("#if DEBUG_PARSE_LOG");
        WriteLine("Console.WriteLine($\"[GetKeywordOrNameType] Out of bounds or null, returning NAME\");");
        WriteLine("#endif");
        WriteLine("return (int)PyToken.Type.NAME;");
        _indentLevel--;
        WriteLine("}");
        WriteLine();
        WriteLine("// CPython 3.12: Search for keyword in the list for this length");
        WriteLine("if (_reservedKeywords[nameLen].TryGetValue(name, out PyToken.Type keywordType))");
        WriteLine("{");
        _indentLevel++;
        WriteLine("#if DEBUG_PARSE_LOG");
        WriteLine("Console.WriteLine($\"[GetKeywordOrNameType] Found keyword, returning {(int)keywordType}\");");
        WriteLine("#endif");
        WriteLine("return (int)keywordType;");
        _indentLevel--;
        WriteLine("}");
        WriteLine();
        WriteLine("#if DEBUG_PARSE_LOG");
        WriteLine("Console.WriteLine($\"[GetKeywordOrNameType] Not found in table, returning NAME\");");
        WriteLine("#endif");
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
        foreach (var kw in _hardKeywords.Keys.OrderBy(k => k))
        {
            WriteLine($"\"{kw}\",");
        }
        _indentLevel--;
        WriteLine("};");
        WriteLine();

        // Reserved keywords table (CPython 방식: 길이별 그룹화)
        GenerateReservedKeywordsTable();
    }

    /// <summary>
    /// CPython _setup_keywords() 방식으로 reserved keywords 테이블 생성
    /// 참고: CPython c_generator.py line 483-502, parser.c line 15-73
    /// </summary>
    private void GenerateReservedKeywordsTable()
    {
        if (_hardKeywords.Count == 0)
            return;

        WriteLine("// ============================================================");
        WriteLine("// Reserved Keywords Table (CPython 방식)");
        WriteLine("// NAME 토큰을 keyword 전용 토큰 타입으로 변환하기 위한 테이블");
        WriteLine("// ============================================================");
        WriteLine();

        // 길이별로 그룹화
        var groups = new Dictionary<int, List<(string keyword, int tokenNumber)>>();
        foreach (var (keyword, tokenNumber) in _hardKeywords)
        {
            int length = keyword.Length;
            if (!groups.ContainsKey(length))
                groups[length] = new List<(string, int)>();
            groups[length].Add((keyword, tokenNumber));
        }

        int maxLength = groups.Keys.Max();

        WriteLine($"private static readonly int _nKeywordLists = {maxLength + 1};");
        WriteLine();
        WriteLine("private static readonly Dictionary<string, PyToken.Type>?[] _reservedKeywords = new Dictionary<string, PyToken.Type>?[]");
        WriteLine("{");
        _indentLevel++;

        for (int length = 0; length <= maxLength; length++)
        {
            if (!groups.ContainsKey(length))
            {
                WriteLine("null,");
            }
            else
            {
                WriteLine("new Dictionary<string, PyToken.Type>");
                WriteLine("{");
                _indentLevel++;
                foreach (var (keyword, tokenNumber) in groups[length].OrderBy(x => x.keyword))
                {
                    WriteLine($"{{ \"{keyword}\", (PyToken.Type){tokenNumber} }},");
                }
                _indentLevel--;
                WriteLine("},");
            }
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

    private string GetAtomDescription(Atom atom)
    {
        return atom switch
        {
            RuleRef ruleRef => $"Parse_{ToPascalCase(ruleRef.Name)}()",
            Token token => $"ExpectToken({token.TokenType})",
            Keyword kw => $"Expect('{kw.Value}')",
            Optional opt => $"[{GetAtomDescription(opt.Inner)}]",
            OneOrMore om => $"{GetAtomDescription(om.Inner)}+",
            ZeroOrMore zm => $"{GetAtomDescription(zm.Inner)}*",
            Gather gather => $"{GetAtomDescription(gather.Separator)}.{GetAtomDescription(gather.Item)}{(gather.IsPlus ? "+" : "*")}",
            Group _ => "Group",
            PositiveLookahead pl => $"&{GetAtomDescription(pl.Inner)}",
            NegativeLookahead nl => $"!{GetAtomDescription(nl.Inner)}",
            Forced forced => $"&&{GetAtomDescription(forced.Inner)}",
            _ => atom.GetType().Name
        };
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
