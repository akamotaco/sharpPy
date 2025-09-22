using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy.Parser
{
    /// <summary>
    /// Represents a left recursion head for Warth et al.'s left recursion algorithm
    /// </summary>
    public class LeftRecursionHead
    {
        public string Rule { get; set; }
        public HashSet<string> InvolvedSet { get; set; }
        public HashSet<string> EvalSet { get; set; }

        public LeftRecursionHead(string rule)
        {
            Rule = rule;
            InvolvedSet = new HashSet<string>();
            EvalSet = new HashSet<string>();
        }
    }

    /// <summary>
    /// Represents a left recursion entry for tracking recursive calls
    /// </summary>
    public class LeftRecursionEntry
    {
        public string Rule { get; set; }
        public LeftRecursionHead Head { get; set; }

        public LeftRecursionEntry(string rule, LeftRecursionHead head)
        {
            Rule = rule;
            Head = head;
        }
    }

    /// <summary>
    /// Memo entry for caching parse results
    /// </summary>
    public class MemoEntry<T>
    {
        public T Result { get; set; }
        public int Position { get; set; }
        public LeftRecursionEntry LeftRecursion { get; set; }

        public MemoEntry(T result, int position, LeftRecursionEntry leftRecursion = null)
        {
            Result = result;
            Position = position;
            LeftRecursion = leftRecursion;
        }
    }

    /// <summary>
    /// Enhanced memoization system with left recursion support
    /// Based on Warth, Douglass, and Millstein's algorithm
    /// </summary>
    public class PegMemoizer
    {
        private readonly Dictionary<(int position, string rule), MemoEntry<object>> _memo;
        private readonly Stack<LeftRecursionEntry> _leftRecursionStack;
        private readonly Dictionary<(int position, string rule), LeftRecursionHead> _heads;

        public PegMemoizer()
        {
            _memo = new Dictionary<(int, string), MemoEntry<object>>();
            _leftRecursionStack = new Stack<LeftRecursionEntry>();
            _heads = new Dictionary<(int, string), LeftRecursionHead>();
        }

        /// <summary>
        /// Apply rule with memoization and left recursion support
        /// </summary>
        public T ApplyRule<T>(string ruleName, int position, Func<T> ruleFunc, Func<int> getPosition, Action<int> setPosition)
        {
            var key = (position, ruleName);

            // Check memo
            if (_memo.TryGetValue(key, out var memoEntry))
            {
                setPosition(memoEntry.Position);

                if (memoEntry.LeftRecursion != null && !memoEntry.LeftRecursion.Head.EvalSet.Contains(ruleName))
                {
                    // Left recursion detected but rule not in eval set
                    return default(T);
                }

                return (T)memoEntry.Result;
            }

            // Check for left recursion head
            if (_heads.TryGetValue(key, out var head))
            {
                if (head.EvalSet.Contains(ruleName))
                {
                    // Remove from eval set to prevent infinite recursion
                    head.EvalSet.Remove(ruleName);

                    // Execute rule
                    var result = ruleFunc();
                    var newPosition = getPosition();

                    // Store result
                    _memo[key] = new MemoEntry<object>(result, newPosition);
                    return result;
                }
                else
                {
                    // Not allowed to evaluate this rule
                    return default(T);
                }
            }

            // Setup left recursion detection
            var lr = new LeftRecursionEntry(ruleName, null);
            _leftRecursionStack.Push(lr);
            var startPosition = position;

            // Mark as left recursive
            _memo[key] = new MemoEntry<object>(default(T), position, lr);

            // Execute rule
            var ruleResult = ruleFunc();
            var resultPosition = getPosition();

            // Pop left recursion entry
            _leftRecursionStack.Pop();

            // Check if left recursion was detected
            if (lr.Head != null)
            {
                // We detected left recursion
                lr.Head = lr.Head;
                return HandleLeftRecursion<T>(ruleName, position, ruleFunc, getPosition, setPosition);
            }
            else
            {
                // No left recursion, normal case
                _memo[key] = new MemoEntry<object>(ruleResult, resultPosition);
                return ruleResult;
            }
        }

        /// <summary>
        /// Handle left recursion using seed parse technique
        /// </summary>
        private T HandleLeftRecursion<T>(string ruleName, int position, Func<T> ruleFunc, Func<int> getPosition, Action<int> setPosition)
        {
            var key = (position, ruleName);
            var head = _heads[key];

            // Start with seed (fail)
            _memo[key] = new MemoEntry<object>(default(T), position);

            T lastResult = default(T);
            int lastPosition = position;

            // Grow the seed
            while (true)
            {
                setPosition(position);
                head.EvalSet.UnionWith(head.InvolvedSet);

                var result = ruleFunc();
                var newPosition = getPosition();

                // If we didn't advance or got same result, we're done
                if (newPosition <= lastPosition || EqualityComparer<T>.Default.Equals(result, lastResult))
                {
                    break;
                }

                // Update memo with new result
                _memo[key] = new MemoEntry<object>(result, newPosition);
                lastResult = result;
                lastPosition = newPosition;
            }

            // Clean up
            _heads.Remove(key);
            setPosition(lastPosition);

            return lastResult;
        }

        /// <summary>
        /// Setup left recursion when detected
        /// </summary>
        public void SetupLeftRecursion(string ruleName, int position)
        {
            var key = (position, ruleName);

            if (_leftRecursionStack.Count == 0)
                return;

            var lr = _leftRecursionStack.Peek();

            if (lr.Head == null)
            {
                // Create new head
                lr.Head = new LeftRecursionHead(ruleName);
            }

            // Add all rules in the stack to involved set
            foreach (var stackEntry in _leftRecursionStack)
            {
                if (stackEntry.Rule == lr.Head.Rule)
                    break;

                lr.Head.InvolvedSet.Add(stackEntry.Rule);
                stackEntry.Head = lr.Head;
            }

            _heads[key] = lr.Head;
        }

        /// <summary>
        /// Clear all memoization data
        /// </summary>
        public void Clear()
        {
            _memo.Clear();
            _leftRecursionStack.Clear();
            _heads.Clear();
        }

        /// <summary>
        /// Get memo statistics for debugging
        /// </summary>
        public (int MemoEntries, int Heads, int StackDepth) GetStats()
        {
            return (_memo.Count, _heads.Count, _leftRecursionStack.Count);
        }
    }

    /// <summary>
    /// Extensions for parser classes using memoization
    /// </summary>
    public static class MemoizationExtensions
    {
        /// <summary>
        /// Wrap a rule function with memoization
        /// </summary>
        public static T WithMemo<T>(this PegMemoizer memoizer, string ruleName, int position,
            Func<T> ruleFunc, Func<int> getPosition, Action<int> setPosition)
        {
            return memoizer.ApplyRule(ruleName, position, ruleFunc, getPosition, setPosition);
        }
    }
}

namespace SharpPy.Parser
{
    /// <summary>
    /// Enhanced packrat parser base class with left recursion support
    /// </summary>
    public abstract class PackratParser
    {
        protected readonly PegMemoizer _memoizer;
        protected int _position;

        protected PackratParser()
        {
            _memoizer = new PegMemoizer();
            _position = 0;
        }

        /// <summary>
        /// Parse with memoization and left recursion handling
        /// </summary>
        protected T ParseWithMemo<T>(string ruleName, Func<T> ruleFunc)
        {
            var startPos = _position;
            return _memoizer.ApplyRule(ruleName, startPos, ruleFunc,
                () => _position, pos => _position = pos);
        }

        /// <summary>
        /// Get current position
        /// </summary>
        protected int GetPosition() => _position;

        /// <summary>
        /// Set current position
        /// </summary>
        protected void SetPosition(int position) => _position = position;

        /// <summary>
        /// Reset parser state
        /// </summary>
        protected virtual void Reset()
        {
            _position = 0;
            _memoizer.Clear();
        }

        /// <summary>
        /// Get memoization statistics
        /// </summary>
        protected (int MemoEntries, int Heads, int StackDepth) GetMemoStats()
        {
            return _memoizer.GetStats();
        }
    }
}