using System;

namespace SharpPy
{
    /// <summary>
    /// FASTSEARCH (Objects/stringlib/fastsearch.h of CPython v3.12.15) for a forward search: the
    /// default scan for small problems, the Crochemore-Perrin two-way algorithm for large ones, so a
    /// find or count is O(n + m) where a plain scan is O(n * m). Indexes are UTF-16 units.
    /// </summary>
    internal static class PyFastSearch
    {
        /// <summary>The first index of pattern in text[start, end), or -1.</summary>
        internal static int IndexOf(string text, string pattern, int start, int end)
        {
            int n = end - start, m = pattern.Length;
            if (n < m)
                return -1;
            if (n < 2500 || (m < 100 && n < 30000) || m < 6)
                return text.IndexOf(pattern, start, n, StringComparison.Ordinal);
            return TwoWay(text.AsSpan(start, n), pattern.AsSpan()) is var found && found >= 0 ? found + start : -1;
        }

        // _lex_search: the maximal suffix of the needle under one order, and its period.
        private static (int Suffix, int Period) MaximalSuffix(ReadOnlySpan<char> needle, bool inverted)
        {
            int suffix = -1, j = 0, k = 1, period = 1;
            while (j + k < needle.Length)
            {
                char a = needle[j + k], b = needle[suffix + k];
                if (inverted ? a > b : a < b)
                {
                    j += k;
                    k = 1;
                    period = j - suffix;
                }
                else if (a == b)
                {
                    if (k != period)
                        k++;
                    else
                    {
                        j += period;
                        k = 1;
                    }
                }
                else
                {
                    suffix = j;
                    j = suffix + 1;
                    k = period = 1;
                }
            }
            return (suffix, period);
        }

        // _factorize, _preprocess and _two_way.
        private static int TwoWay(ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle)
        {
            int n = haystack.Length, m = needle.Length;
            var (s1, p1) = MaximalSuffix(needle, false);
            var (s2, p2) = MaximalSuffix(needle, true);
            int cut = s1 > s2 ? s1 : s2;
            int period = s1 > s2 ? p1 : p2;

            if (needle.Slice(0, cut + 1).SequenceEqual(needle.Slice(period, cut + 1)))
            {
                // A periodic needle: remember how much of the last window already matched.
                int memory = -1;
                for (int j = 0; j <= n - m;)
                {
                    int i = Math.Max(cut, memory) + 1;
                    while (i < m && needle[i] == haystack[i + j])
                        i++;
                    if (i < m)
                    {
                        j += i - cut;
                        memory = -1;
                        continue;
                    }
                    i = cut;
                    while (i > memory && needle[i] == haystack[i + j])
                        i--;
                    if (i <= memory)
                        return j;
                    j += period;
                    memory = m - period - 1;
                }
                return -1;
            }

            int shift = Math.Max(cut + 1, m - cut - 1) + 1;
            for (int j = 0; j <= n - m;)
            {
                int i = cut + 1;
                while (i < m && needle[i] == haystack[i + j])
                    i++;
                if (i < m)
                {
                    j += i - cut;
                    continue;
                }
                i = cut;
                while (i >= 0 && needle[i] == haystack[i + j])
                    i--;
                if (i < 0)
                    return j;
                j += shift;
            }
            return -1;
        }
    }
}
