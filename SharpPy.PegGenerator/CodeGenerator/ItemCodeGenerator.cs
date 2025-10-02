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

            // CPython 3.12 pattern: Expect exact token match
            _parent.WriteLine($"// Expect '{escaped}'");
            _parent.WriteLine($"if (!Expect(\"{escaped}\"))");
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
            var methodName = _parent.ToCSharpMethodName(ruleRef.Name);

            // CPython 3.12 pattern: Call rule method and check result
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

        private void GenerateOptional(Optional opt)
        {
            // CPython 3.12 pattern: Optional always succeeds, result may be null
            _parent.WriteLine($"// Optional");
            _parent.WriteLine($"// TODO: Implement optional parsing for {opt.Expression?.GetType().Name}");
            _parent.WriteLine($"object? {_varName} = null; // Optional always succeeds");
        }

        private void GenerateZeroOrMore(ZeroOrMore zm)
        {
            // CPython 3.12 pattern: Loop until parsing fails
            _parent.WriteLine($"// Zero or more - always succeeds");
            _parent.WriteLine($"// TODO: Implement zero or more loop for {zm.Expression?.GetType().Name}");
            _parent.WriteLine($"var {_varName} = new System.Collections.Generic.List<object?>(); // ZeroOrMore");
        }

        private void GenerateOneOrMore(OneOrMore om)
        {
            // CPython 3.12 pattern: Must match at least once
            _parent.WriteLine($"// One or more - must match at least once");
            _parent.WriteLine($"// TODO: Implement one or more loop for {om.Expression?.GetType().Name}");
            _parent.WriteLine($"var {_varName} = new System.Collections.Generic.List<object?>(); // OneOrMore");
            _parent.WriteLine($"// NOTE: Must fail if list is empty after parsing");
        }

        private void GenerateGroup(Group grp)
        {
            // CPython 3.12 pattern: Group is just for precedence
            _parent.WriteLine($"// Group (precedence)");
            _parent.WriteLine($"// TODO: Implement group alternatives");
            _parent.WriteLine($"object? {_varName} = null; // Group");
        }

        private void GeneratePositiveLookahead(PositiveLookahead pla)
        {
            // CPython 3.12 pattern: Check without consuming
            _parent.WriteLine($"// Positive lookahead - check without consuming");
            _parent.WriteLine($"int _lookahead_mark = _position;");
            _parent.WriteLine($"// TODO: Parse lookahead content for {pla.Expression?.GetType().Name}");
            _parent.WriteLine($"_position = _lookahead_mark; // Restore position");
        }

        private void GenerateNegativeLookahead(NegativeLookahead nla)
        {
            // CPython 3.12 pattern: Fail if matches
            _parent.WriteLine($"// Negative lookahead - fail if matches");
            _parent.WriteLine($"int _lookahead_mark = _position;");
            _parent.WriteLine($"// TODO: Parse lookahead content for {nla.Expression?.GetType().Name}, fail if succeeds");
            _parent.WriteLine($"_position = _lookahead_mark; // Restore position");
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
