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
            _parent.WriteLine("goto alternative_failed;");
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
                _parent.WriteLine($"var {_varName} = ExpectToken(GeneratedTokenType.{ruleRef.Name.ToUpper()});");
                _parent.WriteLine($"if ({_varName} == null)");
                _parent.WriteLine("{");
                _parent.Indent();
                _parent.WriteLine("_position = _mark;");
                _parent.WriteLine("_res = null;");
                _parent.WriteLine("goto alternative_failed;");
                _parent.Dedent();
                _parent.WriteLine("}");
            }
            else
            {
                // Rule reference - call method
                var methodName = _parent.ToCSharpMethodName(ruleRef.Name);
                _parent.WriteLine($"// Call rule: {ruleRef.Name}");
                _parent.WriteLine($"var {_varName} = {methodName}();");
                _parent.WriteLine($"if ({_varName} == null)");
                _parent.WriteLine("{");
                _parent.Indent();
                _parent.WriteLine("_position = _mark;");
                _parent.WriteLine("_res = null;");
                _parent.WriteLine("goto alternative_failed;");
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
            _parent.WriteLine($"goto alternative_failed;");
            _parent.Dedent();
            _parent.WriteLine("}");
        }

        private void GenerateGroup(Group grp)
        {
            // CPython 3.12 pattern: Group tries each alternative until one succeeds
            // Unlike rule alternatives, group must handle goto alternative_failed internally
            _parent.WriteLine($"// Group: ({string.Join(" | ", grp.Alternatives.Select(a => a.ToString()))})");
            _parent.WriteLine($"object? {_varName} = null;");
            _parent.WriteLine($"int _group_mark_{_varName} = _position;");

            for (int i = 0; i < grp.Alternatives.Count; i++)
            {
                var alt = grp.Alternatives[i];
                var altVarName = $"_group_alt{i}_{_varName}";

                _parent.WriteLine($"// Try group alternative {i + 1}: {alt}");
                _parent.WriteLine("{");
                _parent.Indent();
                _parent.WriteLine($"_position = _group_mark_{_varName};");

                // Generate code for all items in this alternative - each item may use "goto alternative_failed"
                // which will jump to the end of current rule alternative, so we need to catch that
                // For now, we'll generate items and check if the last item succeeded
                bool hasItems = alt.Items.Count > 0;
                string lastItemVarName = null;

                for (int j = 0; j < alt.Items.Count; j++)
                {
                    var item = alt.Items[j];
                    var itemVarName = $"{altVarName}_item{j}";

                    // Track the last item that actually generates a variable
                    // (lookaheads don't generate variables but still count as items)
                    if (!(item.Atom is PositiveLookahead) && !(item.Atom is NegativeLookahead))
                    {
                        lastItemVarName = itemVarName;
                    }

                    // Items use goto alternative_failed which exits the entire rule alternative
                    // In a group context, we need to catch this and try next group alternative
                    // Solution: Each generated item code already does: if (item == null) goto alternative_failed
                    // So if we reach here, the item succeeded
                    var itemGen = new ItemCodeGenerator(_parent, item, itemVarName, _labelPrefix);
                    itemGen.Generate();
                }

                // If we reach here, all items succeeded
                if (hasItems && lastItemVarName != null)
                {
                    _parent.WriteLine($"// Group alternative {i + 1} succeeded");
                    _parent.WriteLine($"{_varName} = {lastItemVarName};");
                    _parent.WriteLine($"goto group_success_{_varName};");
                }

                _parent.Dedent();
                _parent.WriteLine("}");
            }

            // All alternatives tried, none succeeded
            _parent.WriteLine($"// All group alternatives failed");
            _parent.WriteLine($"_position = _mark;");
            _parent.WriteLine($"_res = null;");
            _parent.WriteLine($"goto alternative_failed;");

            _parent.WriteLine($"group_success_{_varName}: ; // Group succeeded");
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
            // CPython 3.12 pattern: Fail if matches
            var markVar = $"_lookahead_mark_{_lookaheadCounter++}";
            _parent.WriteLine($"// Negative lookahead - fail if matches");
            _parent.WriteLine($"int {markVar} = _position;");
            _parent.WriteLine($"// TODO: Parse lookahead content for {nla.Expression?.GetType().Name}, fail if succeeds");
            _parent.WriteLine($"_position = {markVar}; // Restore position");
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
