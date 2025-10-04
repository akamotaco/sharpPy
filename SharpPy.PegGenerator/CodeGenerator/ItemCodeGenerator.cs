using System;
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
        private static int _lookaheadCounter = 0;

        public ItemCodeGenerator(
            CSharpCodeGenerator parent,
            Item item,
            string varName,
            string labelPrefix,
            bool insideRepeater = false)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _item = item ?? throw new ArgumentNullException(nameof(item));
            _varName = varName ?? throw new ArgumentNullException(nameof(varName));
            _labelPrefix = labelPrefix ?? "item";
            _insideRepeater = insideRepeater;
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

                default:
                    _parent.WriteLine($"// TODO: Unsupported atom type: {_item.Atom?.GetType().Name}");
                    break;
            }
        }

        private void GenerateStringLiteral(StringLiteral lit)
        {
            var escaped = _parent.EscapeString(lit.Value);

            // CPython 3.12 pattern: Expect exact token match and store result
            _parent.WriteLine($"// Expect '{escaped}'");
            _parent.WriteLine($"var {_varName} = Expect(\"{escaped}\");");
            _parent.WriteLine($"if ({_varName} == null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine("_position = _mark;");
            _parent.WriteLine("_res = null;");
            _parent.WriteLine("break;  // Exit this alternative");
            _parent.Dedent();
            _parent.WriteLine("}");
        }

        private void GenerateRuleRef(RuleRef ruleRef)
        {
            // CPython 3.12: Check if this is a token (uppercase) or a rule (lowercase)
            var isToken = char.IsUpper(ruleRef.Name[0]);

            if (isToken)
            {
                // Token reference - use Expect()
                _parent.WriteLine($"// Expect token: {ruleRef.Name}");
                _parent.WriteLine($"Console.WriteLine($\"[DEBUG] ExpectToken({ruleRef.Name}): pos={{_position}}, token={{CurrentToken?.Type}}:'{{CurrentToken?.Value}}'\");");

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
                    _parent.WriteLine($"if (_token_{_varName} == null)");
                    _parent.WriteLine("{");
                    _parent.Indent();
                    _parent.WriteLine("_position = _mark;");
                    _parent.WriteLine("_res = null;");
                    _parent.WriteLine("break;  // Exit this alternative");
                    _parent.Dedent();
                    _parent.WriteLine("}");

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
                    _parent.WriteLine($"if ({_varName} == null)");
                    _parent.WriteLine("{");
                    _parent.Indent();
                    _parent.WriteLine("_position = _mark;");
                    _parent.WriteLine("_res = null;");
                    _parent.WriteLine("break;  // Exit this alternative");
                    _parent.Dedent();
                    _parent.WriteLine("}");
                }

                _parent.WriteLine($"Console.WriteLine($\"[DEBUG] ExpectToken({ruleRef.Name}): result={{({_varName} != null ? \"SUCCESS\" : \"FAIL\")}}, newPos={{_position}}\");");
            }
            else
            {
                // Rule reference - call method
                var methodName = _parent.ToCSharpMethodName(ruleRef.Name);
                bool isInvalidRule = ruleRef.Name.StartsWith("invalid_", StringComparison.OrdinalIgnoreCase);

                _parent.WriteLine($"// Call rule: {ruleRef.Name}");

                // CPython 3.12: Wrap invalid_* rule calls in if (_callInvalidRules) check
                // Declare variable outside if block to avoid scope issues
                if (isInvalidRule)
                {
                    var ruleReturnType = _parent.GetRuleReturnType(ruleRef.Name);
                    _parent.WriteLine($"{ruleReturnType} {_varName} = null;");
                    _parent.WriteLine($"if (_callInvalidRules)");
                    _parent.WriteLine("{");
                    _parent.Indent();
                    _parent.WriteLine($"{_varName} = {methodName}();");
                    _parent.Dedent();
                    _parent.WriteLine("}");
                }
                else
                {
                    _parent.WriteLine($"var {_varName} = {methodName}();");
                }

                _parent.WriteLine($"if ({_varName} == null)");
                _parent.WriteLine("{");
                _parent.Indent();
                _parent.WriteLine("_position = _mark;");
                _parent.WriteLine("_res = null;");
                _parent.WriteLine("break;  // Exit this alternative");
                _parent.Dedent();
                _parent.WriteLine("}");
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
            var innerGen = new ItemCodeGenerator(_parent, innerItem, innerVarName, _labelPrefix);
            innerGen.Generate();

            // Determine type of optional result based on inner expression
            string itemType = DetermineItemType(opt.Expression);
            // Ensure nullable
            if (!itemType.EndsWith("?"))
            {
                itemType += "?";
            }

            // If parsing failed, reset and set to null (optional always succeeds)
            _parent.WriteLine($"{itemType} {_varName} = {innerVarName};");
            _parent.WriteLine($"if ({_varName} == null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"_position = _opt_mark_{_varName}; // Reset position");
            _parent.WriteLine($"{_varName} = null; // Optional not present");
            _parent.Dedent();
            _parent.WriteLine("}");
        }

        private void GenerateZeroOrMore(ZeroOrMore zm)
        {
            // CPython 3.12 pattern: Loop until parsing fails (always succeeds, may return empty list)
            _parent.WriteLine($"// Zero or more: {zm.Expression}*");

            // Determine the appropriate sequence type based on inner expression
            string seqType = DetermineSequenceType(zm.Expression);
            _parent.WriteLine($"var {_varName} = new {seqType}();");

            _parent.WriteLine($"while (true)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"int _loop_mark = _position;");

            // Generate code for the inner expression
            var innerItem = new Item { Atom = zm.Expression, Name = null };
            var innerVarName = $"_loop_elem_{_varName}";
            var innerGen = new ItemCodeGenerator(_parent, innerItem, innerVarName, _labelPrefix, insideRepeater: true);
            innerGen.Generate();

            // If parsing failed, break the loop
            _parent.WriteLine($"if ({innerVarName} == null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"_position = _loop_mark; // Reset to before failed attempt");
            _parent.WriteLine("break;");
            _parent.Dedent();
            _parent.WriteLine("}");

            // Add successful result to list
            _parent.WriteLine($"{_varName}.Add({innerVarName});");

            _parent.Dedent();
            _parent.WriteLine("}");
            _parent.WriteLine($"// Collected {_varName}.Count items (may be 0)");
        }

        private void GenerateOneOrMore(OneOrMore om)
        {
            // CPython 3.12 pattern: Must match at least once (fails if zero matches)
            _parent.WriteLine($"// One or more: {om.Expression}+");

            // Determine the appropriate sequence type based on inner expression
            string seqType = DetermineSequenceType(om.Expression);
            _parent.WriteLine($"var {_varName} = new {seqType}();");

            _parent.WriteLine($"while (true)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"int _loop_mark = _position;");

            // Generate code for the inner expression
            var innerItem = new Item { Atom = om.Expression, Name = null };
            var innerVarName = $"_loop_elem_{_varName}";
            var innerGen = new ItemCodeGenerator(_parent, innerItem, innerVarName, _labelPrefix, insideRepeater: true);
            innerGen.Generate();

            // If parsing failed, break the loop
            _parent.WriteLine($"if ({innerVarName} == null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"_position = _loop_mark; // Reset to before failed attempt");
            _parent.WriteLine("break;");
            _parent.Dedent();
            _parent.WriteLine("}");

            // Add successful result to list
            _parent.WriteLine($"{_varName}.Add({innerVarName});");

            _parent.Dedent();
            _parent.WriteLine("}");

            // CPython 3.12: OneOrMore must have at least one element
            _parent.WriteLine($"if ({_varName}.Count == 0)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"// One or more requires at least one match");
            _parent.WriteLine($"_position = _mark;");
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
            // CPython 3.12: Gather collects items separated by a separator
            // Example: ','.expression+ means one or more expressions separated by ','
            _parent.WriteLine($"// Gather: {gather.Separator}.{gather.Item}{(gather.IsOneOrMore ? "+" : "*")}");

            // Determine the sequence type for items
            string seqType = DetermineSequenceType(gather.Item);
            _parent.WriteLine($"var {_varName} = new {seqType}();");

            // CPython 3.12: Infer item type from sequence type
            // Example: GeneratedExprSeq → GeneratedExpr
            string expectedItemType = InferItemTypeFromSeqType(seqType);

            // First item (no separator before it)
            _parent.WriteLine($"// Parse first item (no separator)");
            var firstItemVarName = $"_first_{_varName}";
            var firstItemGen = new ItemCodeGenerator(_parent, new Item { Atom = gather.Item, Name = null }, firstItemVarName, _labelPrefix, insideRepeater: true);
            firstItemGen.Generate();

            // CPython 3.12: Determine if cast is necessary (C# explicit cast, C uses implicit void*)
            string actualItemType = DetermineItemType(gather.Item);
            bool needsCast = actualItemType != expectedItemType && actualItemType.Replace("?", "") != expectedItemType.Replace("?", "");

            if (gather.IsOneOrMore)
            {
                // For +: First item is required
                _parent.WriteLine($"if ({firstItemVarName} == null)");
                _parent.WriteLine("{");
                _parent.Indent();
                _parent.WriteLine($"_position = _mark;");
                _parent.WriteLine($"_res = null;");
                _parent.WriteLine("break;  // Exit this alternative");
                _parent.Dedent();
                _parent.WriteLine("}");
                if (needsCast)
                {
                    _parent.WriteLine($"{_varName}.Add(({expectedItemType}){firstItemVarName});");
                }
                else
                {
                    _parent.WriteLine($"{_varName}.Add({firstItemVarName});");
                }
            }
            else
            {
                // For *: First item is optional
                _parent.WriteLine($"if ({firstItemVarName} != null)");
                _parent.WriteLine("{");
                _parent.Indent();
                if (needsCast)
                {
                    _parent.WriteLine($"{_varName}.Add(({expectedItemType}){firstItemVarName});");
                }
                else
                {
                    _parent.WriteLine($"{_varName}.Add({firstItemVarName});");
                }
                _parent.Dedent();
                _parent.WriteLine("}");
            }

            // Loop for remaining items (separator + item)
            _parent.WriteLine($"// Parse remaining items (separator + item)");
            _parent.WriteLine($"while (true)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"int _loop_mark = _position;");

            // Parse separator
            var sepVarName = $"_sep_{_varName}";
            var sepItemGen = new ItemCodeGenerator(_parent, new Item { Atom = gather.Separator, Name = null }, sepVarName, _labelPrefix);
            sepItemGen.Generate();

            _parent.WriteLine($"if ({sepVarName} == null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"_position = _loop_mark;");
            _parent.WriteLine("break; // No more separators");
            _parent.Dedent();
            _parent.WriteLine("}");

            // Parse item
            var itemVarName = $"_loop_elem_{_varName}";
            var itemGen = new ItemCodeGenerator(_parent, new Item { Atom = gather.Item, Name = null }, itemVarName, _labelPrefix, insideRepeater: true);
            itemGen.Generate();

            _parent.WriteLine($"if ({itemVarName} == null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"_position = _loop_mark; // Reset to before separator");
            _parent.WriteLine("break; // No item after separator");
            _parent.Dedent();
            _parent.WriteLine("}");

            // Add to list with cast if necessary (reuse needsCast from above)
            if (needsCast)
            {
                _parent.WriteLine($"{_varName}.Add(({expectedItemType}){itemVarName});");
            }
            else
            {
                _parent.WriteLine($"{_varName}.Add({itemVarName});");
            }

            _parent.Dedent();
            _parent.WriteLine("}");

            if (!gather.IsOneOrMore)
            {
                // For *, the list can be empty, which is already handled
                _parent.WriteLine($"// Collected {_varName}.Count items (may be 0)");
            }
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

            // If all alternatives failed, this group fails the rule alternative
            _parent.WriteLine($"if ({_varName} == null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"_position = _mark;");
            _parent.WriteLine($"_res = null;");
            _parent.WriteLine("break;  // Exit this alternative");
            _parent.Dedent();
            _parent.WriteLine("}");
        }

        private void GenerateGroupAlternativeItems(Alternative alt, string altVarName, int altIndex)
        {
            // CPython 3.12: Generate items in nested if structure
            // Each item check is wrapped in "if (prev != null)"
            var items = alt.Items.ToList();
            if (items.Count == 0)
            {
                return;
            }

            // Generate first item
            var firstItemVar = $"{altVarName}_item0";
            GenerateGroupItem(items[0], firstItemVar, 0, alt.Items.Count);

            // Generate remaining items in nested if blocks
            for (int i = 1; i < items.Count; i++)
            {
                var prevItemVar = $"{altVarName}_item{i - 1}";
                var currItemVar = $"{altVarName}_item{i}";

                _parent.WriteLine($"if ({prevItemVar} != null)");
                _parent.WriteLine("{");
                _parent.Indent();

                GenerateGroupItem(items[i], currItemVar, i, items.Count);
            }

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

            // Close nested if blocks (one for each item after first)
            for (int i = 1; i < items.Count; i++)
            {
                _parent.Dedent();
                _parent.WriteLine("}");
            }
        }

        private void GenerateGroupItem(Item item, string varName, int itemIndex, int totalItems)
        {
            // Generate item parsing code without goto (CPython pattern for groups)
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
                    // Lookahead should not appear as group item that produces a value
                    // But if it does, generate the lookahead test and set result to true/false
                    _parent.WriteLine($"// WARNING: Lookahead in value position - this is unusual");
                    var lookaheadGen = new ItemCodeGenerator(_parent, item, $"_lookahead_{varName}", "group_dummy");
                    lookaheadGen.Generate();
                    _parent.WriteLine($"bool {varName} = true; // Lookahead succeeded");
                    break;

                default:
                    // For complex atoms (Optional, ZeroOrMore, OneOrMore, Group, etc.)
                    // These generate their own variable declarations
                    var itemGen = new ItemCodeGenerator(_parent, item, varName, "group_dummy");
                    itemGen.Generate();
                    // Note: ItemCodeGenerator.Generate() will declare the variable with appropriate type
                    break;
            }
        }

        private void GeneratePositiveLookahead(PositiveLookahead pla)
        {
            // CPython 3.12 pattern: Check without consuming
            var markVar = $"_lookahead_mark_{_lookaheadCounter++}";
            _parent.WriteLine($"// Positive lookahead - check without consuming");
            _parent.WriteLine($"int {markVar} = _position;");
            _parent.WriteLine($"// TODO: Parse lookahead content for {pla.Expression?.GetType().Name}");
            _parent.WriteLine($"_position = {markVar}; // Restore position");
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

                default:
                    // For complex cases, skip for now
                    _parent.WriteLine($"// TODO: Complex negative lookahead for {nla.Expression.GetType().Name}");
                    break;
            }

            // If the expression matched, this alternative must fail
            _parent.WriteLine($"if ({testVar} != null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"// Negative lookahead matched - fail this alternative");
            _parent.WriteLine($"_position = _mark;");
            _parent.WriteLine($"_res = null;");
            _parent.WriteLine($"break;  // Exit this alternative");
            _parent.Dedent();
            _parent.WriteLine("}");
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
                var innerGen = new ItemCodeGenerator(_parent, innerItem, _varName, _labelPrefix);
                innerGen.Generate();
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
            if (seqType == "GeneratedMixedSeq" || seqType == "GeneratedAstNodeSeq")
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
                    return "GeneratedMixedSeq"; // Key-value pairs are stored in mixed seq

                case "SlashWithDefault*":
                case "GeneratedSlashWithDefault":
                    return "GeneratedMixedSeq"; // SlashWithDefault is stored in mixed seq

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
    }
}
