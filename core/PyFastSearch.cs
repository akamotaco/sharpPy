using System;

namespace SharpPy
{
    /// <summary>
    /// FASTSEARCH (Objects/stringlib/fastsearch.h of CPython v3.12.15) for a forward search: the
    /// default scan for small problems, the Crochemore-Perrin two-way algorithm for large ones, so a
    /// find or count is O(n + m) where a plain scan is O(n * m). It is one template over the
    /// character width, as stringlib is: char for str kinds 1 and 2, int for kind 4.
    /// </summary>
    internal static class PyFastSearch
    {
        /// <summary>The first index of pattern in text, or -1.</summary>
        internal static int IndexOf<T>(ReadOnlySpan<T> text, ReadOnlySpan<T> pattern)
            where T : IEquatable<T>, IComparable<T>
        {
            int n = text.Length, m = pattern.Length;
            if (n < m)
                return -1;
            if (n < 2500 || (m < 100 && n < 30000) || m < 6)
                return text.IndexOf(pattern);
            return TwoWay(text, pattern);
        }

        // _lex_search: the maximal suffix of the needle under one order, and its period.
        private static (int Suffix, int Period) MaximalSuffix<T>(ReadOnlySpan<T> needle, bool inverted)
            where T : IEquatable<T>, IComparable<T>
        {
            int suffix = -1, j = 0, k = 1, period = 1;
            while (j + k < needle.Length)
            {
                int order = needle[j + k].CompareTo(needle[suffix + k]);
                if (inverted ? order > 0 : order < 0)
                {
                    j += k;
                    k = 1;
                    period = j - suffix;
                }
                else if (order == 0)
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
        private static int TwoWay<T>(ReadOnlySpan<T> haystack, ReadOnlySpan<T> needle)
            where T : IEquatable<T>, IComparable<T>
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
                    while (i < m && needle[i].Equals(haystack[i + j]))
                        i++;
                    if (i < m)
                    {
                        j += i - cut;
                        memory = -1;
                        continue;
                    }
                    i = cut;
                    while (i > memory && needle[i].Equals(haystack[i + j]))
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
                while (i < m && needle[i].Equals(haystack[i + j]))
                    i++;
                if (i < m)
                {
                    j += i - cut;
                    continue;
                }
                i = cut;
                while (i >= 0 && needle[i].Equals(haystack[i + j]))
                    i--;
                if (i < 0)
                    return j;
                j += shift;
            }
            return -1;
        }
    }
}
