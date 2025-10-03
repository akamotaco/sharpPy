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
        private static int _lookaheadCounter = 0;

        public ItemCodeGenerator(
            CSharpCodeGenerator parent,
            Item item,
            string varName,
            string labelPrefix)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _item = item ?? throw new ArgumentNullException(nameof(item));
            _varName = varName ?? throw new ArgumentNullException(nameof(varName));
            _labelPrefix = labelPrefix ?? "item";
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
                _parent.WriteLine($"var {_varName} = ExpectToken(GeneratedTokenType.{ruleRef.Name.ToUpper()});");
                _parent.WriteLine($"Console.WriteLine($\"[DEBUG] ExpectToken({ruleRef.Name}): result={{({_varName} != null ? \"SUCCESS\" : \"FAIL\")}}, newPos={{_position}}\");");
                _parent.WriteLine($"if ({_varName} == null)");
                _parent.WriteLine("{");
                _parent.Indent();
                _parent.WriteLine("_position = _mark;");
                _parent.WriteLine("_res = null;");
                _parent.WriteLine("break;  // Exit this alternative");
                _parent.Dedent();
                _parent.WriteLine("}");
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
                    _parent.WriteLine($"object? {_varName} = null;");
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

            // If parsing failed, reset and set to null (optional always succeeds)
            _parent.WriteLine($"object? {_varName} = {innerVarName};");
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
            _parent.WriteLine($"var {_varName} = new System.Collections.Generic.List<object?>();");
            _parent.WriteLine($"while (true)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"int _loop_mark = _position;");

            // Generate code for the inner expression
            var innerItem = new Item { Atom = zm.Expression, Name = null };
            var innerVarName = $"_loop_elem_{_varName}";
            var innerGen = new ItemCodeGenerator(_parent, innerItem, innerVarName, _labelPrefix);
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
            _parent.WriteLine($"var {_varName} = new System.Collections.Generic.List<object?>();");
            _parent.WriteLine($"while (true)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"int _loop_mark = _position;");

            // Generate code for the inner expression
            var innerItem = new Item { Atom = om.Expression, Name = null };
            var innerVarName = $"_loop_elem_{_varName}";
            var innerGen = new ItemCodeGenerator(_parent, innerItem, innerVarName, _labelPrefix);
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

        private void GenerateGroup(Group grp)
        {
            // CPython 3.12 pattern: Group tries each alternative until one succeeds
            // Unlike rule alternatives, group uses nested if-else (no goto within group)
            _parent.WriteLine($"// Group: ({string.Join(" | ", grp.Alternatives.Select(a => a.ToString()))})");
            _parent.WriteLine($"object? {_varName} = null;");
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
            var lastItemVar = $"{altVarName}_item{items.Count - 1}";
            var groupVarName = altVarName.Substring(0, altVarName.LastIndexOf("_group_alt") + "_group_alt0".Length - 1);
            // Extract original group var name from altVarName
            var parts = altVarName.Split(new[] { "_group_alt" }, StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                groupVarName = parts[1].Substring(parts[1].IndexOf('_') + 1);
            }

            _parent.WriteLine($"if ({lastItemVar} != null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine($"{groupVarName} = {lastItemVar};");
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
                        _parent.WriteLine($"var {varName} = ExpectToken(GeneratedTokenType.{ruleRef.Name.ToUpper()});");
                    }
                    else
                    {
                        var methodName = _parent.ToCSharpMethodName(ruleRef.Name);
                        _parent.WriteLine($"var {varName} = {methodName}();");
                    }
                    break;

                case StringLiteral lit:
                    var escaped = _parent.EscapeString(lit.Value);
                    _parent.WriteLine($"var {varName} = Expect(\"{escaped}\");");
                    break;

                default:
                    // For complex atoms (Optional, OneOrMore, etc.), generate normally
                    // Use a dummy label prefix since we don't use goto in groups
                    var itemGen = new ItemCodeGenerator(_parent, item, varName, "group_dummy");
                    // This is recursive but safe since groups are relatively shallow
                    // TODO: Handle this better - maybe create a flag for "no goto" mode
                    _parent.WriteLine($"object? {varName} = null;");
                    _parent.WriteLine($"// TODO: Complex group item type: {item.Atom.GetType().Name}");
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
            _parent.WriteLine($"object? {testVar} = null;");

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
    }
}
