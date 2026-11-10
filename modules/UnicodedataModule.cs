using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/unicodedata.c. The tables come from CPython's (tools/gen_unicode_data.py), and
    /// normalization is done here rather than by string.Normalize, which depends on ICU and is
    /// not there in every place Godot runs.
    /// </summary>
    internal static class UnicodedataModule
    {
        private sealed class Runs
        {
            internal readonly List<int> Starts = new();
            internal readonly List<string> Values = new();

            internal string At(int code)
            {
                int low = 0, high = Starts.Count - 1;
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    if (Starts[middle] <= code) low = middle;
                    else high = middle - 1;
                }
                return Values[low];
            }
        }

        private const int SBase = 0xAC00, LBase = 0x1100, VBase = 0x1161, TBase = 0x11A7;
        private const int LCount = 19, VCount = 21, TCount = 28, NCount = VCount * TCount, SCount = LCount * NCount;

        private static Dictionary<string, Runs> _properties;
        private static Dictionary<long, int> _compositions;

        private static Runs Property(string name)
        {
            if (_properties == null)
                Load();
            return _properties[name];
        }

        private static void Load()
        {
            var properties = new Dictionary<string, Runs>();
            var compositions = new List<int>();
            using var packed = new MemoryStream(Convert.FromBase64String(UnicodePropertyData.Deflated));
            using var inflate = new DeflateStream(packed, CompressionMode.Decompress);
            using var reader = new StreamReader(inflate, Encoding.ASCII);
            Runs current = null;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line[0] == '=')
                {
                    current = line == "=compositions" ? null : properties[line.Substring(1)] = new Runs();
                    continue;
                }
                int space = line.IndexOf(' ');
                if (current == null)
                {
                    compositions.Add(Convert.ToInt32(line, 16));
                    continue;
                }
                current.Starts.Add(Convert.ToInt32(line.Substring(0, space), 16));
                current.Values.Add(line.Substring(space + 1));
            }
            var pairs = new Dictionary<long, int>();
            foreach (int code in compositions)
            {
                var parts = properties["decomposition"].At(code).Split(' ');
                pairs[Pair(Convert.ToInt32(parts[0], 16), Convert.ToInt32(parts[1], 16))] = code;
            }
            _compositions = pairs;
            _properties = properties;
        }

        private static long Pair(int first, int second) => ((long)first << 32) | (uint)second;

        internal static int Combining(int code) => int.Parse(Property("combining").At(code), CultureInfo.InvariantCulture);

        // The argument of each function: a str of one character.
        // The clinic's int(accept={str}) character converter: "argument" for the METH_O functions,
        // "argument 1" for those with a default (METH_FASTCALL).
        private static int Code(string function, PyObject[] args)
        {
            if (args[0] is PyString text && text.CodePointLength() == 1)
                return text.Value.Length == 1 ? text.Value[0] : char.ConvertToUtf32(text.Value, 0);
            throw PyArgs.BadArgument(function, function is "decimal" or "digit" or "numeric" or "name" ? "argument 1" : "argument",
                "a unicode character", args[0]);
        }

        private static List<int> CodePoints(string text)
        {
            var points = new List<int>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    points.Add(char.ConvertToUtf32(text[i], text[++i]));
                else
                    points.Add(text[i]);
            }
            return points;
        }

        private static string Text(List<int> points)
        {
            var result = new StringBuilder(points.Count);
            foreach (int point in points)
            {
                if (point >= 0x10000) result.Append(char.ConvertFromUtf32(point));
                else result.Append((char)point);
            }
            return result.ToString();
        }

        private static void Decompose(int code, bool compatible, List<int> output)
        {
            if (code >= SBase && code < SBase + SCount)
            {
                int index = code - SBase;
                output.Add(LBase + index / NCount);
                output.Add(VBase + index % NCount / TCount);
                if (index % TCount != 0)
                    output.Add(TBase + index % TCount);
                return;
            }
            string decomposition = Property("decomposition").At(code);
            var parts = decomposition.Length == 0 ? Array.Empty<string>() : decomposition.Split(' ');
            int first = parts.Length > 0 && parts[0][0] == '<' ? 1 : 0;
            if (parts.Length == 0 || (first == 1 && !compatible))
            {
                output.Add(code);
                return;
            }
            for (int i = first; i < parts.Length; i++)
                Decompose(Convert.ToInt32(parts[i], 16), compatible, output);
        }

        // nfd_nfkd: full decomposition, and then the canonical order of the combining marks
        private static List<int> Decomposed(string text, bool compatible)
        {
            var output = new List<int>(text.Length);
            foreach (int point in CodePoints(text))
                Decompose(point, compatible, output);
            for (int i = 1; i < output.Count; i++)
            {
                int point = output[i], rank = Combining(point);
                if (rank == 0)
                    continue;
                int j = i;
                while (j > 0 && Combining(output[j - 1]) > rank)
                {
                    output[j] = output[j - 1];
                    j--;
                }
                output[j] = point;
            }
            return output;
        }

        private static int Composite(int first, int second)
        {
            if (first >= LBase && first < LBase + LCount && second >= VBase && second < VBase + VCount)
                return SBase + ((first - LBase) * VCount + (second - VBase)) * TCount;
            if (first >= SBase && first < SBase + SCount && (first - SBase) % TCount == 0 &&
                second > TBase && second < TBase + TCount)
                return first + second - TBase;
            return _compositions.TryGetValue(Pair(first, second), out int code) ? code : -1;
        }

        // nfc_nfkc
        private static List<int> Composed(List<int> points)
        {
            if (points.Count == 0)
                return points;
            var output = new List<int>(points.Count);
            // A mark is blocked from the starter by one of the same or a higher class between them.
            int starterIndex = -1, lastRank = -1;
            foreach (int point in points)
            {
                int rank = Combining(point);
                if (starterIndex >= 0 && lastRank < rank)
                {
                    int composite = Composite(output[starterIndex], point);
                    if (composite >= 0)
                    {
                        output[starterIndex] = composite;
                        continue;
                    }
                }
                if (rank == 0)
                {
                    starterIndex = output.Count;
                    lastRank = -1;
                }
                else
                    lastRank = rank;
                output.Add(point);
            }
            return output;
        }

        private static string Normalize(PyObject form, PyObject input, string function)
        {
            if (form is not PyString name)
                throw PyArgs.BadArgument(function, "argument 1", "str", form);
            if (input is not PyString text)
                throw PyArgs.BadArgument(function, "argument 2", "str", input);
            return name.Value switch
            {
                "NFC" => Text(Composed(Decomposed(text.Value, false))),
                "NFKC" => Text(Composed(Decomposed(text.Value, true))),
                "NFD" => Text(Decomposed(text.Value, false)),
                "NFKD" => Text(Decomposed(text.Value, true)),
                _ => throw PyValueError.Create("invalid normalization form"),
            };
        }

        private static PyObject TwoArguments(string function, PyObject[] args, Func<PyObject, PyObject, PyObject> body)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"{function} expected 2 arguments, got {args.Length}");
            return body(args[0], args[1]);
        }

        private static void Text1(PyModule module, string name, Func<int, PyObject> answer) =>
            module.ModuleDict[name] = new PyBuiltinFunction(name, args => answer(Code(name, args)));

        // decimal, digit and numeric: what the table has, or the default, or this error
        private static void Number(PyModule module, string name, string error, Func<string, PyObject> value) =>
            module.ModuleDict[name] = new PyBuiltinFunction(name, args =>
            {
                string found = Property(name).At(Code(name, args));
                if (found.Length != 0)
                    return value(found);
                return args.Length > 1 ? args[1] : throw PyValueError.Create(error);
            });

        internal static PyModule CreateModule()
        {
            var module = new PyModule("unicodedata");
            module.ModuleDict["unidata_version"] = PyString.From(UnicodePropertyData.Version);
            Text1(module, "category", code => PyString.From(Property("category").At(code)));
            Text1(module, "bidirectional", code => PyString.From(Property("bidirectional").At(code)));
            Text1(module, "east_asian_width", code => PyString.From(Property("east_asian_width").At(code)));
            Text1(module, "decomposition", code => PyString.From(Property("decomposition").At(code)));
            Text1(module, "combining", code => PyInt.From(Combining(code)));
            Text1(module, "mirrored", code => PyInt.From(Property("mirrored").At(code) == "1" ? 1 : 0));
            Number(module, "decimal", "not a decimal", found => PyInt.From(int.Parse(found, CultureInfo.InvariantCulture)));
            Number(module, "digit", "not a digit", found => PyInt.From(int.Parse(found, CultureInfo.InvariantCulture)));
            Number(module, "numeric", "not a numeric character", found => new PyFloat(double.Parse(found, CultureInfo.InvariantCulture)));
            module.ModuleDict["normalize"] = new PyBuiltinFunction("normalize", args => TwoArguments("normalize", args,
                (form, input) => PyString.From(Normalize(form, input, "normalize"))));
            module.ModuleDict["is_normalized"] = new PyBuiltinFunction("is_normalized", args => TwoArguments("is_normalized", args,
                (form, input) => PyBool.FromBool(Normalize(form, input, "is_normalized") == ((PyString)input).Value)));
            module.ModuleDict["name"] = new PyBuiltinFunction("name", args =>
            {
                // The table also serves N{...}, where a control character goes by an alias.
                int code = Code("name", args);
                string found = Property("category").At(code) == "Cc" ? null : UnicodeNames.Name(code);
                if (found != null)
                    return PyString.From(found);
                return args.Length > 1 ? args[1] : throw PyValueError.Create("no such name");
            });
            module.ModuleDict["lookup"] = new PyBuiltinFunction("lookup", args =>
            {
                if (args.Length != 1 || args[0] is not PyString name)
                    throw PyTypeError.Create("lookup() argument must be str");
                int code = UnicodeNames.Lookup(name.Value);
                if (code < 0)
                    throw PyKeyError.Create(PyString.From($"undefined character name '{name.Value}'"));
                return PyString.From(code >= 0x10000 ? char.ConvertFromUtf32(code) : ((char)code).ToString());
            });
            return module;
        }
    }
}
