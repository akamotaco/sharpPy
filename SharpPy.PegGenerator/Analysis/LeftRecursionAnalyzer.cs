using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.PegGenerator.Grammar;

namespace SharpPy.PegGenerator.Analysis
{
    /// <summary>
    /// CPython 3.12: Strongly Connected Components utilities (sccutils.py)
    /// Adapted from mypy (mypy/build.py) under the MIT license
    /// </summary>
    public static class SccUtils
    {
        /// <summary>
        /// Compute Strongly Connected Components of a directed graph (Tarjan's algorithm)
        /// CPython: pegen/sccutils.py:strongly_connected_components
        /// </summary>
        public static IEnumerable<HashSet<string>> StronglyConnectedComponents(
            HashSet<string> vertices,
            Dictionary<string, HashSet<string>> edges)
        {
            var identified = new HashSet<string>();
            var stack = new List<string>();
            var index = new Dictionary<string, int>();
            var boundaries = new List<int>();

            IEnumerable<HashSet<string>> Dfs(string v)
            {
                index[v] = stack.Count;
                stack.Add(v);
                boundaries.Add(index[v]);

                foreach (var w in edges[v])
                {
                    if (!index.ContainsKey(w))
                    {
                        foreach (var scc in Dfs(w))
                        {
                            yield return scc;
                        }
                    }
                    else if (!identified.Contains(w))
                    {
                        while (index[w] < boundaries[boundaries.Count - 1])
                        {
                            boundaries.RemoveAt(boundaries.Count - 1);
                        }
                    }
                }

                if (boundaries[boundaries.Count - 1] == index[v])
                {
                    boundaries.RemoveAt(boundaries.Count - 1);
                    var scc = new HashSet<string>(stack.Skip(index[v]));
                    stack.RemoveRange(index[v], stack.Count - index[v]);
                    identified.UnionWith(scc);
                    yield return scc;
                }
            }

            foreach (var v in vertices)
            {
                if (!index.ContainsKey(v))
                {
                    foreach (var scc in Dfs(v))
                    {
                        yield return scc;
                    }
                }
            }
        }

        /// <summary>
        /// Find cycles in SCC emanating from start
        /// CPython: pegen/sccutils.py:find_cycles_in_scc
        /// </summary>
        public static IEnumerable<List<string>> FindCyclesInScc(
            Dictionary<string, HashSet<string>> graph,
            HashSet<string> scc,
            string start)
        {
            // Basic input checks
            if (!scc.Contains(start))
            {
                throw new ArgumentException($"Start node {start} not in SCC");
            }

            // Reduce the graph to nodes in the SCC
            var reducedGraph = new Dictionary<string, HashSet<string>>();
            foreach (var kvp in graph)
            {
                if (scc.Contains(kvp.Key))
                {
                    reducedGraph[kvp.Key] = new HashSet<string>(kvp.Value.Where(dst => scc.Contains(dst)));
                }
            }

            if (!reducedGraph.ContainsKey(start))
            {
                throw new ArgumentException($"Start node {start} has no outgoing edges in SCC");
            }

            // Recursive helper that yields cycles
            IEnumerable<List<string>> Dfs(string node, List<string> path)
            {
                if (path.Contains(node))
                {
                    var result = new List<string>(path);
                    result.Add(node);
                    yield return result;
                    yield break;
                }

                var newPath = new List<string>(path);
                newPath.Add(node);

                foreach (var child in reducedGraph[node])
                {
                    foreach (var cycle in Dfs(child, newPath))
                    {
                        yield return cycle;
                    }
                }
            }

            foreach (var cycle in Dfs(start, new List<string>()))
            {
                yield return cycle;
            }
        }
    }

    /// <summary>
    /// CPython 3.12: NullableVisitor (parser_generator.py:221-285)
    /// Compute which rules in a grammar are nullable (can match empty string)
    /// </summary>
    public class NullableVisitor
    {
        private readonly Dictionary<string, Rule> _rules;
        private readonly HashSet<object> _visited = new HashSet<object>();
        private readonly HashSet<object> _nullables = new HashSet<object>();

        public HashSet<object> Nullables => _nullables;

        public NullableVisitor(Dictionary<string, Rule> rules)
        {
            _rules = rules;
        }

        public bool VisitRule(Rule rule)
        {
            if (_visited.Contains(rule))
                return false;
            _visited.Add(rule);

            if (VisitAlternatives(rule.Alternatives))
            {
                _nullables.Add(rule);
            }
            return _nullables.Contains(rule);
        }

        private bool VisitAlternatives(List<Alternative> alternatives)
        {
            // Rhs is nullable if any alternative is nullable
            foreach (var alt in alternatives)
            {
                if (VisitAlternative(alt))
                    return true;
            }
            return false;
        }

        private bool VisitAlternative(Alternative alt)
        {
            // Alternative is nullable if all items are nullable
            foreach (var item in alt.Items)
            {
                if (!VisitItem(item))
                    return false;
            }
            return true;
        }

        private bool VisitItem(Item item)
        {
            if (item.Atom == null)
                return false;

            return VisitAtom(item.Atom);
        }

        private bool VisitAtom(Atom atom)
        {
            switch (atom)
            {
                case Optional _:
                    return true; // ? is always nullable

                case ZeroOrMore _:
                    return true; // * is always nullable

                case OneOrMore _:
                    return false; // + is never nullable

                case Gather _:
                    return false; // Gather is never nullable

                case Grammar.Group group:
                    return VisitAlternatives(group.Alternatives);

                case RuleRef ruleRef:
                    if (_rules.ContainsKey(ruleRef.Name))
                    {
                        return VisitRule(_rules[ruleRef.Name]);
                    }
                    // Token or unknown; never empty
                    return false;

                case StringLiteral strLit:
                    // Empty string '' is considered nullable
                    return string.IsNullOrEmpty(strLit.Value);

                case PositiveLookahead _:
                case NegativeLookahead _:
                    return true; // Lookaheads don't consume input

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// CPython 3.12: InitialNamesVisitor (parser_generator.py:298-334)
    /// Compute which rule names can be invoked at the initial position of each rule
    /// </summary>
    public class InitialNamesVisitor
    {
        private readonly Dictionary<string, Rule> _rules;
        private readonly HashSet<object> _nullables;

        public InitialNamesVisitor(Dictionary<string, Rule> rules, HashSet<object> nullables)
        {
            _rules = rules;
            _nullables = nullables;
        }

        public HashSet<string> VisitRule(Rule rule)
        {
            return VisitAlternatives(rule.Alternatives);
        }

        private HashSet<string> VisitAlternatives(List<Alternative> alternatives)
        {
            var names = new HashSet<string>();
            foreach (var alt in alternatives)
            {
                names.UnionWith(VisitAlternative(alt));
            }
            return names;
        }

        private HashSet<string> VisitAlternative(Alternative alt)
        {
            var names = new HashSet<string>();
            foreach (var item in alt.Items)
            {
                names.UnionWith(VisitItem(item));
                // Stop if this item is not nullable
                if (!_nullables.Contains(item))
                    break;
            }
            return names;
        }

        private HashSet<string> VisitItem(Item item)
        {
            if (item.Atom == null)
                return new HashSet<string>();

            return VisitAtom(item.Atom);
        }

        private HashSet<string> VisitAtom(Atom atom)
        {
            switch (atom)
            {
                case RuleRef ruleRef:
                    return new HashSet<string> { ruleRef.Name };

                case Grammar.Group group:
                    return VisitAlternatives(group.Alternatives);

                case PositiveLookahead _:
                case NegativeLookahead _:
                    return new HashSet<string>(); // Lookaheads don't invoke rules

                default:
                    return new HashSet<string>();
            }
        }
    }

    /// <summary>
    /// CPython 3.12: compute_left_recursives (parser_generator.py:337-364)
    /// Analyze grammar for left-recursive rules and identify leaders
    /// </summary>
    public static class LeftRecursionAnalyzer
    {
        /// <summary>
        /// CPython: compute_nullables
        /// </summary>
        public static HashSet<object> ComputeNullables(Dictionary<string, Rule> rules)
        {
            var visitor = new NullableVisitor(rules);
            foreach (var rule in rules.Values)
            {
                visitor.VisitRule(rule);
            }
            return visitor.Nullables;
        }

        /// <summary>
        /// CPython: make_first_graph
        /// Compute the graph of left-invocations (which rules can invoke which others at initial position)
        /// </summary>
        public static Dictionary<string, HashSet<string>> MakeFirstGraph(Dictionary<string, Rule> rules)
        {
            var nullables = ComputeNullables(rules);
            var visitor = new InitialNamesVisitor(rules, nullables);
            var graph = new Dictionary<string, HashSet<string>>();
            var vertices = new HashSet<string>();

            foreach (var kvp in rules)
            {
                var names = visitor.VisitRule(kvp.Value);
                graph[kvp.Key] = names;
                vertices.UnionWith(names);
            }

            // Ensure all vertices are in the graph
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
        /// CPython: compute_left_recursives
        /// Mark left-recursive rules and identify leaders
        /// </summary>
        public static void ComputeLeftRecursives(Dictionary<string, Rule> rules)
        {
            var graph = MakeFirstGraph(rules);
            var vertices = new HashSet<string>(graph.Keys);
            var sccs = SccUtils.StronglyConnectedComponents(vertices, graph).ToList();

            foreach (var scc in sccs)
            {
                if (scc.Count > 1)
                {
                    // Multiple rules in SCC - all are left-recursive
                    foreach (var name in scc)
                    {
                        if (rules.ContainsKey(name))
                        {
                            rules[name].IsLeftRecursive = true;
                        }
                    }

                    // Try to find a leader such that all cycles go through it
                    var leaders = new HashSet<string>(scc);
                    foreach (var start in scc)
                    {
                        foreach (var cycle in SccUtils.FindCyclesInScc(graph, scc, start))
                        {
                            // Remove non-cycle members from leaders
                            leaders.ExceptWith(scc.Except(new HashSet<string>(cycle)));

                            if (leaders.Count == 0)
                            {
                                throw new InvalidOperationException(
                                    $"SCC {string.Join(", ", scc)} has no leadership candidate (no element is included in all cycles)");
                            }
                        }
                    }

                    // Pick an arbitrary leader from the candidates
                    var leader = leaders.Min();
                    if (leader != null && rules.ContainsKey(leader))
                    {
                        rules[leader].IsLeader = true;
                    }
                }
                else
                {
                    // Single rule in SCC - check if it's self-recursive
                    var name = scc.First();
                    if (graph.ContainsKey(name) && graph[name].Contains(name))
                    {
                        if (rules.ContainsKey(name))
                        {
                            rules[name].IsLeftRecursive = true;
                            rules[name].IsLeader = true;
                        }
                    }
                }
            }
        }
    }
}
