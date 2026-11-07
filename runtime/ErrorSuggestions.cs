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

            // Trim away common affixes (CPython advances BOTH string pointers by
            // the same count, so a single shared window start represents both).
            int aStart = 0;
            while (aSize > aStart && bSize > aStart && a[aStart] == b[aStart])
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

            // Prefer shorter buffer (the shared start offset still applies to both)
            if (bSize < aSize)
            {
                var tempStr = a;
                a = b;
                b = tempStr;

                var tempSize = aSize;
                aSize = bSize;
                bSize = tempSize;
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
                char code = b[aStart + bIndex];
                int distance = result = bIndex * MoveCost;
                int minimum = int.MaxValue;

                for (int index = 0; index < aSize; index++)
                {
                    // CPython 3.12 Python/suggestions.c exact recurrence:
                    // 1) substitute: cost(b[:b_index], a[:index]) + sub_cost
                    int substituteCost = distance + SubstitutionCost(code, a[aStart + index]);
                    // 2) previous-row value cost(b[:b_index], a[:index+1]) becomes the
                    //    DIAGONAL for the next column - keep it in `distance`.
                    distance = buffer[index];
                    // 3) insert/delete: min(left, top) + MOVE_COST
                    int insertDelete = Math.Min(result, distance) + MoveCost;

                    result = Math.Min(insertDelete, substituteCost);
                    buffer[index] = result;
                    if (result < minimum)
                    {
                        minimum = result;
                    }
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

            // CPython 3.12: calculate_suggestions refuses to scan very large namespaces
            var dirList = dir.Take(MaxCandidateItems + 1).ToList();
            if (dirList.Count >= MaxCandidateItems)
            {
                return null;
            }

            string? suggestion = null;
            int suggestionDistance = int.MaxValue;

            foreach (var item in dirList)
            {
                if (item == name)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(item))
                {
                    continue;
                }

                int itemSize = item.Length;

                // CPython 3.12: no more than 1/3 of the involved characters should need changed.
                int maxDistance = (nameSize + itemSize + 3) * MoveCost / 6;
                // Don't take matches we've already beaten.
                if (suggestionDistance - 1 < maxDistance)
                {
                    maxDistance = suggestionDistance - 1;
                }

                int currentDistance = LevenshteinDistance(name, item, maxDistance);
                if (currentDistance > maxDistance)
                {
                    continue;
                }

                if (suggestion == null || currentDistance < suggestionDistance)
                {
                    suggestion = item;
                    suggestionDistance = currentDistance;
                }
            }

            return suggestion;
        }

        /// <summary>
        /// _Py_Offer_Suggestions (Python/suggestions.c): the trailer the exception writer adds to an
        /// AttributeError, NameError or ImportError of exactly that type, or null.
        /// </summary>
        internal static string? Offer(PyObject exception)
        {
            if (exception.IsExactly(PyType.AttributeErrorType))
                return OfferForAttributeError((PyBaseException)exception);
            if (exception.IsExactly(PyType.NameErrorType))
                return OfferForNameError((PyBaseException)exception);
            if (exception.IsExactly(PyType.ImportErrorType))
                return OfferForImportError((PyBaseException)exception);
            return null;
        }

        private static PyObject? Member(PyBaseException exception, string name) =>
            exception.TryGetMember(name, out var value) ? value : null;

        // offer_suggestions_for_attribute_error / get_suggestions_for_attribute_error
        private static string? OfferForAttributeError(PyBaseException exception)
        {
            var name = Member(exception, "name");
            var obj = Member(exception, "obj");
            if (name == null || obj == null || !name.IsExactly(PyType.StrType))
                return null;
            var suggestion = CalculateSuggestions(DirNames(PyBuiltinFunction.ObjectDir(obj)), ((PyString)name).Value);
            return suggestion == null ? null : $". Did you mean: {PyString.From(suggestion).ToRepr().Value}?";
        }

        // offer_suggestions_for_name_error: the innermost frame of the traceback.
        private static string? OfferForNameError(PyBaseException exception)
        {
            var name = Member(exception, "name");
            if (name == null || !name.IsExactly(PyType.StrType) || exception.__traceback__ is not PyTraceback traceback)
                return null;
            while (traceback.Next != null)
                traceback = traceback.Next;
            var frame = traceback.Frame;
            string text = ((PyString)name).Value;
            string? suggestion = SuggestionsForNameError(text, frame);
            string quoted = PyString.From(text).ToRepr().Value;
            if (!StdlibModuleNames.Contains(text))
                return suggestion == null ? null : $". Did you mean: {PyString.From(suggestion).ToRepr().Value}?";
            if (suggestion == null)
                return $". Did you forget to import {quoted}?";
            return $". Did you mean: {PyString.From(suggestion).ToRepr().Value}? Or did you forget to import {quoted}?";
        }

        // get_suggestions_for_name_error: co_varnames (and self's attributes), then the globals,
        // then the builtins; an earlier pool with an answer wins.
        private static string? SuggestionsForNameError(string name, PyFrame frame)
        {
            var varnames = DirNames(frame.Code.GetAttribute("co_varnames"));
            if (varnames.Contains("self"))
            {
                var locals = frame.GetAttribute("f_locals");
                if (locals is PyDict dict && dict.TryGetStoredValue(PyString.From("self"), out var self))
                {
                    try
                    {
                        self.GetAttribute(name);
                        return "self." + name;
                    }
                    catch (PythonException error) when (error.PyException is PyAttributeError)
                    {
                    }
                }
            }
            return CalculateSuggestions(varnames, name)
                ?? CalculateSuggestions(DirNames(frame.GetAttribute("f_globals")), name)
                ?? CalculateSuggestions(DirNames(frame.GetAttribute("f_builtins")), name);
        }

        // offer_suggestions_for_import_error: dir() of the module the name was wanted from.
        private static string? OfferForImportError(PyBaseException exception)
        {
            var moduleName = Member(exception, "name");
            var name = Member(exception, "name_from");
            if (name == null || moduleName == null || name is PyNone ||
                !name.IsExactly(PyType.StrType) || !moduleName.IsExactly(PyType.StrType))
                return null;
            var modules = PyImportSystem.Import("sys").GetAttribute("modules");
            if (modules is not PyDict table || !table.TryGetStoredValue(moduleName, out var module))
                return null;
            var suggestion = CalculateSuggestions(DirNames(PyBuiltinFunction.ObjectDir(module)), ((PyString)name).Value);
            return suggestion == null ? null : $". Did you mean: {PyString.From(suggestion).ToRepr().Value}?";
        }

        // PySequence_List of names: what the candidates are, in their order.
        private static List<string> DirNames(PyObject names)
        {
            var result = new List<string>();
            foreach (var item in PyObject.IterateItems(names))
                if (item is PyString text)
                    result.Add(text.Value);
            return result;
        }

        private static List<string>? AsNameList(PyObject? value)
        {
            switch (value)
            {
                case PyDict d:
                    var fromDict = new List<string>();
                    foreach (var key in d.Keys().Items)
                        if (key is PyString ks && ks.Value.Length > 0)
                            fromDict.Add(ks.Value);
                    return fromDict;
                case PyModule m:
                    var fromModule = new List<string>();
                    foreach (var key in m.ModuleDict.Keys)
                        if (key.Length > 0)
                            fromModule.Add(key);
                    return fromModule;
                default:
                    return null;
            }
        }

    }
}
