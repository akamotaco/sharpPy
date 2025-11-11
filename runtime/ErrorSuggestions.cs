using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// Provides "Did you mean?" suggestions for runtime errors
    /// CPython 3.12: Python/suggestions.c
    /// </summary>
    public static class ErrorSuggestions
    {
        // CPython 3.12: Python/suggestions.c:9-10
        private const int MaxCandidateItems = 750;
        private const int MaxStringSize = 40;

        // CPython 3.12: Python/suggestions.c:12-13
        private const int MoveCost = 2;
        private const int CaseCost = 1;

        /// <summary>
        /// Calculate substitution cost for Levenshtein distance
        /// CPython 3.12: Python/suggestions.c:17-37
        /// </summary>
        private static int SubstitutionCost(char a, char b)
        {
            // LEAST_FIVE_BITS(n) = ((n) & 31)
            if ((a & 31) != (b & 31))
            {
                // Not the same, not a case flip
                return MoveCost;
            }

            if (a == b)
            {
                return 0;
            }

            // Normalize to lowercase
            char aLower = char.ToLowerInvariant(a);
            char bLower = char.ToLowerInvariant(b);

            if (aLower == bLower)
            {
                return CaseCost;
            }

            return MoveCost;
        }

        /// <summary>
        /// Calculate Levenshtein distance between two strings
        /// CPython 3.12: Python/suggestions.c:39-133
        /// </summary>
        private static int LevenshteinDistance(string a, string b, int maxCost)
        {
            // Both strings are the same (by identity)
            if (a == b)
            {
                return 0;
            }

            int aSize = a.Length;
            int bSize = b.Length;
            int aStart = 0;
            int bStart = 0;

            // Trim away common affixes
            while (aStart < aSize && aStart < bSize && a[aStart] == b[aStart])
            {
                aStart++;
            }

            while (aSize > aStart && bSize > aStart && a[aSize - 1] == b[bSize - 1])
            {
                aSize--;
                bSize--;
            }

            aSize -= aStart;
            bSize -= aStart;

            if (aSize == 0 || bSize == 0)
            {
                return (aSize + bSize) * MoveCost;
            }

            if (aSize > MaxStringSize || bSize > MaxStringSize)
            {
                return maxCost + 1;
            }

            // Prefer shorter buffer
            bool swapped = false;
            if (bSize < aSize)
            {
                var tempStr = a;
                a = b;
                b = tempStr;

                var tempSize = aSize;
                aSize = bSize;
                bSize = tempSize;

                var tempStart = aStart;
                aStart = bStart;
                bStart = tempStart;

                swapped = true;
            }

            // Quick fail when a match is impossible
            if ((bSize - aSize) * MoveCost > maxCost)
            {
                return maxCost + 1;
            }

            // Initialize buffer
            int[] buffer = new int[aSize];
            int tmp = MoveCost;
            for (int i = 0; i < aSize; i++)
            {
                buffer[i] = tmp;
                tmp += MoveCost;
            }

            int result = 0;
            for (int bIndex = 0; bIndex < bSize; bIndex++)
            {
                char code = b[bStart + bIndex];
                int distance = result = bIndex * MoveCost;
                int minimum = int.MaxValue;

                for (int index = 0; index < aSize; index++)
                {
                    // 1) substitute
                    int substituteCost = distance + SubstitutionCost(code, a[aStart + index]);
                    // 2) delete from b
                    int deleteCost = result + MoveCost;
                    // 3) delete from a
                    int insertCost = buffer[index] + MoveCost;

                    distance = result = Math.Min(substituteCost, Math.Min(deleteCost, insertCost));
                    if (distance < minimum)
                    {
                        minimum = distance;
                    }

                    buffer[index] = distance;
                }

                if (minimum > maxCost)
                {
                    // Everything in this row is too big, bail early
                    return maxCost + 1;
                }
            }

            return result;
        }

        /// <summary>
        /// Calculate suggestions from a list of candidates
        /// CPython 3.12: Python/suggestions.c:135-201
        /// </summary>
        private static string? CalculateSuggestions(IEnumerable<string> dir, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            int nameSize = name.Length;
            if (nameSize >= MaxStringSize)
            {
                return null;
            }

            // CPython: MAX_CANDIDATE_ITEMS check
            var dirList = dir.Take(MaxCandidateItems).ToList();

            int maxDistance = (nameSize + 2) * MoveCost / 6;

            string? suggestion = null;
            int suggestionDistance = int.MaxValue;

            foreach (var item in dirList)
            {
                if (string.IsNullOrEmpty(item))
                {
                    continue;
                }

                // Skip private/magic names (start with '_')
                if (item.StartsWith("_"))
                {
                    continue;
                }

                int itemSize = item.Length;
                if (itemSize >= MaxStringSize)
                {
                    continue;
                }

                // Don't suggest excessively long names
                if (itemSize > (nameSize + maxDistance) * 3)
                {
                    continue;
                }

                int currentDistance = LevenshteinDistance(name, item, suggestionDistance - 1);

                if (currentDistance < suggestionDistance)
                {
                    suggestion = item;
                    suggestionDistance = currentDistance;
                }
            }

            return suggestion;
        }

        /// <summary>
        /// Get suggestions for NameError
        /// CPython 3.12: Python/suggestions.c:217-290
        /// </summary>
        public static string? GetSuggestionForNameError(string name, Dictionary<string, PyObject> locals, Dictionary<string, PyObject> globals, Dictionary<string, PyObject> builtins)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            // 1. Search in locals
            string? suggestion = CalculateSuggestions(locals.Keys, name);
            if (suggestion != null)
            {
                return suggestion;
            }

            // 2. Check if we're in a method and instance has attribute 'name'
            if (locals.ContainsKey("self") && locals["self"] is PyObject self)
            {
                // Try to get attribute from self
                try
                {
                    var attr = self.GetAttribute(name);
                    if (attr != null)
                    {
                        return "self." + name;
                    }
                }
                catch
                {
                    // Attribute doesn't exist, continue
                }
            }

            // 3. Search in globals
            suggestion = CalculateSuggestions(globals.Keys, name);
            if (suggestion != null)
            {
                return suggestion;
            }

            // 4. Search in builtins
            suggestion = CalculateSuggestions(builtins.Keys, name);
            if (suggestion != null)
            {
                return suggestion;
            }

            return null;
        }

        /// <summary>
        /// Format NameError with suggestion
        /// CPython 3.12: Python/suggestions.c:306-350
        /// </summary>
        public static string FormatNameErrorWithSuggestion(string name, string? suggestion, bool isStdlibModule = false)
        {
            string baseMessage = $"name '{name}' is not defined";

            if (isStdlibModule)
            {
                if (suggestion == null)
                {
                    return baseMessage + $". Did you forget to import '{name}'?";
                }
                else
                {
                    return baseMessage + $". Did you mean: '{suggestion}'? Or did you forget to import '{name}'?";
                }
            }
            else
            {
                if (suggestion == null)
                {
                    return baseMessage;
                }
                else
                {
                    return baseMessage + $". Did you mean: '{suggestion}'?";
                }
            }
        }
    }
}
