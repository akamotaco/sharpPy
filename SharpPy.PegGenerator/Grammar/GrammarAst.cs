using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy.PegGenerator.Grammar
{
    /// <summary>
    /// Base class for all grammar AST nodes
    /// </summary>
    public abstract class GrammarNode
    {
        public abstract override string ToString();
    }

    /// <summary>
    /// Represents a complete grammar file
    /// </summary>
    public class Grammar : GrammarNode
    {
        public List<Rule> Rules { get; set; } = new();
        public string? Trailer { get; set; }

        public override string ToString()
        {
            var rules = string.Join("\n", Rules.Select(r => r.ToString()));
            return $"Grammar:\n{rules}";
        }
    }

    /// <summary>
    /// Represents a single grammar rule
    /// </summary>
    public class Rule : GrammarNode
    {
        public string Name { get; set; } = "";
        public string? ReturnType { get; set; }
        public List<Alternative> Alternatives { get; set; } = new();
        public bool IsMemoized { get; set; } = false; // Default to NOT memoized (CPython 3.12 behavior)

        // CPython 3.12: Left-recursion detection (compute_left_recursives in parser_generator.py)
        public bool IsLeftRecursive { get; set; } = false;
        public bool IsLeader { get; set; } = false; // Leader of a left-recursive SCC (Strongly Connected Component)

        public override string ToString()
        {
            var type = ReturnType != null ? $"[{ReturnType}]" : "";
            var alts = string.Join(" | ", Alternatives.Select(a => a.ToString()));
            return $"{Name}{type}: {alts}";
        }
    }

    /// <summary>
    /// Represents one alternative in a rule
    /// </summary>
    public class Alternative : GrammarNode
    {
        public List<Item> Items { get; set; } = new();
        public string? Action { get; set; }

        public override string ToString()
        {
            var items = string.Join(" ", Items.Select(i => i.ToString()));
            var action = Action != null ? $" {{ {Action} }}" : "";
            return $"{items}{action}";
        }
    }

    /// <summary>
    /// Represents an item in an alternative
    /// </summary>
    public class Item : GrammarNode
    {
        public string? Name { get; set; }  // Optional variable name
        public string? TypeAnnotation { get; set; }  // Optional type annotation: a[asdl_expr_seq*]
        public Atom Atom { get; set; } = null!;

        public override string ToString()
        {
            var name = Name != null ? $"{Name}=" : "";
            var type = TypeAnnotation != null ? $"[{TypeAnnotation}]" : "";
            return $"{name}{type}{Atom}";
        }
    }

    /// <summary>
    /// Base class for atomic expressions
    /// </summary>
    public abstract class Atom : GrammarNode
    {
    }

    /// <summary>
    /// Represents a rule reference
    /// </summary>
    public class RuleRef : Atom
    {
        public string Name { get; set; } = "";

        public override string ToString() => Name;
    }

    /// <summary>
    /// Represents a string literal
    /// </summary>
    public class StringLiteral : Atom
    {
        public string Value { get; set; } = "";

        public override string ToString() => $"'{Value}'";
    }

    /// <summary>
    /// Represents a group (parentheses)
    /// </summary>
    public class Group : Atom
    {
        public List<Alternative> Alternatives { get; set; } = new();

        public override string ToString()
        {
            var alts = string.Join(" | ", Alternatives.Select(a => a.ToString()));
            return $"({alts})";
        }
    }

    /// <summary>
    /// Represents an optional expression [expr]
    /// </summary>
    public class Optional : Atom
    {
        public Atom Expression { get; set; } = null!;

        public override string ToString() => $"[{Expression}]";
    }

    /// <summary>
    /// Represents zero or more repetition expr*
    /// </summary>
    public class ZeroOrMore : Atom
    {
        public Atom Expression { get; set; } = null!;

        public override string ToString() => $"{Expression}*";
    }

    /// <summary>
    /// Represents one or more repetition expr+
    /// </summary>
    public class OneOrMore : Atom
    {
        public Atom Expression { get; set; } = null!;

        public override string ToString() => $"{Expression}+";
    }

    /// <summary>
    /// Represents positive lookahead &expr
    /// </summary>
    public class PositiveLookahead : Atom
    {
        public Atom Expression { get; set; } = null!;

        public override string ToString() => $"&{Expression}";
    }

    /// <summary>
    /// Represents negative lookahead !expr
    /// </summary>
    public class NegativeLookahead : Atom
    {
        public Atom Expression { get; set; } = null!;

        public override string ToString() => $"!{Expression}";
    }

    /// <summary>
    /// Represents a cut operator ~expr
    /// </summary>
    public class Cut : Atom
    {
        public Atom Expression { get; set; } = null!;

        public override string ToString() => $"~{Expression}";
    }

    /// <summary>
    /// Represents forced token/result &&expr
    /// CPython 3.12: Forced tokens must match or raise syntax error immediately
    /// </summary>
    public class Forced : Atom
    {
        public Atom Node { get; set; } = null!;

        public override string ToString() => $"&&{Node}";
    }

    /// <summary>
    /// Gather pattern: separator.item+ or separator.item*
    /// CPython 3.12: Used for separated lists like ','.expression+
    /// </summary>
    public class Gather : Atom
    {
        public Atom Separator { get; set; } = null!;  // e.g., StringLiteral(",")
        public Atom Item { get; set; } = null!;        // e.g., RuleRef("expression")
        public bool IsOneOrMore { get; set; }          // true for +, false for *

        public override string ToString() => $"{Separator}.{Item}{(IsOneOrMore ? "+" : "*")}";
    }
}