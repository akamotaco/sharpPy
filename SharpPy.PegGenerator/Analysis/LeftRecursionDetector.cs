using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.PegGenerator.DataStructures;

namespace SharpPy.PegGenerator.Analysis;

/// <summary>
/// Detects left-recursive rules using SCC (Strongly Connected Components) algorithm
/// CPython 3.12: Tools/peg_generator/pegen/parser_generator.py - compute_left_recursives()
/// </summary>
public class LeftRecursionDetector
{
    private readonly PegRule[] _rules;
    private readonly Dictionary<string, PegRule> _ruleMap;

    public LeftRecursionDetector(PegRule[] rules)
    {
        _rules = rules;
        _ruleMap = rules.ToDictionary(r => r.Name);
    }

    /// <summary>
    /// Detect all left-recursive rules
    /// CPython: compute_left_recursives()
    /// </summary>
    public HashSet<string> DetectLeftRecursiveRules()
    {
        // 1. Build first-invocation graph
        var graph = MakeFirstGraph();

        // 2. Find strongly connected components
        var sccs = FindStronglyConnectedComponents(graph);

        // 3. Mark left-recursive rules
        var leftRecursiveRules = new HashSet<string>();

        foreach (var scc in sccs)
        {
            if (scc.Count > 1)
            {
                // Multiple rules that call each other
                foreach (var name in scc)
                {
                    leftRecursiveRules.Add(name);
                }
            }
            else
            {
                // Single rule - check if it calls itself
                var name = scc.First();
                if (graph[name].Contains(name))
                {
                    leftRecursiveRules.Add(name);
                }
            }
        }

        return leftRecursiveRules;
    }

    /// <summary>
    /// Build graph of which rules can be called at the initial position of each rule
    /// CPython: make_first_graph()
    /// </summary>
    private Dictionary<string, HashSet<string>> MakeFirstGraph()
    {
        var graph = new Dictionary<string, HashSet<string>>();
        var vertices = new HashSet<string>();

        // For each rule, find which rules it may call at its initial position
        foreach (var rule in _rules)
        {
            var initialNames = GetInitialNames(rule);
            graph[rule.Name] = initialNames;
            vertices.UnionWith(initialNames);

            // Debug: print star-related rules
            if (rule.Name.Contains("star") || rule.Name.Contains("target"))
            {
                Console.WriteLine($"[FIRST-GRAPH] {rule.Name} → {{ {string.Join(", ", initialNames.OrderBy(n => n))} }}");
            }
        }

        // Ensure all vertices are in the graph (even if they have no outgoing edges)
        foreach (var vertex in vertices)
        {
            if (!graph.ContainsKey(vertex))
            {
                graph[vertex] = new HashSet<string>();
            }
        }

        return graph;
    }

    /// <summary>
    /// Get the set of rule names that may be called at the initial position
    /// CPython: InitialNamesVisitor
    /// </summary>
    private HashSet<string> GetInitialNames(PegRule rule)
    {
        var names = new HashSet<string>();

        // Check each alternative
        foreach (var alt in rule.Alternatives)
        {
            // Collect names from items until we hit a non-nullable item
            foreach (var item in alt.Items)
            {
                names.UnionWith(GetInitialNamesFromAtom(item.Atom));

                // If this item is not nullable, stop (subsequent items won't be reached)
                if (!IsNullable(item.Atom))
                {
                    break;
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Get initial names from an atom
    /// </summary>
    private HashSet<string> GetInitialNamesFromAtom(Atom atom)
    {
        var names = new HashSet<string>();

        switch (atom)
        {
            case RuleRef ruleRef:
                // Direct rule reference
                names.Add(ruleRef.Name);
                break;

            case Group group:
                // Group: collect from all alternatives
                foreach (var alt in group.Alternatives)
                {
                    foreach (var item in alt.Items)
                    {
                        names.UnionWith(GetInitialNamesFromAtom(item.Atom));
                        if (!IsNullable(item.Atom))
                        {
                            break;
                        }
                    }
                }
                break;

            case Optional opt:
                // Optional: collect from inner, but it's nullable so don't stop
                names.UnionWith(GetInitialNamesFromAtom(opt.Inner));
                break;

            case OneOrMore om:
                // OneOrMore: collect from inner
                names.UnionWith(GetInitialNamesFromAtom(om.Inner));
                break;

            case ZeroOrMore zm:
                // ZeroOrMore: collect from inner (but it's nullable)
                names.UnionWith(GetInitialNamesFromAtom(zm.Inner));
                break;

            case Gather gather:
                // Gather: first item is the element, not the separator
                names.UnionWith(GetInitialNamesFromAtom(gather.Item));
                break;

            case PositiveLookahead:
            case NegativeLookahead:
            case Keyword:
            case Token:
                // These don't reference rules
                break;
        }

        return names;
    }

    /// <summary>
    /// Check if an atom is nullable (can match empty input)
    /// CPython: compute_nullables()
    /// </summary>
    private bool IsNullable(Atom atom)
    {
        return atom switch
        {
            Optional => true,
            ZeroOrMore => true,
            Gather g => !g.IsPlus,  // sep.item* is nullable, sep.item+ is not
            PositiveLookahead => true,  // Lookahead doesn't consume
            NegativeLookahead => true,  // Lookahead doesn't consume
            Group group => group.Alternatives.Any(alt => alt.Items.All(item => IsNullable(item.Atom))),
            RuleRef ruleRef => IsRuleNullable(ruleRef.Name),
            _ => false
        };
    }

    /// <summary>
    /// Check if a rule is nullable (using memoization to handle recursion)
    /// </summary>
    private readonly Dictionary<string, bool> _nullableCache = new();
    private readonly HashSet<string> _nullableInProgress = new();

    private bool IsRuleNullable(string ruleName)
    {
        if (_nullableCache.TryGetValue(ruleName, out var cached))
        {
            return cached;
        }

        // Prevent infinite recursion
        if (_nullableInProgress.Contains(ruleName))
        {
            return false;
        }

        _nullableInProgress.Add(ruleName);

        try
        {
            if (!_ruleMap.TryGetValue(ruleName, out var rule))
            {
                return false;
            }

            // A rule is nullable if any of its alternatives is nullable
            var isNullable = rule.Alternatives.Any(alt =>
                alt.Items.All(item => IsNullable(item.Atom))
            );

            _nullableCache[ruleName] = isNullable;
            return isNullable;
        }
        finally
        {
            _nullableInProgress.Remove(ruleName);
        }
    }

    /// <summary>
    /// Find strongly connected components using Tarjan's algorithm
    /// CPython uses sccutils.strongly_connected_components()
    /// </summary>
    private List<HashSet<string>> FindStronglyConnectedComponents(Dictionary<string, HashSet<string>> graph)
    {
        var sccs = new List<HashSet<string>>();
        var index = 0;
        var stack = new Stack<string>();
        var indices = new Dictionary<string, int>();
        var lowlinks = new Dictionary<string, int>();
        var onStack = new HashSet<string>();

        void StrongConnect(string v)
        {
            indices[v] = index;
            lowlinks[v] = index;
            index++;
            stack.Push(v);
            onStack.Add(v);

            // Consider successors of v
            if (graph.TryGetValue(v, out var successors))
            {
                foreach (var w in successors)
                {
                    if (!indices.ContainsKey(w))
                    {
                        // Successor w has not yet been visited; recurse on it
                        StrongConnect(w);
                        lowlinks[v] = Math.Min(lowlinks[v], lowlinks[w]);
                    }
                    else if (onStack.Contains(w))
                    {
                        // Successor w is in stack and hence in the current SCC
                        lowlinks[v] = Math.Min(lowlinks[v], indices[w]);
                    }
                }
            }

            // If v is a root node, pop the stack and generate an SCC
            if (lowlinks[v] == indices[v])
            {
                var scc = new HashSet<string>();
                string w;
                do
                {
                    w = stack.Pop();
                    onStack.Remove(w);
                    scc.Add(w);
                } while (w != v);

                sccs.Add(scc);
            }
        }

        // Run Tarjan's algorithm on all nodes
        foreach (var v in graph.Keys)
        {
            if (!indices.ContainsKey(v))
            {
                StrongConnect(v);
            }
        }

        return sccs;
    }
}
