using System;
using System.Text;
using SharpPy.PegGenerator.Grammar;

namespace SharpPy.PegGenerator.CodeGenerator
{
    /// <summary>
    /// Generates C# code for a single Item in a PEG alternative
    /// Handles all Atom types: StringLiteral, RuleRef, Optional, ZeroOrMore, OneOrMore, Group, Lookahead
    /// </summary>
    public class ItemCodeGenerator
    {
        private readonly CSharpCodeGenerator _parent;
        private readonly Item _item;
        private readonly string _varName;
        private readonly string _labelPrefix;
        private readonly bool _insideRepeater;
        private readonly bool _insideOptional;
        private readonly bool _insideLoopRule;  // CPython 3.12: inside loop rule function
        private readonly bool _insideGroup;  // CPython 3.12: inside group alternative (no break statements)
        private readonly string _contextMark;  // CPython 3.12: Context-specific mark variable for failures
        private static int _lookaheadCounter = 0;

        public ItemCodeGenerator(
            CSharpCodeGenerator parent,
            Item item,
            string varName,
            string labelPrefix,
            bool insideRepeater = false,
            bool insideOptional = false,
            bool insideLoopRule = false,
            bool insideGroup = false,
            string contextMark = null)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _item = item ?? throw new ArgumentNullException(nameof(item));
            _varName = varName ?? throw new ArgumentNullException(nameof(varName));
            _labelPrefix = labelPrefix ?? "item";
            _insideRepeater = insideRepeater;
            _insideOptional = insideOptional;
            _insideLoopRule = insideLoopRule;
            _insideGroup = insideGroup;
            _contextMark = contextMark ?? "_mark";  // Default to _mark if not specified
        }

        /// <summary>
        /// Generate C# code for this item based on its Atom type
        /// </summary>
        public void Generate()
        {
            switch (_item.Atom)
            {
                case StringLiteral lit:
                    GenerateStringLiteral(lit);
                    break;

                case RuleRef ruleRef:
                    GenerateRuleRef(ruleRef);
                    break;

                case Optional opt:
                    GenerateOptional(opt);
                    break;

                case ZeroOrMore zm:
                    GenerateZeroOrMore(zm);
                    break;

                case OneOrMore om:
                    GenerateOneOrMore(om);
                    break;

                case Gather gather:
                    GenerateGather(gather);
                    break;

                case Group grp:
                    GenerateGroup(grp);
                    break;

                case PositiveLookahead pla:
                    GeneratePositiveLookahead(pla);
                    break;

                case NegativeLookahead nla:
                    GenerateNegativeLookahead(nla);
                    break;

                case Cut cut:
                    GenerateCut(cut);
                    break;

                case Forced forced:
                    GenerateForced(forced);
                    break;

                default:
                    _parent.WriteLine($"// TODO: Unsupported atom type: {_item.Atom?.GetType().Name}");
                    break;
            }
        }

        private void GenerateStringLiteral(StringLiteral lit)
        {
            var escaped = _parent.EscapeString(lit.Value);

            // CPython 3.12: Check if this is a keyword (alphabetic identifier) or operator
            int keywordTokenType = _parent.GetKeywordTokenType(lit.Value);

            if (keywordTokenType > 0)
            {
                // CPython 3.12: This is a keyword - use KeywordType enum
                string enumName = lit.Value.ToUpper();
                if (lit.Value == "None" || lit.Value == "True" || lit.Value == "False")
                {
                    enumName = "KW_" + enumName; // Avoid conflict with C# keywords
                }
                _parent.WriteLine($"// Expect keyword: '{escaped}' (token type {keywordTokenType})");
                _parent.WriteLine($"var {_varName} = ExpectToken((GeneratedTokenType)KeywordType.{enumName});");
            }
            else
            {
                // CPython 3.12: This is an operator - use OP token type with value check
                // CPython tokenizer generates all operators as OP type, parser checks value
                _parent.WriteLine($"// Expect '{escaped}'");
                _parent.WriteLine($"var {_varName} = Expect(GeneratedTokenType.OP, \"{escaped}\");");
            }

            // CPython 3.12: Only add null check if NOT inside a repeater, loop rule, or optional
            if (!_insideRepeater && !_insideLoopRule && !_insideOptional)
            {
                _parent.WriteLine($"if ({_varName} == null)");
                _parent.WriteLine("{");
                _parent.Indent();
                _parent.WriteAlternativeFailure();
                _parent.Dedent();
                _parent.WriteLine("}");
            }
        }

        private void GenerateRuleRef(RuleRef ruleRef)
        {
            // CPython 3.12: Check if this is a token (uppercase) or a rule (lowercase)
            var isToken = char.IsUpper(ruleRef.Name[0]);

            if (isToken)
            {
                // Token reference - use Expect()
                _parent.WriteLine($"// Expect token: {ruleRef.Name}");
                _parent.WriteLine("#if DEBUG_PARSE_LOG");
                _parent.WriteLine($"Console.WriteLine($\"[DEBUG] ExpectToken({ruleRef.Name}): pos={{_position}}, token={{CurrentToken?.Type}}:'{{CurrentToken?.Value}}'\");");
                _parent.WriteLine("#endif");

                // CPython 3.12: NAME/STRING/NUMBER tokens are automatically converted to AST nodes
                // BUT: Inside repeaters (Gather, ZeroOrMore, OneOrMore), tokens stay as tokens
                // because _PyPegen_map_names_to_ids expects token list, not expr list
                var tokenName = ruleRef.Name.ToUpper();
                bool needsConversion = (tokenName == "NAME" || tokenName == "STRING" || tokenName == "NUMBER")
                                       && !_insideRepeater;

                if (needsConversion)
                {
                    // Store token temporarily, then convert to AST
                    _parent.WriteLine($"var _token_{_varName} = ExpectToken(GeneratedTokenType.{tokenName});");

                    // CPython 3.12: Only add null check if NOT inside a repeater or loop rule
                    if (!_insideRepeater && !_insideLoopRule)
                    {
                        _parent.WriteLine($"if (_token_{_varName} == null)");
                        _parent.WriteLine("{");
                        _parent.Indent();
                        _parent.WriteAlternativeFailure();
                        _parent.Dedent();
                        _parent.WriteLine("}");
                    }

                    // Convert token to AST node (CPython pattern)
                    string conversionMethod = tokenName switch
                    {
                        "NAME" => "NameToken",
                        "STRING" => "StringToken",
                        "NUMBER" => "NumberToken",
                        _ => throw new InvalidOperationException($"Unknown token: {tokenName}")
                    };
                    _parent.WriteLine($"var {_varName} = {conversionMethod}(_token_{_varName});");
                }
                else
                {
                    // Other tokens - no conversion needed
                    _parent.WriteLine($"var {_varName} = ExpectToken(GeneratedTokenType.{tokenName});");

                    // CPython 3.12: Only add null check if NOT inside a repeater or loop rule
                    if (!_insideRepeater && !_insideLoopRule)
                    {
                        _parent.WriteLine($"if ({_varName} == null)");
                        _parent.WriteLine("{");
                        _parent.Indent();
                        _parent.WriteLine("_position = _mark;");
                        _parent.WriteLine("_pendingSyntaxError = null;  // CPython 3.12: Clear error when alternative fails");
                        _parent.WriteLine("_res = null;");
                        _parent.WriteLine("break;  // Exit this alternative");
                        _parent.Dedent();
                        _parent.WriteLine("}");
                    }
                }

                _parent.WriteLine($"#if DEBUG_PARSE_LOG");
                _parent.WriteLine($"Console.WriteLine($\"[DEBUG] ExpectToken({ruleRef.Name}): result={{({_varName} != null ? \"SUCCESS\" : \"FAIL\")}}, newPos={{_position}}\");");
                _parent.WriteLine($"#endif");
            }
            else
            {
                // Rule reference - call method
                // CPython 3.12: Artificial rules (_Loop0_, _Gather_, _Loop1_, _Tmp_) keep their names
                bool isArtificialRule = ruleRef.Name.StartsWith("_Loop0_") ||
                                        ruleRef.Name.StartsWith("_Loop1_") ||
                                        ruleRef.Name.StartsWith("_Gather_") ||
                                        ruleRef.Name.StartsWith("_Tmp_");
                var methodName = isArtificialRule ? ruleRef.Name : _parent.ToCSharpMethodName(ruleRef.Name);
                bool isInvalidRule = ruleRef.Name.StartsWith("invalid_", StringComparison.OrdinalIgnoreCase);

                _parent.WriteLine($"// Call rule: {ruleRef.Name}");
                // Debug log for rule calls (conditional compilation for DEBUG_PARSE_LOG)
                if (!isArtificialRule && !isInvalidRule)
                {
                    _parent.WriteLine($"#if DEBUG_PARSE_LOG");
                    _parent.WriteLine($"Console.WriteLine($\"[CALL] {{_position,4}}: {ruleRef.Name}()\");");
                    _parent.WriteLine($"#endif");
                }

                // CPython 3.12: Wrap invalid_* rule calls in if (_callInvalidRules) check
                // Declare variable outside if block to avoid scope issues
                if (isInvalidRule)
                {
                    var ruleReturnType = _parent.GetRuleReturnType(ruleRef.Name);
                    _parent.WriteLine($"{ruleReturnType} {_varName} = null;");
                    _parent.WriteLine($"#if DEBUG_PARSE_LOG");
                    _parent.WriteLine($"Console.WriteLine($\"[{ruleRef.Name.ToUpper()}] _callInvalidRules={{_callInvalidRules}}\");");
                    _parent.WriteLine($"#endif");
                    _parent.WriteLine($"if (_callInvalidRules)");
                    _parent.WriteLine("{");
                    _parent.Indent();
                    _parent.WriteLine($"#if DEBUG_PARSE_LOG");
                    _parent.WriteLine($"Console.WriteLine($\"[{ruleRef.Name.ToUpper()}] Calling {methodName}()\");");
                    _parent.WriteLine($"#endif");
                    _parent.WriteLine($"{_varName} = {methodName}();");
                    _parent.WriteLine($"#if DEBUG_PARSE_LOG");
                    _parent.WriteLine($"Console.WriteLine($\"[{ruleRef.Name.ToUpper()}] Returned {{({_varName} == null ? \"null\" : \"non-null\")}}\");");
                    _parent.WriteLine($"#endif");
                    _parent.Dedent();
                    _parent.WriteLine("}");
                    _parent.WriteLine("else");
                    _parent.WriteLine("{");
                    _parent.Indent();
                    _parent.WriteLine($"#if DEBUG_PARSE_LOG");
                    _parent.WriteLine($"Console.WriteLine($\"[{ruleRef.Name.ToUpper()}] SKIP due to _callInvalidRules=false\");");
                    _parent.WriteLine($"#endif");
                    _parent.Dedent();
                    _parent.WriteLine("}");
                }
                else
                {
                    // Check if there's a type annotation
                    if (!string.IsNullOrEmpty(_item.TypeAnnotation))
                    {
                        var targetType = _parent.TranslatePegTypeToCS(_item.TypeAnnotation);
                        _parent.WriteLine($"{targetType} {_varName} = ({targetType}){methodName}();");
                    }
                    else
                    {
                        _parent.WriteLine($"var {_varName} = {methodName}();");
                    }
                }

                // CPython 3.12: Only add null check if NOT inside a repeater or loop rule
                // Repeaters (OneOrMore/ZeroOrMore) handle null checks themselves
                // Loop rules also handle null checks themselves
                if (!_insideRepeater && !_insideLoopRule)
                {
                    _parent.WriteLine($"if ({_varName} == null)");
                    _parent.WriteLine("{");
                    _parent.Indent();
                    _parent.WriteLine("_position = _mark;");

                    // CPython 3.12: DON'T clear error if it was set by invalid_* rule
                    // invalid_* rules set error_indicator when they want to report an error
                    if (isInvalidRule)
                    {
                        _parent.WriteLine("// CPython 3.12: invalid_* rule returned NULL - check if error was set");
                        _parent.WriteLine("// If error is set, preserve it and exit. Otherwise, try next alternative.");
                        _parent.WriteLine("if (_pendingSyntaxError != null)");
                        _parent.WriteLine("{");
                        _parent.Indent();
                        _parent.WriteLine("_res = null;");
                        _parent.WriteLine("break;  // Exit with error set");
                        _parent.Dedent();
                        _parent.WriteLine("}");
                    }
                    else
                    {
                        _parent.WriteLine("_pendingSyntaxError = null;  // CPython 3.12: Clear error when alternative fails");
                    }

                    _parent.WriteLine("_res = null;");
                    _parent.WriteLine("break;  // Exit this alternative");
                    _parent.Dedent();
                    _parent.WriteLine("}");
                }
            }
        }

        private void GenerateOptional(Optional opt)
        {
            // CPython 3.12 pattern: Optional always succeeds, result may be null
            _parent.WriteLine($"// Optional: [{opt.Expression}]");
            _parent.WriteLine($"int _opt_mark_{_varName} = _position;");

            // Generate code for the inner expression - it will declare its own variable
            var innerVarName = $"_opt_{_varName}";
            var innerItem = new Item { Atom = opt.Expression, Name = null };
            // CPython 3.12: Pass context mark and insideGroup to nested constructs
            var innerGen = new ItemCodeGenerator(_parent, innerItem, innerVarName, _labelPrefix,
                insideRepeater: false, insideOptional: true, insideLoopRule: _insideLoopRule, insideGroup: _insideGroup, contextMark: _contextMark);
            innerGen.Generate();

            // Determine type of optional result based on inner expression
            string itemType = DetermineItemType(opt.Expression);
            // Ensure nullable
            if (!itemType.EndsWith("?"))
            {
                itemType += "?";
            }

            // CPython 3.12: Optional pattern (expr, !p->error_indicator)
            // The comma operator in C: evaluates expr, stores in var, then checks !error_indicator
            // If error_indicator is set, the entire pattern fails
            // If no error, pattern succeeds (even if expr returned NULL)
            _parent.WriteLine($"// CPython: (a = expr, !p->error_indicator) - check error after optional");
            _parent.WriteLine($"{itemType} {_varName} = {innerVarName};");
            _parent.WriteLine("if (_pendingSyntaxError != null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine("// CPython: error_indicator is set - optional pattern FAILS");
            _parent.WriteLine("// This causes the entire alternative to fail (like && short-circuit in C)");
            _parent.WriteLine("_position = _mark;");
            _parent.WriteLine("_res = null;");
            _parent.WriteLine("break;  // Exit alternative with error preserved");
            _parent.Dedent();
            _parent.WriteLine("}");
            _parent.WriteLine($"else if ({_varName} == null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine("// CPython: No error, but expr returned NULL - optional not present");
            _parent.WriteLine($"_position = _opt_mark_{_varName}; // Reset position");
            _parent.Dedent();
            _parent.WriteLine("}");
        }

        private void GenerateZeroOrMore(ZeroOrMore zm)
        {
            // CPython 3.12: ALL loops use separate rule functions (artificial_rule_from_repeat)
            // This avoids mark restoration issues with complex patterns
            _parent.WriteLine($"// Zero or more: {zm.Expression}* (CPython: _Loop0_N rule)");

            // Get the inner pattern as a callable string (handles all patterns)
            string innerPattern = GetInnerPatternCallForLoop(zm.Expression);

            // Use type annotation if available, otherwise infer
            string seqType;
            if (!string.IsNullOrEmpty(_item.TypeAnnotation))
            {
                seqType = _parent.TranslatePegTypeToCS(_item.TypeAnnotation);
                _parent.WriteLine($"// Using type annotation: {_item.TypeAnnotation} → {seqType}");
            }
            else
            {
                seqType = DetermineSequenceType(zm.Expression);
            }

            // Register loop rule and get its name - pass Atom for complex patterns
            string loopRuleName = _parent.GetOrCreateLoopRule("Loop0", seqType, innerPattern, zm.Expression);

            // Call the loop rule with explicit cast if type annotation exists
            if (!string.IsNullOrEmpty(_item.TypeAnnotation))
            {
                _parent.WriteLine($"{seqType} {_varName} = ({seqType}){loopRuleName}();");
            }
            else
            {
                _parent.WriteLine($"var {_varName} = {loopRuleName}();");
            }
        }

        private void GenerateOneOrMore(OneOrMore om)
        {
            // CPython 3.12: ALL loops use separate rule functions (artificial_rule_from_repeat)
            _parent.WriteLine($"// One or more: {om.Expression}+ (CPython: _Loop1_N rule)");

            // Get the inner pattern as a callable string (handles all patterns)
            string innerPattern = GetInnerPatternCallForLoop(om.Expression);

            // Use type annotation if available, otherwise infer
            string seqType;
            if (!string.IsNullOrEmpty(_item.TypeAnnotation))
            {
                seqType = _parent.TranslatePegTypeToCS(_item.TypeAnnotation);
                _parent.WriteLine($"// Using type annotation: {_item.TypeAnnotation} → {seqType}");
            }
            else
            {
                seqType = DetermineSequenceType(om.Expression);
            }

            // Register loop rule and get its name - pass Atom for complex patterns
            string loopRuleName = _parent.GetOrCreateLoopRule("Loop1", seqType, innerPattern, om.Expression);

            // Call the loop rule with explicit cast if type annotation exists
            if (!string.IsNullOrEmpty(_item.TypeAnnotation))
            {
                _parent.WriteLine($"{seqType} {_varName} = ({seqType}){loopRuleName}();");
            }
            else
            {
                _parent.WriteLine($"var {_varName} = {loopRuleName}();");
            }

            // CPython 3.12: Loop1 rules return null on failure (0 elements)
            _parent.WriteLine($"if ({_varName} == null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"_position = _mark;");
                _parent.WriteLine("_pendingSyntaxError = null;  // CPython 3.12: Clear error when alternative fails");
            _parent.WriteLine($"_res = null;");
            _parent.WriteLine("break;  // Exit this alternative");
            _parent.Dedent();
            _parent.WriteLine("}");
        }

        /// <summary>
        /// Generate code for Gather pattern: separator.item+ or separator.item*
        /// CPython 3.12: Separated list like ','.expression+
        /// </summary>
        private void GenerateGather(Gather gather)
        {
            // CPython 3.12: Gather patterns generate artificial helper rules
            // Example: ','.expression+ generates _gather_N and _loop0_N helper rules
            // This matches CPython's artifical_rule_from_gather mechanism
            _parent.WriteLine($"// Gather: {gather.Separator}.{gather.Item}{(gather.IsOneOrMore ? "+" : "*")}");

            // Use type annotation if available, otherwise infer
            string seqType;
            if (!string.IsNullOrEmpty(_item.TypeAnnotation))
            {
                seqType = _parent.TranslatePegTypeToCS(_item.TypeAnnotation);
                _parent.WriteLine($"// Using type annotation: {_item.TypeAnnotation} → {seqType}");
            }
            else
            {
                seqType = DetermineSequenceType(gather.Item);
            }

            // CPython 3.12: Call artificial_rule_from_gather to get/create helper rule
            string gatherRuleName = _parent.ArtificialRuleFromGather(gather, seqType);

            // CPython 3.12: Simply call the generated gather rule
            _parent.WriteLine($"var {_varName} = {gatherRuleName}();");

            // CPython 3.12: gather+ requires at least one element, gather* allows empty
            if (gather.IsOneOrMore)
            {
                // For +: Result is required (gather rule returns null if no first element)
                _parent.WriteLine($"if ({_varName} == null)");
                _parent.WriteLine("{");
                _parent.Indent();
                _parent.WriteLine($"_position = {_contextMark};");
                _parent.WriteLine("_pendingSyntaxError = null;  // CPython 3.12: Clear error when alternative fails");
                _parent.WriteLine($"_res = null;");
                _parent.WriteLine("break;  // Exit this alternative");
                _parent.Dedent();
                _parent.WriteLine("}");
            }
            // For *, gather rule handles empty sequence correctly
        }

        private void GenerateGroup(Group grp)
        {
            // CPython 3.12 pattern: Group tries each alternative until one succeeds
            // Unlike rule alternatives, group uses nested if-else (no goto within group)
            _parent.WriteLine($"// Group: ({string.Join(" | ", grp.Alternatives.Select(a => a.ToString()))})");

            // Determine type of group result based on alternatives
            // CPython 3.12: When alternatives return different types, find common base
            // C uses void* (implicit conversion), C# needs explicit common type
            string groupType = "GeneratedAstNode?";
            if (grp.Alternatives.Count > 0)
            {
                var alternativeTypes = new List<string>();

                foreach (var alt in grp.Alternatives)
                {
                    var items = alt.Items.ToList();
                    if (items.Count == 0) continue;

                    // Find result item index for this alternative
                    int resultItemIndex = -1;
                    if (!string.IsNullOrWhiteSpace(alt.Action))
                    {
                        var actionContent = alt.Action.Trim();
                        // Check if action is a simple variable reference
                        if (actionContent.Length > 0 && char.IsLetter(actionContent[0]) &&
                            actionContent.All(c => char.IsLetterOrDigit(c) || c == '_'))
                        {
                            // Find which item has this name
                            for (int i = 0; i < items.Count; i++)
                            {
                                if (items[i].Name == actionContent)
                                {
                                    resultItemIndex = i;
                                    break;
                                }
                            }
                        }
                    }

                    // If no action or action variable not found, use last value-producing item
                    if (resultItemIndex < 0)
                    {
                        resultItemIndex = items.Count - 1;
                        // Skip lookaheads
                        while (resultItemIndex >= 0)
                        {
                            var atom = items[resultItemIndex].Atom;
                            if (atom is PositiveLookahead || atom is NegativeLookahead)
                            {
                                resultItemIndex--;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    if (resultItemIndex >= 0)
                    {
                        var altType = DetermineItemType(items[resultItemIndex].Atom);
                        alternativeTypes.Add(altType);
                    }
                }

                // Find common base type for all alternatives
                if (alternativeTypes.Count > 0)
                {
                    groupType = FindCommonBaseType(alternativeTypes);
                    if (!groupType.EndsWith("?"))
                    {
                        groupType += "?";
                    }
                }
            }

            _parent.WriteLine($"{groupType} {_varName} = null;");

            // Add type validation comment for GeneratedPtr? types (void* pattern)
            if (groupType == "GeneratedPtr?")
            {
                _parent.WriteLine($"// CPython 3.12: void* pattern - only GeneratedTokenInfo or GeneratedArg expected");
                _parent.WriteLine($"// Type check: if ({_varName} != null) {{ var typeName = {_varName}.GetType().Name; /* validate */ }}");
            }

            _parent.WriteLine($"int _group_mark_{_varName} = _position;");

            for (int i = 0; i < grp.Alternatives.Count; i++)
            {
                var alt = grp.Alternatives[i];
                var altVarName = $"_group_alt{i}_{_varName}";

                _parent.WriteLine($"// Try group alternative {i + 1}: {alt}");
                if (i > 0)
                {
                    _parent.WriteLine($"if ({_varName} == null)");
                }
                _parent.WriteLine("{");
                _parent.Indent();
                _parent.WriteLine($"_position = _group_mark_{_varName};");

                // Generate code for items with nested if structure (CPython pattern)
                GenerateGroupAlternativeItems(alt, altVarName, i);

                _parent.Dedent();
                _parent.WriteLine("}");
            }

            // If all alternatives failed, this group fails
            // UNLESS we're inside an Optional - it will handle the null
            // OR inside a loop rule - the loop will handle the null check
            // OR inside a repeater (gather/loop) - the repeater will handle it
            // OR inside a group - the parent group will handle it
            if (!_insideOptional && !_insideLoopRule && !_insideRepeater && !_insideGroup)
            {
                _parent.WriteLine($"if ({_varName} == null)");
                _parent.WriteLine("{");
                _parent.Indent();
                _parent.WriteLine($"_position = {_contextMark};");  // Use context mark
                _parent.WriteLine("_pendingSyntaxError = null;  // CPython 3.12: Clear error when alternative fails");
                _parent.WriteLine($"_res = null;");
                _parent.WriteLine("break;  // Exit this alternative");
                _parent.Dedent();
                _parent.WriteLine("}");
            }
        }

        private void GenerateGroupAlternativeItems(Alternative alt, string altVarName, int altIndex)
        {
            // CPython 3.12: Generate items in nested if structure OR && conditions
            // If items include lookaheads, use && pattern like CPython
            var items = alt.Items.ToList();
            if (items.Count == 0)
            {
                return;
            }

            // Check if we can use CPython && pattern:
            // - Must have lookahead
            // - Value-producing items must be simple (RuleRef or StringLiteral, not nested Groups)
            bool hasLookahead = items.Any(item => item.Atom is PositiveLookahead || item.Atom is NegativeLookahead);
            bool hasComplexItem = items.Any(item =>
                !(item.Atom is PositiveLookahead) &&
                !(item.Atom is NegativeLookahead) &&
                !(item.Atom is RuleRef) &&
                !(item.Atom is StringLiteral));

            if (hasLookahead && !hasComplexItem)
            {
                // CPython pattern: use && conditions for simple items with lookaheads
                GenerateGroupAlternativeWithLookaheads(alt, altVarName, altIndex, items);
            }
            else
            {
                // Original pattern: nested if blocks (handles all cases including nested groups)
                GenerateGroupAlternativeWithoutLookaheads(alt, altVarName, altIndex, items);
            }
        }

        private void GenerateGroupAlternativeWithoutLookaheads(Alternative alt, string altVarName, int altIndex, List<Item> items)
        {
            // CPython 3.12: Generate items in nested if structure
            // For lookaheads, we need special handling to ensure they can fail the alternative

            string groupMark = $"_group_mark_{_varName}";

            // Generate first item
            var firstItemVar = $"{altVarName}_item0";
            var firstAtom = items[0].Atom;

            if (firstAtom is PositiveLookahead || firstAtom is NegativeLookahead)
            {
                // First item is lookahead - generate it inline
                GenerateInlineLookahead(items[0], groupMark);
            }
            else
            {
                GenerateGroupItem(items[0], firstItemVar, 0, alt.Items.Count);
            }

            // Generate remaining items in nested if blocks
            int openBraceCount = 0;
            for (int i = 1; i < items.Count; i++)
            {
                var atom = items[i].Atom;

                if (atom is PositiveLookahead || atom is NegativeLookahead)
                {
                    // Lookahead item - check previous item first, then lookahead
                    var prevItemVar = $"{altVarName}_item{i - 1}";
                    _parent.WriteLine($"if ({prevItemVar} != null)");
                    _parent.WriteLine("{");
                    _parent.Indent();

                    // Generate lookahead inline - if it fails, nullify previous item
                    GenerateInlineLookaheadWithNullify(items[i], groupMark, prevItemVar);

                    _parent.Dedent();
                    _parent.WriteLine("}");
                    // Lookahead closes its own block, doesn't contribute to open brace count
                }
                else
                {
                    // Regular item - opens a brace that will be closed by completion logic
                    var prevItemVar = $"{altVarName}_item{i - 1}";
                    var currItemVar = $"{altVarName}_item{i}";

                    _parent.WriteLine($"if ({prevItemVar} != null)");
                    _parent.WriteLine("{");
                    _parent.Indent();

                    GenerateGroupItem(items[i], currItemVar, i, alt.Items.Count);
                    openBraceCount++;
                }
            }

            // Store the number of braces to close
            GenerateGroupAlternativeItemsCompletion(alt, altVarName, items, openBraceCount);
        }

        private void GenerateInlineLookahead(Item lookaheadItem, string groupMark)
        {
            // Generate lookahead test that can fail the group alternative
            var atom = lookaheadItem.Atom;

            if (atom is NegativeLookahead nla)
            {
                if (nla.Expression is StringLiteral strLit)
                {
                    var escaped = _parent.EscapeString(strLit.Value);
                    _parent.WriteLine($"// Negative lookahead: !'{escaped}'");
                    _parent.WriteLine($"if (CurrentToken?.Value == \"{escaped}\")");
                    _parent.WriteLine("{");
                    _parent.Indent();
                    _parent.WriteLine($"_position = {groupMark};");
                    _parent.WriteLine($"// Lookahead failed - alternative fails");
                    _parent.Dedent();
                    _parent.WriteLine("}");
                }
            }
            else if (atom is PositiveLookahead pla)
            {
                if (pla.Expression is StringLiteral strLit)
                {
                    var escaped = _parent.EscapeString(strLit.Value);
                    _parent.WriteLine($"// Positive lookahead: &'{escaped}'");
                    _parent.WriteLine($"if (CurrentToken?.Value != \"{escaped}\")");
                    _parent.WriteLine("{");
                    _parent.Indent();
                    _parent.WriteLine($"_position = {groupMark};");
                    _parent.WriteLine($"// Lookahead failed - alternative fails");
                    _parent.Dedent();
                    _parent.WriteLine("}");
                }
            }
        }

        private void GenerateInlineLookaheadWithNullify(Item lookaheadItem, string groupMark, string prevItemVar)
        {
            // Generate lookahead test that nullifies previous item if it fails
            var atom = lookaheadItem.Atom;

            if (atom is NegativeLookahead nla)
            {
                if (nla.Expression is StringLiteral strLit)
                {
                    var escaped = _parent.EscapeString(strLit.Value);
                    _parent.WriteLine($"// Negative lookahead: !'{escaped}'");
                    _parent.WriteLine($"if (CurrentToken?.Value == \"{escaped}\")");
                    _parent.WriteLine("{");
                    _parent.Indent();
                    _parent.WriteLine($"_position = {groupMark};");
                    _parent.WriteLine($"{prevItemVar} = null;  // Lookahead failed");
                    _parent.Dedent();
                    _parent.WriteLine("}");
                }
            }
            else if (atom is PositiveLookahead pla)
            {
                if (pla.Expression is StringLiteral strLit)
                {
                    var escaped = _parent.EscapeString(strLit.Value);
                    _parent.WriteLine($"// Positive lookahead: &'{escaped}'");
                    _parent.WriteLine($"if (CurrentToken?.Value != \"{escaped}\")");
                    _parent.WriteLine("{");
                    _parent.Indent();
                    _parent.WriteLine($"_position = {groupMark};");
                    _parent.WriteLine($"{prevItemVar} = null;  // Lookahead failed");
                    _parent.Dedent();
                    _parent.WriteLine("}");
                }
            }
        }

        private void GenerateGroupAlternativeWithLookaheads(Alternative alt, string altVarName, int altIndex, List<Item> items)
        {
            // CPython 3.12 pattern: if ((item1 = Expr()) != null && lookahead && ...)
            // Generate all items in a single if condition with && operators

            string groupVarName = altVarName;
            var match = System.Text.RegularExpressions.Regex.Match(groupVarName, "^_group_alt\\d+_");
            if (match.Success)
            {
                groupVarName = groupVarName.Substring(match.Length);
            }
            string groupMark = $"_group_mark_{_varName}";

            // Build the compound condition
            List<string> conditions = new List<string>();
            List<string> itemVars = new List<string>();
            int lastValueItemIndex = -1;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var atom = item.Atom;

                if (atom is PositiveLookahead pla)
                {
                    // Positive lookahead: check that pattern matches
                    if (pla.Expression is StringLiteral strLit)
                    {
                        var escaped = _parent.EscapeString(strLit.Value);
                        conditions.Add($"CurrentToken?.Value == \"{escaped}\"");
                    }
                    else if (pla.Expression is Group grp)
                    {
                        // Lookahead for multiple options: (a | b | c)
                        var subConditions = new List<string>();
                        foreach (var subAlt in grp.Alternatives)
                        {
                            if (subAlt.Items.Count == 1 && subAlt.Items[0].Atom is StringLiteral subLit)
                            {
                                var subEscaped = _parent.EscapeString(subLit.Value);
                                subConditions.Add($"CurrentToken?.Value == \"{subEscaped}\"");
                            }
                        }
                        if (subConditions.Any())
                        {
                            conditions.Add($"({string.Join(" || ", subConditions)})");
                        }
                    }
                }
                else if (atom is NegativeLookahead nla)
                {
                    // Negative lookahead: check that pattern does NOT match
                    if (nla.Expression is StringLiteral strLit)
                    {
                        var escaped = _parent.EscapeString(strLit.Value);
                        conditions.Add($"CurrentToken?.Value != \"{escaped}\"");
                    }
                    else if (nla.Expression is Group grp)
                    {
                        // Negative lookahead for multiple options: !(a | b | c) => NOT (a OR b OR c)
                        var subConditions = new List<string>();
                        foreach (var subAlt in grp.Alternatives)
                        {
                            if (subAlt.Items.Count == 1 && subAlt.Items[0].Atom is StringLiteral subLit)
                            {
                                var subEscaped = _parent.EscapeString(subLit.Value);
                                subConditions.Add($"CurrentToken?.Value == \"{subEscaped}\"");
                            }
                        }
                        if (subConditions.Any())
                        {
                            conditions.Add($"!({string.Join(" || ", subConditions)})");
                        }
                    }
                }
                else
                {
                    // Value-producing item
                    lastValueItemIndex = i;
                    var itemVar = $"{altVarName}_item{i}";
                    itemVars.Add(itemVar);

                    // Declare variable and add assignment condition
                    string itemType = DetermineItemType(atom);
                    _parent.WriteLine($"{itemType} {itemVar};");

                    string assignmentCondition = GenerateItemAssignmentCondition(item, itemVar);
                    conditions.Add(assignmentCondition);
                }
            }

            // Generate the compound if statement
            var combinedCondition = string.Join(" &&\n    ", conditions);
            _parent.WriteLine($"if (");
            _parent.Indent();
            _parent.WriteLine(combinedCondition);
            _parent.Dedent();
            _parent.WriteLine($")");
            _parent.WriteLine("{");
            _parent.Indent();

            // Assign the result (last value-producing item)
            if (lastValueItemIndex >= 0)
            {
                var resultVar = $"{altVarName}_item{lastValueItemIndex}";
                _parent.WriteLine($"{groupVarName} = {resultVar};");
            }

            _parent.Dedent();
            _parent.WriteLine("}");
            _parent.WriteLine($"else");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"// CPython 3.12: Group alternative failed, restore position");
            _parent.WriteLine($"_position = {groupMark};");
            _parent.Dedent();
            _parent.WriteLine("}");
        }

        private string GenerateItemAssignmentCondition(Item item, string varName)
        {
            // Generate inline assignment that can be used in if condition
            // Returns: "(varName = Expression()) != null"
            var atom = item.Atom;

            switch (atom)
            {
                case RuleRef ruleRef:
                    var isToken = char.IsUpper(ruleRef.Name[0]);
                    if (isToken)
                    {
                        return $"({varName} = ExpectToken(GeneratedTokenType.{ruleRef.Name.ToUpper()})) != null";
                    }
                    else
                    {
                        var methodName = _parent.ToCSharpMethodName(ruleRef.Name);
                        return $"({varName} = {methodName}()) != null";
                    }

                case StringLiteral lit:
                    var escaped = _parent.EscapeString(lit.Value);
                    return $"({varName} = Expect(\"{escaped}\")) != null";

                case Group grp:
                    // For nested groups, we need to call the parsing code
                    // This is complex - for now, fall back to separate variable
                    return $"({varName} = /* TODO: nested group */) != null";

                default:
                    return $"({varName} = /* TODO: {atom.GetType().Name} */) != null";
            }
        }

        private void GenerateGroupAlternativeItemsCompletion(Alternative alt, string altVarName, List<Item> items, int openBraceCount)
        {
            // After last item, assign result and close all if blocks
            // CPython 3.12: Find the last item that produces a value (not a lookahead)
            int lastValueItemIndex = items.Count - 1;
            while (lastValueItemIndex >= 0)
            {
                var atom = items[lastValueItemIndex].Atom;
                if (atom is PositiveLookahead || atom is NegativeLookahead)
                {
                    // Lookahead doesn't produce a value, skip it
                    lastValueItemIndex--;
                }
                else
                {
                    break;
                }
            }

            if (lastValueItemIndex < 0)
            {
                // All items are lookaheads? This shouldn't happen in valid grammar
                Console.WriteLine($"[ERROR] Alternative has no value-producing items: {alt}");
                lastValueItemIndex = items.Count - 1; // Fallback
            }

            var lastItemVar = $"{altVarName}_item{lastValueItemIndex}";

            // Extract original group var name from altVarName
            // altVarName format: "_group_altN_<groupVarName>" where N is alternative index
            // For nested groups: "_group_altN__group_altM_<varname>"
            // CPython 3.12: Only strip the FIRST prefix (current alternative's prefix)
            // NOT all prefixes - nested groups need to keep their parent prefixes!
            string groupVarName = altVarName;

            // Remove only the FIRST "_group_alt<digit>_" prefix
            var match = System.Text.RegularExpressions.Regex.Match(groupVarName, "^_group_alt\\d+_");
            if (match.Success)
            {
                groupVarName = groupVarName.Substring(match.Length);
            }

            _parent.WriteLine($"if ({lastItemVar} != null)");
            _parent.WriteLine("{");
            _parent.Indent();

            // CPython 3.12: Respect grammar action if present
            // If action is { varname }, return that variable instead of last item
            string resultVar = lastItemVar;
            if (!string.IsNullOrWhiteSpace(alt.Action))
            {
                var actionContent = alt.Action.Trim();
                // Check if action is a simple variable reference
                if (actionContent.Length > 0 && char.IsLetter(actionContent[0]) && actionContent.All(c => char.IsLetterOrDigit(c) || c == '_'))
                {
                    // Find which item has this name
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].Name == actionContent)
                        {
                            resultVar = $"{altVarName}_item{i}";
                            break;
                        }
                    }
                }
            }

            _parent.WriteLine($"{groupVarName} = {resultVar};");
            _parent.Dedent();
            _parent.WriteLine("}");

            // CPython 3.12: If group alternative failed (last item is null), restore position
            // This is critical for sequences like "star_targets '='" where star_targets succeeds
            // but '=' fails - position must be restored to group start
            _parent.WriteLine($"else");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"// CPython 3.12: Group alternative failed, restore position");
            _parent.WriteLine($"_position = _group_mark_{_varName};");
            _parent.Dedent();
            _parent.WriteLine("}");

            // Close nested if blocks - use the count passed from caller
            // This accounts for lookaheads which close their own blocks
            for (int i = 0; i < openBraceCount; i++)
            {
                _parent.Dedent();
                _parent.WriteLine("}");
            }
        }

        private void GenerateGroupItem(Item item, string varName, int itemIndex, int totalItems)
        {
            // Generate item parsing code without goto (CPython pattern for groups)
            // CPython 3.12: Pass group mark as context mark for nested lookaheads
            string groupMark = $"_group_mark_{_varName}";

            switch (item.Atom)
            {
                case RuleRef ruleRef:
                    var isToken = char.IsUpper(ruleRef.Name[0]);
                    if (isToken)
                    {
                        _parent.WriteLine($"GeneratedTokenInfo? {varName} = ExpectToken(GeneratedTokenType.{ruleRef.Name.ToUpper()});");
                    }
                    else
                    {
                        var methodName = _parent.ToCSharpMethodName(ruleRef.Name);
                        string returnType = _parent.GetRuleReturnType(ruleRef.Name);
                        _parent.WriteLine($"{returnType} {varName} = {methodName}();");
                    }
                    break;

                case StringLiteral lit:
                    var escaped = _parent.EscapeString(lit.Value);
                    _parent.WriteLine($"GeneratedTokenInfo? {varName} = Expect(\"{escaped}\");");
                    break;

                case PositiveLookahead pla:
                case NegativeLookahead nla:
                    // CPython 3.12: Lookahead as group item (e.g., "expression !':='")
                    // Lookaheads don't produce values, so we generate them inline without variable
                    // They should be checked as part of the previous item's condition
                    // This is handled in GenerateGroupAlternativeItems
                    break;

                default:
                    // For complex atoms (Optional, ZeroOrMore, OneOrMore, Group, etc.)
                    // These generate their own variable declarations
                    // CPython 3.12: Pass insideGroup=true so nested lookaheads don't use break
                    var itemGen = new ItemCodeGenerator(_parent, item, varName, "group_dummy",
                        insideRepeater: false, insideOptional: false, insideLoopRule: _insideLoopRule, insideGroup: true, contextMark: groupMark);
                    itemGen.Generate();
                    // Note: ItemCodeGenerator.Generate() will declare the variable with appropriate type
                    break;
            }
        }

        private void GeneratePositiveLookahead(PositiveLookahead pla)
        {
            // CPython 3.12 pattern: Check without consuming
            // Positive lookahead: &expr
            // If expr matches, restore position and continue
            // If expr doesn't match, fail this alternative
            var markVar = $"_lookahead_mark_{_lookaheadCounter}";
            var testVar = $"_lookahead_test_{_lookaheadCounter}";
            _lookaheadCounter++;

            _parent.WriteLine($"// Positive lookahead: &({pla.Expression})");
            _parent.WriteLine($"int {markVar} = _position;");

            // Generate code to parse the lookahead expression
            _parent.WriteLine($"bool {testVar} = false;");
            _parent.WriteLine($"{{");
            _parent.Indent();

            // Parse the expression (actual parsing depends on expression type)
            GenerateLookaheadExpression(pla.Expression, testVar);

            _parent.Dedent();
            _parent.WriteLine($"}}");
            _parent.WriteLine($"_position = {markVar}; // Restore position after lookahead");

            // CPython 3.12: If lookahead failed, fail using context-appropriate mark
            // Inside group: don't use break, just set position and let group's if-else handle it
            _parent.WriteLine($"if (!{testVar})");
            _parent.WriteLine($"{{");
            _parent.Indent();
            _parent.WriteLine($"_position = {_contextMark};");
                _parent.WriteLine("_pendingSyntaxError = null;  // CPython 3.12: Clear error when alternative fails");
            if (!_insideGroup)
            {
                _parent.WriteLine($"_res = null;");
                _parent.WriteLine($"break;  // Exit this alternative");
            }
            _parent.Dedent();
            _parent.WriteLine($"}}");
        }

        private void GenerateNegativeLookahead(NegativeLookahead nla)
        {
            // CPython 3.12 pattern: Negative lookahead fails the alternative if the pattern matches
            // We test without consuming input (position doesn't change)
            var testVar = $"_lookahead_test_{_lookaheadCounter++}";

            _parent.WriteLine($"// Negative lookahead: !({nla.Expression})");
            _parent.WriteLine($"GeneratedTokenInfo? {testVar} = null;");

            // Generate code to test the expression - only check current token position
            switch (nla.Expression)
            {
                case Group grp:
                    // For group like ('=' | ':='), test each alternative
                    _parent.WriteLine($"// Test if current token matches: {nla.Expression}");
                    foreach (var alt in grp.Alternatives)
                    {
                        if (alt.Items.Count == 1 && alt.Items[0].Atom is StringLiteral lit)
                        {
                            var escaped = _parent.EscapeString(lit.Value);
                            _parent.WriteLine($"if (CurrentToken?.Value == \"{escaped}\") {{ {testVar} = CurrentToken; }}");
                        }
                    }
                    break;

                case StringLiteral slit:
                    // Single string literal
                    var escapedS = _parent.EscapeString(slit.Value);
                    _parent.WriteLine($"if (CurrentToken?.Value == \"{escapedS}\") {{ {testVar} = CurrentToken; }}");
                    break;

                case RuleRef rref:
                    // CPython 3.12: RuleRef in negative lookahead - check if token/rule matches
                    // Check if it's a TOKEN (uppercase) or a rule (lowercase/mixed)
                    bool isToken = char.IsUpper(rref.Name[0]);
                    if (isToken)
                    {
                        // Token - use ExpectToken without consuming (save/restore position)
                        _parent.WriteLine($"// Negative lookahead: !{rref.Name}");
                        _parent.WriteLine($"int _nla_mark = _position;");
                        _parent.WriteLine($"{testVar} = ExpectToken(GeneratedTokenType.{rref.Name});");
                        _parent.WriteLine($"_position = _nla_mark;  // Restore position after lookahead");
                    }
                    else
                    {
                        // Rule - call the method and restore position
                        var methodName = ToPascalCase(rref.Name);
                        _parent.WriteLine($"// Negative lookahead: !{rref.Name}");
                        _parent.WriteLine($"int _nla_mark = _position;");
                        _parent.WriteLine($"if ({methodName}() != null) {{ {testVar} = CurrentToken; }}");
                        _parent.WriteLine($"_position = _nla_mark;  // Restore position after lookahead");
                    }
                    break;

                default:
                    // For complex cases, skip for now
                    _parent.WriteLine($"// TODO: Complex negative lookahead for {nla.Expression.GetType().Name}");
                    break;
            }

            // CPython 3.12: If the expression matched, fail using context-appropriate mark
            // Inside group: don't use break, just set position and let group's if-else handle it
            // Inside gather loop/top-level: use break to exit alternative
            _parent.WriteLine($"if ({testVar} != null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"// Negative lookahead matched - fail");
            _parent.WriteLine($"_position = {_contextMark};");
                _parent.WriteLine("_pendingSyntaxError = null;  // CPython 3.12: Clear error when alternative fails");
            if (!_insideGroup)
            {
                _parent.WriteLine($"_res = null;");
                _parent.WriteLine($"break;  // Exit this alternative");
            }
            _parent.Dedent();
            _parent.WriteLine("}");
        }

        private void GenerateLookaheadExpression(Atom expr, string testVar)
        {
            // CPython 3.12: Generate code to test if expression matches without consuming input
            // Sets testVar to true if expression matches
            switch (expr)
            {
                case RuleRef ruleRef:
                    // Check if it's a TOKEN (uppercase) or a rule (lowercase/mixed)
                    bool isToken = char.IsUpper(ruleRef.Name[0]);
                    if (isToken)
                    {
                        // Token - check using ExpectToken
                        _parent.WriteLine($"if (ExpectToken(GeneratedTokenType.{ruleRef.Name}) != null) {{ {testVar} = true; }}");
                    }
                    else
                    {
                        // Rule - call the method (convert snake_case to PascalCase)
                        var methodName = ToPascalCase(ruleRef.Name);
                        _parent.WriteLine($"if ({methodName}() != null) {{ {testVar} = true; }}");
                    }
                    break;

                case Group grp:
                    // For group like ('(' | '[' | '.'), test each alternative
                    _parent.WriteLine($"// Test if current token matches any alternative");
                    foreach (var alt in grp.Alternatives)
                    {
                        if (alt.Items.Count == 1 && alt.Items[0].Atom is StringLiteral lit)
                        {
                            var escaped = _parent.EscapeString(lit.Value);
                            _parent.WriteLine($"if (CurrentToken?.Value == \"{escaped}\") {{ {testVar} = true; }}");
                        }
                        else if (alt.Items.Count == 1 && alt.Items[0].Atom is RuleRef rref)
                        {
                            // Recursively call this method for nested rules
                            GenerateLookaheadExpression(rref, testVar);
                        }
                        else if (alt.Items.Count == 1 && alt.Items[0].Atom is PositiveLookahead pla)
                        {
                            // Nested positive lookahead in group: (&expr | ...) in lookahead context
                            // Handle simple cases only
                            if (pla.Expression is StringLiteral nestedLit)
                            {
                                var escapedNested = _parent.EscapeString(nestedLit.Value);
                                _parent.WriteLine($"if (CurrentToken?.Value == \"{escapedNested}\") {{ {testVar} = true; }}");
                            }
                            else
                            {
                                _parent.WriteLine($"// Nested lookahead in group too complex");
                            }
                        }
                        else
                        {
                            // Complex group alternative - skip for now
                            _parent.WriteLine($"// TODO: Complex group alternative in lookahead");
                        }
                    }
                    break;

                case StringLiteral slit:
                    // Single string literal
                    var escapedS = _parent.EscapeString(slit.Value);
                    _parent.WriteLine($"if (CurrentToken?.Value == \"{escapedS}\") {{ {testVar} = true; }}");
                    break;

                case PositiveLookahead nestedPla:
                    // Nested positive lookahead: &(&expr)
                    // CPython 3.12: This is rare, handle by checking the inner expression
                    // For now, conservatively check the inner expression without recursion
                    switch (nestedPla.Expression)
                    {
                        case StringLiteral nestedLit:
                            var escapedNested = _parent.EscapeString(nestedLit.Value);
                            _parent.WriteLine($"if (CurrentToken?.Value == \"{escapedNested}\") {{ {testVar} = true; }}");
                            break;
                        default:
                            // Too complex, fail for safety
                            _parent.WriteLine($"// Nested lookahead too complex, defaulting to FAIL");
                            break;
                    }
                    break;

                default:
                    // For unknown cases, FAIL (don't assume success)
                    // This is safer than assuming success
                    _parent.WriteLine($"// TODO: Complex positive lookahead for {expr.GetType().Name}");
                    _parent.WriteLine($"// Defaulting to FAIL for safety");
                    // testVar stays false
                    break;
            }
        }

        private void GenerateCut(Cut cut)
        {
            // CPython 3.12 pattern: Commit to current alternative
            _parent.WriteLine($"// Cut operator - commit to this alternative");
            _parent.WriteLine("// TODO: Implement cut semantics (prevent backtracking)");

            // Important: Cut wraps another expression, we need to generate code for it
            if (cut.Expression != null)
            {
                var innerItem = new Item { Atom = cut.Expression, Name = null };
                // CPython 3.12: Pass all context flags to nested constructs
                var innerGen = new ItemCodeGenerator(_parent, innerItem, _varName, _labelPrefix,
                    insideRepeater: _insideRepeater, insideOptional: _insideOptional, insideLoopRule: _insideLoopRule, insideGroup: _insideGroup, contextMark: _contextMark);
                innerGen.Generate();
            }
        }

        private void GenerateForced(Forced forced)
        {
            // CPython 3.12: Forced token/result - must match or raise syntax error
            // _PyPegen_expect_forced_token(p, type, expected) for StringLiteral
            // _PyPegen_expect_forced_result(p, result, expected) for Group/RuleRef

            if (forced.Node is StringLiteral slit)
            {
                // CPython: _PyPegen_expect_forced_token(p, type, "expected")
                var escaped = _parent.EscapeString(slit.Value);
                _parent.WriteLine($"// Forced token: &&'{escaped}'");
                _parent.WriteLine($"var {_varName} = ExpectForcedToken(GeneratedTokenType.OP, \"{escaped}\");");
                _parent.WriteLine($"if ({_varName} == null)");
                _parent.WriteLine("{");
                _parent.Indent();
                _parent.WriteLine($"_position = {_contextMark};");
                _parent.WriteLine($"_res = null;");
                if (!_insideGroup)
                {
                    _parent.WriteLine($"break;  // Exit this alternative");
                }
                _parent.Dedent();
                _parent.WriteLine("}");
            }
            else if (forced.Node is Group || forced.Node is RuleRef)
            {
                // CPython: _PyPegen_expect_forced_result(p, result, "expected")
                // First generate code to parse the node
                var innerItem = new Item { Atom = forced.Node, Name = null };
                var innerVarName = $"_forced_{_varName}";
                var innerGen = new ItemCodeGenerator(_parent, innerItem, innerVarName, _labelPrefix,
                    insideRepeater: _insideRepeater, insideOptional: _insideOptional, insideLoopRule: _insideLoopRule, insideGroup: _insideGroup, contextMark: _contextMark);
                innerGen.Generate();

                // Then check result and raise syntax error if null
                _parent.WriteLine($"// Forced result: &&({forced.Node})");
                _parent.WriteLine($"var {_varName} = ExpectForcedResult({innerVarName}, \"{forced.Node}\");");
                _parent.WriteLine($"if ({_varName} == null)");
                _parent.WriteLine("{");
                _parent.Indent();
                _parent.WriteLine($"_position = {_contextMark};");
                _parent.WriteLine($"_res = null;");
                if (!_insideGroup)
                {
                    _parent.WriteLine($"break;  // Exit this alternative");
                }
                _parent.Dedent();
                _parent.WriteLine("}");
            }
            else
            {
                _parent.WriteLine($"// TODO: Forced tokens don't work with {forced.Node.GetType().Name} nodes");
            }
        }

        /// <summary>
        /// Determine the type of a single item (for Optional, Group, etc.)
        /// CPython 3.12: Analyze the expression to determine result type
        /// </summary>
        private string DetermineItemType(Atom expression)
        {
            // If expression is a RuleRef
            if (expression is RuleRef ruleRef)
            {
                // CPython PEG convention: Uppercase = Token, lowercase = Rule
                bool isToken = char.IsUpper(ruleRef.Name[0]);

                if (isToken)
                {
                    return "GeneratedTokenInfo?";
                }
                else
                {
                    return _parent.GetRuleReturnType(ruleRef.Name);
                }
            }

            // For StringLiteral, return token type
            if (expression is StringLiteral)
            {
                return "GeneratedTokenInfo?";
            }

            // For Group, analyze alternatives - CPython 3.12: Use action or last value-producing item
            if (expression is Group group && group.Alternatives.Count > 0)
            {
                // CPython 3.12: Group type is determined by what it returns
                // When multiple alternatives return different types, find common base type
                // C uses void* (implicit conversion), C# needs explicit common type
                var alternativeTypes = new List<string>();

                foreach (var alt in group.Alternatives)
                {
                    var items = alt.Items.ToList();
                    if (items.Count == 0) continue;

                    // Find result item index for this alternative
                    int resultItemIndex = -1;
                    if (!string.IsNullOrWhiteSpace(alt.Action))
                    {
                        var actionContent = alt.Action.Trim();
                        // Check if action is a simple variable reference
                        if (actionContent.Length > 0 && char.IsLetter(actionContent[0]) &&
                            actionContent.All(c => char.IsLetterOrDigit(c) || c == '_'))
                        {
                            // Find which item has this name
                            for (int i = 0; i < items.Count; i++)
                            {
                                if (items[i].Name == actionContent)
                                {
                                    resultItemIndex = i;
                                    break;
                                }
                            }
                        }
                    }

                    // If no action or action variable not found, use last value-producing item
                    if (resultItemIndex < 0)
                    {
                        resultItemIndex = items.Count - 1;
                        // Skip lookaheads
                        while (resultItemIndex >= 0)
                        {
                            var atom = items[resultItemIndex].Atom;
                            if (atom is PositiveLookahead || atom is NegativeLookahead)
                            {
                                resultItemIndex--;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    if (resultItemIndex >= 0)
                    {
                        var altType = DetermineItemType(items[resultItemIndex].Atom);
                        alternativeTypes.Add(altType);
                    }
                }

                // Find common base type for all alternatives
                if (alternativeTypes.Count > 0)
                {
                    return FindCommonBaseType(alternativeTypes);
                }
            }

            // For Optional, analyze inner expression
            if (expression is Optional opt)
            {
                return DetermineItemType(opt.Expression);
            }

            // For Gather, return sequence type
            if (expression is Gather gather)
            {
                return DetermineSequenceType(gather.Item);
            }

            // For Lookahead, return bool (lookahead doesn't capture value, only tests)
            if (expression is PositiveLookahead || expression is NegativeLookahead)
            {
                // Lookahead doesn't produce a value for assignment
                // This should not be used in Optional/Group that needs a value
                Console.WriteLine($"[WARNING] Lookahead in value position - should not happen");
                return "bool";
            }

            // Default: base AST node type
            return "GeneratedAstNode?";
        }

        /// <summary>
        /// Determine the appropriate sequence type for loop/gather operations
        /// CPython 3.12: Analyze the inner expression to determine element type
        /// </summary>
        private string DetermineSequenceType(Atom expression)
        {
            // If expression is a RuleRef
            if (expression is RuleRef ruleRef)
            {
                // CPython PEG convention: Uppercase = Token, lowercase = Rule
                bool isToken = char.IsUpper(ruleRef.Name[0]);

                if (isToken)
                {
                    // Token sequence: List<GeneratedTokenInfo>
                    // But there's no GeneratedTokenInfoSeq, so we need to create one
                    // For now, tokens in sequences are not common - use GeneratedAstNodeSeq
                    Console.WriteLine($"[WARNING] Token sequence {ruleRef.Name}* - tokens don't usually form sequences");
                    return "List<GeneratedTokenInfo>";
                }
                else
                {
                    // Rule sequence: look up the rule's return type
                    string returnType = _parent.GetRuleReturnType(ruleRef.Name);
                    return MapReturnTypeToSequenceType(returnType);
                }
            }

            // For StringLiteral
            if (expression is StringLiteral)
            {
                // String literals in sequences (e.g., '.'*) return tokens
                return "List<GeneratedTokenInfo>";
            }

            // For Group/Optional/etc, try to infer from contained expressions
            if (expression is Group group && group.Alternatives.Count > 0)
            {
                // CPython 3.12: Group result type is determined by what it returns
                // 1. If action exists and references a variable, use that variable's type
                // 2. Otherwise, use the last value-producing item's type
                var firstAlt = group.Alternatives[0];
                var items = firstAlt.Items.ToList();

                if (items.Count > 0)
                {
                    // Check if action references a specific variable
                    int resultItemIndex = -1;
                    if (!string.IsNullOrWhiteSpace(firstAlt.Action))
                    {
                        var actionContent = firstAlt.Action.Trim();
                        // Check if action is a simple variable reference
                        if (actionContent.Length > 0 && char.IsLetter(actionContent[0]) &&
                            actionContent.All(c => char.IsLetterOrDigit(c) || c == '_'))
                        {
                            // Find which item has this name
                            for (int i = 0; i < items.Count; i++)
                            {
                                if (items[i].Name == actionContent)
                                {
                                    resultItemIndex = i;
                                    break;
                                }
                            }
                        }
                    }

                    // If no action or action variable not found, use last value-producing item
                    if (resultItemIndex < 0)
                    {
                        resultItemIndex = items.Count - 1;
                        // Skip lookaheads
                        while (resultItemIndex >= 0)
                        {
                            var atom = items[resultItemIndex].Atom;
                            if (atom is PositiveLookahead || atom is NegativeLookahead)
                            {
                                resultItemIndex--;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    if (resultItemIndex >= 0)
                    {
                        return DetermineSequenceType(items[resultItemIndex].Atom);
                    }
                }
            }

            // For Gather, return the item sequence type
            if (expression is Gather gather)
            {
                return DetermineSequenceType(gather.Item);
            }

            // Default fallback - but this should be avoided
            // Log warning if we reach here
            Console.WriteLine($"[WARNING] Could not determine sequence type for {expression}, using GeneratedExprSeq as default");
            return "GeneratedExprSeq";
        }

        /// <summary>
        /// Infer item type from sequence type
        /// CPython 3.12: Reverse mapping from Seq to element type
        /// Example: GeneratedExprSeq → GeneratedExpr
        /// </summary>
        private string InferItemTypeFromSeqType(string seqType)
        {
            if (seqType.StartsWith("List<") && seqType.EndsWith(">"))
            {
                // List<GeneratedStmtSeq> → GeneratedStmtSeq
                return seqType.Substring(5, seqType.Length - 6);
            }

            // Special case: MixedSeq and AstNodeSeq store GeneratedAstNode
            if (seqType == "GeneratedSeq" || seqType == "GeneratedAstNodeSeq")
            {
                return "GeneratedAstNode";
            }

            if (seqType.EndsWith("Seq"))
            {
                // Remove "Seq" suffix: GeneratedExprSeq → GeneratedExpr
                return seqType.Substring(0, seqType.Length - 3);
            }

            if (seqType.StartsWith("List<GeneratedTokenInfo>"))
            {
                return "GeneratedTokenInfo";
            }

            // Default fallback
            return seqType;
        }

        /// <summary>
        /// Map CPython rule return type to C# sequence type
        /// </summary>
        private string MapReturnTypeToSequenceType(string returnType)
        {
            if (string.IsNullOrEmpty(returnType))
            {
                return "GeneratedExprSeq"; // Default
            }

            // Remove nullable marker for type matching
            string baseType = returnType.TrimEnd('?');

            // Map CPython types to C# sequence types
            switch (baseType)
            {
                case "stmt_ty":
                case "GeneratedStmt":
                    return "GeneratedStmtSeq";

                case "expr_ty":
                case "GeneratedExpr":
                    return "GeneratedExprSeq";

                case "alias_ty":
                case "GeneratedAlias":
                    return "GeneratedAliasSeq";

                case "pattern_ty":
                case "GeneratedPattern":
                    return "GeneratedPatternSeq";

                case "keyword_ty":
                case "GeneratedKeyword":
                    return "GeneratedKeywordSeq";

                case "KeywordOrStarred*":
                case "GeneratedKeywordOrStarred":
                    return "GeneratedKeywordOrStarredSeq";

                case "arg_ty":
                case "GeneratedArg":
                    return "GeneratedArgSeq";

                case "excepthandler_ty":
                case "GeneratedExcepthandler":
                case "GeneratedExceptHandler":
                    return "GeneratedExcepthandlerSeq";

                case "withitem_ty":
                case "GeneratedWithitem":
                    return "GeneratedWithitemSeq";

                case "match_case_ty":
                case "GeneratedMatchCase":
                    return "GeneratedMatchCaseSeq";

                case "comprehension_ty":
                case "GeneratedComprehension":
                    return "GeneratedComprehensionSeq";

                case "type_param_ty":
                case "GeneratedTypeParam":
                    return "GeneratedTypeParamSeq";

                case "type_ignore_ty":
                case "GeneratedTypeIgnore":
                    return "GeneratedTypeIgnoreSeq";

                // Helper types for grammar parsing
                case "KeyValuePair*":
                case "GeneratedKeyValuePair":
                    return "GeneratedSeq"; // Key-value pairs are stored in mixed seq

                case "SlashWithDefault*":
                case "GeneratedSlashWithDefault":
                    return "GeneratedSeq"; // SlashWithDefault is stored in mixed seq

                // Base AST node type - used for mixed or unknown AST nodes
                case "GeneratedAstNode":
                    return "GeneratedAstNodeSeq";

                // CPython 3.12: If already a sequence type, wrap in List<>
                // Example: statement[asdl_stmt_seq*] returns GeneratedStmtSeq
                // So statement+ should be List<GeneratedStmtSeq>, not GeneratedStmtSeq
                // This is then flattened by _PyPegen_seq_flatten
                case string s when s.EndsWith("Seq"):
                    return $"List<{s}>";

                default:
                    // Unknown type - log and use default
                    Console.WriteLine($"[WARNING] Unknown return type '{returnType}', using GeneratedExprSeq");
                    return "GeneratedExprSeq";
            }
        }

        /// <summary>
        /// Find common base type for multiple alternative types
        /// CPython 3.12: C uses void* for implicit conversion, C# needs explicit common type
        /// Type hierarchy:
        /// - GeneratedAstNode (base for all)
        ///   - GeneratedStmt
        ///   - GeneratedExpr
        ///   - GeneratedPattern
        ///   - GeneratedSlashWithDefault
        ///   - GeneratedStarEtc
        ///   - etc.
        /// </summary>
        private string FindCommonBaseType(List<string> types)
        {
            if (types.Count == 0) return "GeneratedAstNode?";
            if (types.Count == 1) return types[0];

            // Remove nullable markers for comparison
            var cleanTypes = types.Select(t => t.TrimEnd('?')).Distinct().ToList();
            if (cleanTypes.Count == 1) return types[0]; // All same type

            // Check if all types are the same (accounting for nullable)
            var firstType = cleanTypes[0];
            if (cleanTypes.All(t => t == firstType))
            {
                return types[0]; // Return with original nullable marker
            }

            // CPython 3.12: Type hierarchy mapping
            // Seq types: If ALL are Seq types, use common Seq base
            // BUT: Seq types (like GeneratedAstNodeSeq) are List<>, not AstNode subclasses!
            // Special handling needed when mixing Seq and non-Seq types
            bool hasSeqTypes = cleanTypes.Any(t => t.EndsWith("Seq"));
            bool hasNonSeqTypes = cleanTypes.Any(t => !t.EndsWith("Seq"));

            if (hasSeqTypes && hasNonSeqTypes)
            {
                // Mixing Seq and non-Seq types (e.g., GeneratedAstNodeSeq + GeneratedSlashWithDefault)
                // Seq types don't inherit from AstNode, they inherit from GeneratedPtr
                // This happens in invalid_* rules - CPython uses void* for both
                // C# solution: Use GeneratedPtr as common base (both Seq and AstNode derive from it)
                Console.WriteLine($"[WARNING] Group mixing Seq and non-Seq types: {string.Join(", ", types)}");
                return "GeneratedPtr?";
            }

            if (cleanTypes.All(t => t.EndsWith("Seq")))
            {
                // All are sequence types - use GeneratedAstNodeSeq as common base
                return "GeneratedAstNodeSeq?";
            }

            // Check if all derive from GeneratedStmt
            var stmtTypes = new HashSet<string> { "GeneratedStmt", "GeneratedFunctionDefStmt", "GeneratedAsyncFunctionDefStmt",
                "GeneratedClassDefStmt", "GeneratedReturnStmt", "GeneratedDeleteStmt", "GeneratedAssignStmt",
                "GeneratedAugAssignStmt", "GeneratedAnnAssignStmt", "GeneratedForStmt", "GeneratedAsyncForStmt",
                "GeneratedWhileStmt", "GeneratedIfStmt", "GeneratedWithStmt", "GeneratedAsyncWithStmt",
                "GeneratedMatchStmt", "GeneratedRaiseStmt", "GeneratedTryStmt", "GeneratedTryStarStmt",
                "GeneratedAssertStmt", "GeneratedImportStmt", "GeneratedImportFromStmt", "GeneratedGlobalStmt",
                "GeneratedNonlocalStmt", "GeneratedExprStmt", "GeneratedPassStmt", "GeneratedBreakStmt",
                "GeneratedContinueStmt" };
            if (cleanTypes.All(t => stmtTypes.Contains(t)))
            {
                return "GeneratedStmt?";
            }

            // Check if all derive from GeneratedExpr
            var exprTypes = new HashSet<string> { "GeneratedExpr", "GeneratedBoolOpExpr", "GeneratedNamedExpr",
                "GeneratedBinOpExpr", "GeneratedUnaryOpExpr", "GeneratedLambdaExpr", "GeneratedIfExpr",
                "GeneratedDictExpr", "GeneratedSetExpr", "GeneratedListCompExpr", "GeneratedSetCompExpr",
                "GeneratedDictCompExpr", "GeneratedGeneratorExpExpr", "GeneratedAwaitExpr", "GeneratedYieldExpr",
                "GeneratedYieldFromExpr", "GeneratedCompareExpr", "GeneratedCallExpr", "GeneratedFormattedValueExpr",
                "GeneratedJoinedStrExpr", "GeneratedConstantExpr", "GeneratedAttributeExpr", "GeneratedSubscriptExpr",
                "GeneratedStarredExpr", "GeneratedNameExpr", "GeneratedListExpr", "GeneratedTupleExpr", "GeneratedSliceExpr" };
            if (cleanTypes.All(t => exprTypes.Contains(t)))
            {
                return "GeneratedExpr?";
            }

            // Check if all derive from GeneratedPattern
            var patternTypes = new HashSet<string> { "GeneratedPattern", "GeneratedMatchValue", "GeneratedMatchSingleton",
                "GeneratedMatchSequence", "GeneratedMatchMapping", "GeneratedMatchClass", "GeneratedMatchStar",
                "GeneratedMatchAs", "GeneratedMatchOr" };
            if (cleanTypes.All(t => patternTypes.Contains(t)))
            {
                return "GeneratedPattern?";
            }

            // CPython 3.12: Special case for invalid_* rules mixing incompatible types
            // All types ultimately derive from GeneratedAstNode
            // Examples:
            // - (slash_no_default | slash_with_default) → both are GeneratedAstNode subclasses
            // - (param_no_default | ',') → GeneratedArg vs GeneratedTokenInfo (see below)

            // Special case: TokenInfo doesn't derive from AstNode
            if (cleanTypes.Contains("GeneratedTokenInfo"))
            {
                // Mixing TokenInfo with AstNode types - this happens in invalid_* error recovery rules
                // CPython 3.12: Uses void* for both tokens and AST nodes
                // C# solution: Use GeneratedPtr base type (both TokenInfo and AstNode derive from it)
                Console.WriteLine($"[INFO] Group mixing TokenInfo with AST nodes: {string.Join(", ", types)}");
                Console.WriteLine($"[INFO] Using GeneratedPtr? as common base (Token+Node mix)");
                return "GeneratedPtr?";
            }

            // Default: All AST types derive from GeneratedAstNode
            return "GeneratedAstNode?";
        }

        /// <summary>
        /// CPython 3.12: Convert inner expression to a callable pattern string
        /// Used for loop rule registration
        /// </summary>
        /// <summary>
        /// CPython 3.12: Get callable pattern string for loop rule generation
        /// Handles all pattern types: RuleRef, StringLiteral, Group, etc.
        /// </summary>
        private string GetInnerPatternCallForLoop(Atom expression)
        {
            switch (expression)
            {
                case RuleRef ruleRef:
                    // CPython PEG convention: Uppercase = Token, lowercase = Rule
                    bool isToken = char.IsUpper(ruleRef.Name[0]);

                    if (isToken)
                    {
                        // Token: Use ExpectToken
                        return $"ExpectToken(GeneratedTokenType.{ruleRef.Name})";
                    }
                    else
                    {
                        // Rule: Call the rule method - convert snake_case to PascalCase
                        string methodName = ToPascalCase(ruleRef.Name);
                        return $"{methodName}()";
                    }

                case StringLiteral lit:
                    // Expect the literal string
                    return $"Expect(\"{lit.Value}\")";

                case Group group:
                    // CPython 3.12: Groups in loops are common (e.g., ('.' | '...')*)
                    // We need to generate inline code for the group
                    // For now, generate a simple call pattern - the actual logic is in the loop rule
                    // Generate unique variable name based on group content
                    var groupHash = group.GetHashCode().ToString().Replace("-", "N");
                    return $"ParseGroupInLoop_{groupHash}()";

                default:
                    Console.WriteLine($"[WARNING] Unsupported loop inner pattern: {expression.GetType().Name}");
                    return "/* Unsupported pattern */";
            }
        }

        /// <summary>
        /// Convert snake_case to PascalCase for method names
        /// </summary>
        private string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var parts = name.Split('_');
            var result = new StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length > 0)
                {
                    result.Append(char.ToUpper(part[0]));
                    if (part.Length > 1)
                    {
                        result.Append(part.Substring(1));
                    }
                }
            }
            return result.ToString();
        }
    }
}
