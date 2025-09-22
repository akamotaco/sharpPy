using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy.Parser
{
    /// <summary>
    /// Represents a PEG grammar rule
    /// </summary>
    public class PegRule
    {
        public string Name { get; set; }
        public string ReturnType { get; set; }
        public PegExpression Expression { get; set; }
        public bool IsMemoized { get; set; }

        public PegRule(string name, string returnType, PegExpression expression, bool isMemoized = false)
        {
            Name = name;
            ReturnType = returnType ?? "object";
            Expression = expression;
            IsMemoized = isMemoized;
        }

        public override string ToString()
        {
            var memo = IsMemoized ? " (memo)" : "";
            return $"{Name}[{ReturnType}]:{memo} {Expression}";
        }
    }

    /// <summary>
    /// Base class for all PEG expressions
    /// </summary>
    public abstract class PegExpression
    {
        public abstract string ToString();
    }

    /// <summary>
    /// Sequence expression (e1 e2)
    /// </summary>
    public class SequenceExpression : PegExpression
    {
        public List<PegExpression> Elements { get; }

        public SequenceExpression(List<PegExpression> elements)
        {
            Elements = elements ?? new List<PegExpression>();
        }

        public override string ToString()
        {
            return string.Join(" ", Elements.Select(e => e.ToString()));
        }
    }

    /// <summary>
    /// Choice expression (e1 | e2)
    /// </summary>
    public class ChoiceExpression : PegExpression
    {
        public List<PegExpression> Alternatives { get; }

        public ChoiceExpression(List<PegExpression> alternatives)
        {
            Alternatives = alternatives ?? new List<PegExpression>();
        }

        public override string ToString()
        {
            return string.Join(" | ", Alternatives.Select(a => a.ToString()));
        }
    }

    /// <summary>
    /// Optional expression [e] or e?
    /// </summary>
    public class OptionalExpression : PegExpression
    {
        public PegExpression Expression { get; }

        public OptionalExpression(PegExpression expression)
        {
            Expression = expression;
        }

        public override string ToString()
        {
            return $"[{Expression}]";
        }
    }

    /// <summary>
    /// Zero or more expression e*
    /// </summary>
    public class ZeroOrMoreExpression : PegExpression
    {
        public PegExpression Expression { get; }

        public ZeroOrMoreExpression(PegExpression expression)
        {
            Expression = expression;
        }

        public override string ToString()
        {
            return $"{Expression}*";
        }
    }

    /// <summary>
    /// One or more expression e+
    /// </summary>
    public class OneOrMoreExpression : PegExpression
    {
        public PegExpression Expression { get; }

        public OneOrMoreExpression(PegExpression expression)
        {
            Expression = expression;
        }

        public override string ToString()
        {
            return $"{Expression}+";
        }
    }

    /// <summary>
    /// Separated list expression s.e+
    /// </summary>
    public class SeparatedListExpression : PegExpression
    {
        public PegExpression Separator { get; }
        public PegExpression Element { get; }

        public SeparatedListExpression(PegExpression separator, PegExpression element)
        {
            Separator = separator;
            Element = element;
        }

        public override string ToString()
        {
            return $"{Separator}.{Element}+";
        }
    }

    /// <summary>
    /// Positive lookahead &e
    /// </summary>
    public class PositiveLookaheadExpression : PegExpression
    {
        public PegExpression Expression { get; }

        public PositiveLookaheadExpression(PegExpression expression)
        {
            Expression = expression;
        }

        public override string ToString()
        {
            return $"&{Expression}";
        }
    }

    /// <summary>
    /// Negative lookahead !e
    /// </summary>
    public class NegativeLookaheadExpression : PegExpression
    {
        public PegExpression Expression { get; }

        public NegativeLookaheadExpression(PegExpression expression)
        {
            Expression = expression;
        }

        public override string ToString()
        {
            return $"!{Expression}";
        }
    }

    /// <summary>
    /// Group expression (e)
    /// </summary>
    public class GroupExpression : PegExpression
    {
        public PegExpression Expression { get; }

        public GroupExpression(PegExpression expression)
        {
            Expression = expression;
        }

        public override string ToString()
        {
            return $"({Expression})";
        }
    }

    /// <summary>
    /// Terminal expression (token or literal)
    /// </summary>
    public class TerminalExpression : PegExpression
    {
        public string Value { get; }
        public bool IsLiteral { get; }

        public TerminalExpression(string value, bool isLiteral = false)
        {
            Value = value;
            IsLiteral = isLiteral;
        }

        public override string ToString()
        {
            return IsLiteral ? $"'{Value}'" : Value;
        }
    }

    /// <summary>
    /// Named expression with variable binding (a=expression)
    /// </summary>
    public class NamedExpression : PegExpression
    {
        public string Name { get; }
        public PegExpression Expression { get; }

        public NamedExpression(string name, PegExpression expression)
        {
            Name = name;
            Expression = expression;
        }

        public override string ToString()
        {
            return $"{Name}={Expression}";
        }
    }

    /// <summary>
    /// Action expression with C# code { code }
    /// </summary>
    public class ActionExpression : PegExpression
    {
        public string Code { get; }

        public ActionExpression(string code)
        {
            Code = code;
        }

        public override string ToString()
        {
            return $"{{ {Code} }}";
        }
    }

    /// <summary>
    /// Cut expression ~ (commit to current alternative)
    /// </summary>
    public class CutExpression : PegExpression
    {
        public override string ToString()
        {
            return "~";
        }
    }

    /// <summary>
    /// Rule reference expression
    /// </summary>
    public class RuleExpression : PegExpression
    {
        public string RuleName { get; }

        public RuleExpression(string ruleName)
        {
            RuleName = ruleName;
        }

        public override string ToString()
        {
            return RuleName;
        }
    }

    /// <summary>
    /// Complete PEG grammar
    /// </summary>
    public class PegGrammar
    {
        public List<PegRule> Rules { get; }
        public Dictionary<string, PegRule> RuleMap { get; }

        public PegGrammar(List<PegRule> rules)
        {
            Rules = rules ?? new List<PegRule>();
            RuleMap = Rules.ToDictionary(r => r.Name, r => r);
        }

        public PegRule GetRule(string name)
        {
            return RuleMap.TryGetValue(name, out var rule) ? rule : null;
        }

        public bool HasRule(string name)
        {
            return RuleMap.ContainsKey(name);
        }

        public override string ToString()
        {
            return string.Join("\n\n", Rules.Select(r => r.ToString()));
        }
    }
}