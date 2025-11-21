using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython 3.12 _sre 모듈 - re.py의 C# 백엔드
    /// System.Text.RegularExpressions를 활용한 고성능 구현
    /// CPython: Modules/_sre/sre.c
    /// </summary>
    public static class _SreModule
    {
        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:2947 (Pattern_Type)
        /// Pattern 객체의 타입
        /// </summary>
        public static PyType PyPatternType { get; private set; }


        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:3359-3371 (PyInit__sre)
        /// </summary>
        public static PyModule CreateSreModule()
        {
            // CPython 3.12: Modules/_sre/sre.c:2947-3011 (pattern_type_spec)
            // Lazy initialization to avoid static constructor issues
            if (PyPatternType == null)
            {
                PyPatternType = new PyType("Pattern", new[] { PyType.ObjectType });
                PyPatternType.TypeDict["__name__"] = new PyString("Pattern");
                PyPatternType.TypeDict["__module__"] = new PyString("_sre");
            }

            var module = new PyModule("_sre", "<_sre C module>");

            // CPython 3.12: Modules/_sre/sre.c:3315-3332 (sre_exec - module constants)

            // CPython 3.12: Modules/_sre/sre_constants.h:14
            // #define SRE_MAGIC 20221023
            module.ModuleDict["MAGIC"] = new PyInt(20221023);

            // CPython 3.12: Modules/_sre/sre.c:3322
            // sizeof(SRE_CODE) - typically 4 bytes (uint32_t)
            module.ModuleDict["CODESIZE"] = new PyInt(4);

            // CPython 3.12: Modules/_sre/sre.h:20
            // #define SRE_MAXREPEAT (~(SRE_CODE)0)
            module.ModuleDict["MAXREPEAT"] = new PyInt(4294967295);

            // CPython 3.12: Modules/_sre/sre.h:21
            // #define SRE_MAXGROUPS ((SRE_CODE)INT32_MAX / 2)
            module.ModuleDict["MAXGROUPS"] = new PyInt(1073741823);

            // CPython 3.12: Modules/_sre/sre.c:3252-3261 (_functions[])
            // Module-level functions
            module.ModuleDict["compile"] = new PyBuiltinFunction("compile", Compile);
            module.ModuleDict["match"] = new PyBuiltinFunction("match", Match);
            module.ModuleDict["search"] = new PyBuiltinFunction("search", Search);

            // CPython 3.12: Modules/_sre/sre.c:377-431
            // Case checking and conversion functions (required by re._compiler)
            module.ModuleDict["ascii_iscased"] = new PyBuiltinFunction("ascii_iscased", AsciiIscased);
            module.ModuleDict["unicode_iscased"] = new PyBuiltinFunction("unicode_iscased", UnicodeIscased);
            module.ModuleDict["ascii_tolower"] = new PyBuiltinFunction("ascii_tolower", AsciiTolower);
            module.ModuleDict["unicode_tolower"] = new PyBuiltinFunction("unicode_tolower", UnicodeTolower);

            // CPython 3.12: Modules/_sre/sre.c:3365-3367
            // Export Pattern type to module
            module.ModuleDict["Pattern"] = PyPatternType;
            module.ModuleDict["SRE_Pattern"] = PyPatternType;  // Alias for compatibility

            return module;
        }

        #region 핵심 함수들

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:2947-3011 (_sre_compile_impl)
        /// </summary>
        public static PyObject Compile(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("compile() missing 1 required positional argument: 'pattern'");

            var pattern = args[0].ToStr();
            var flags = args.Length > 1 ? GetFlags(args[1]) : RegexOptions.None;

            try
            {
                return new PySrePattern(pattern.Value, flags);
            }
            catch (ArgumentException ex)
            {
                throw PyRegexError.Create($"Invalid regular expression: {ex.Message}");
            }
        }

        public static PyObject Match(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create("match() missing required arguments");

            var pattern = args[0].ToStr();
            var text = args[1].ToStr();
            var flags = args.Length > 2 ? GetFlags(args[2]) : RegexOptions.None;

            try
            {
                var regex = new Regex(pattern.Value, flags);
                var match = regex.Match(text.Value);

                return match.Success ? new PySreMatch(match, text.Value) : PyNone.Instance;
            }
            catch (ArgumentException ex)
            {
                throw PyRegexError.Create($"Invalid regular expression: {ex.Message}");
            }
        }

        public static PyObject Search(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create("search() missing required arguments");

            var pattern = args[0].ToStr();
            var text = args[1].ToStr();
            var flags = args.Length > 2 ? GetFlags(args[2]) : RegexOptions.None;

            try
            {
                var regex = new Regex(pattern.Value, flags);
                var match = regex.Match(text.Value);

                return match.Success ? new PySreMatch(match, text.Value) : PyNone.Instance;
            }
            catch (ArgumentException ex)
            {
                throw PyRegexError.Create($"Invalid regular expression: {ex.Message}");
            }
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:377-385 (_sre_ascii_iscased_impl)
        /// Check if ASCII character has case (is alphabetic)
        /// </summary>
        public static PyObject AsciiIscased(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("ascii_iscased() missing 1 required positional argument: 'character'");

            int ch = args[0].ToInt();

            // CPython: ch < 128 && Py_ISALPHA(ch)
            // Py_ISALPHA checks if character is alphabetic (A-Z, a-z)
            bool result = ch < 128 && char.IsLetter((char)ch);
            return PyBool.FromBool(result);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:393-401 (_sre_unicode_iscased_impl)
        /// Check if Unicode character has case
        /// </summary>
        public static PyObject UnicodeIscased(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("unicode_iscased() missing 1 required positional argument: 'character'");

            int ch = args[0].ToInt();
            char c = (char)ch;

            // CPython: ch != sre_lower_unicode(ch) || ch != sre_upper_unicode(ch)
            // A character is cased if converting to lower/upper changes it
            bool result = c != char.ToLowerInvariant(c) || c != char.ToUpperInvariant(c);
            return PyBool.FromBool(result);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:409-416 (_sre_ascii_tolower_impl)
        /// Convert ASCII character to lowercase
        /// </summary>
        public static PyObject AsciiTolower(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("ascii_tolower() missing 1 required positional argument: 'character'");

            int ch = args[0].ToInt();

            // CPython: sre_lower_ascii(character)
            // ASCII lowercase: if ch is 'A'-'Z', return 'a'-'z', otherwise return ch
            if (ch >= 'A' && ch <= 'Z')
                return new PyInt(ch + ('a' - 'A'));
            return new PyInt(ch);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:424-431 (_sre_unicode_tolower_impl)
        /// Convert Unicode character to lowercase
        /// </summary>
        public static PyObject UnicodeTolower(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("unicode_tolower() missing 1 required positional argument: 'character'");

            int ch = args[0].ToInt();

            // CPython: sre_lower_unicode(character)
            // Use C# char.ToLowerInvariant for Unicode case mapping
            char c = (char)ch;
            char lower = char.ToLowerInvariant(c);
            return new PyInt((int)lower);
        }

        #endregion

        #region 헬퍼 메서드들

        /// <summary>
        /// CPython 3.12: Lib/re/_constants.py:205-217
        /// Maps Python re flags to C# RegexOptions
        ///
        /// Python flags:
        /// - IGNORECASE = 2 (re.I)
        /// - MULTILINE = 8 (re.M)
        /// - DOTALL = 16 (re.S)
        /// - VERBOSE = 64 (re.X) - NOT mapped! Python's parser handles this
        ///
        /// C# RegexOptions:
        /// - IgnoreCase = 1
        /// - Multiline = 2
        /// - Singleline = 16 (equivalent to DOTALL)
        /// - IgnorePatternWhitespace = 32 - DO NOT USE! Incompatible with Python's VERBOSE
        /// </summary>
        private static RegexOptions GetFlags(PyObject flagsObj)
        {
            if (flagsObj is not PyInt flagsInt)
                return RegexOptions.None;

            int pyFlags = (int)flagsInt.Value;
            RegexOptions csFlags = RegexOptions.None;

            // CPython 3.12: Lib/re/_constants.py:205
            const int SRE_FLAG_IGNORECASE = 2;
            // CPython 3.12: Lib/re/_constants.py:207
            const int SRE_FLAG_MULTILINE = 8;
            // CPython 3.12: Lib/re/_constants.py:209
            const int SRE_FLAG_DOTALL = 16;
            // CPython 3.12: Lib/re/_constants.py:213
            const int SRE_FLAG_VERBOSE = 64;

            // Map Python flags to C# RegexOptions
            if ((pyFlags & SRE_FLAG_IGNORECASE) != 0)
                csFlags |= RegexOptions.IgnoreCase;

            if ((pyFlags & SRE_FLAG_MULTILINE) != 0)
                csFlags |= RegexOptions.Multiline;

            if ((pyFlags & SRE_FLAG_DOTALL) != 0)
                csFlags |= RegexOptions.Singleline;  // C# Singleline = Python DOTALL

            // IMPORTANT: Do NOT map SRE_FLAG_VERBOSE to RegexOptions.IgnorePatternWhitespace!
            // Python's re._parser already handles VERBOSE flag by preprocessing the pattern.
            // C# IgnorePatternWhitespace is incompatible - it ignores spaces inside character classes too!

            return csFlags;
        }

        #endregion
    }

    #region SRE_Pattern 클래스

    /// <summary>
    /// CPython SRE_Pattern - 컴파일된 정규표현식 패턴 객체
    /// CPython 3.12: Modules/_sre/sre.c:3080-3128 (pattern_methods[], pattern_spec)
    /// </summary>
    public class PySrePattern : PyObject
    {
        private readonly Regex _regex;
        private readonly string _pattern;
        private readonly RegexOptions _flags;

        public PySrePattern(string pattern, RegexOptions flags)
        {
            _pattern = pattern;
            _flags = flags;
            _regex = new Regex(pattern, flags);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:2947 (Pattern_Type)
        /// Returns the Pattern type instead of object type
        /// </summary>
        public override PyType GetPyType() => _SreModule.PyPatternType;
        public override string GetTypeName() => "Pattern";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "pattern":
                    return new PyString(_pattern);
                case "flags":
                    return new PyInt((int)_flags);
                case "match":
                    return new PyBuiltinFunction("match", Match);
                case "search":
                    return new PyBuiltinFunction("search", Search);
                case "findall":
                    return new PyBuiltinFunction("findall", FindAll);
                case "finditer":
                    return new PyBuiltinFunction("finditer", FindIter);
                case "sub":
                    return new PyBuiltinFunction("sub", Sub);
                case "subn":
                    return new PyBuiltinFunction("subn", SubN);
                case "split":
                    return new PyBuiltinFunction("split", Split);
                default:
                    return base.GetAttribute(name);
            }
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:1754-1825 (pattern_match_impl)
        /// Signature: match(string, pos=0, endpos=sys.maxsize)
        /// </summary>
        private PyObject Match(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("match() missing 1 required positional argument: 'string'");

            var text = args[0].ToStr();
            // CPython 3.12: Modules/_sre/sre.c:1761-1767 - handle pos and endpos parameters
            int pos = args.Length > 1 && args[1] is PyInt posInt ? (int)posInt.Value : 0;
            int endpos = args.Length > 2 && args[2] is PyInt endposInt ? (int)endposInt.Value : text.Value.Length;

            // Clamp pos and endpos to valid range
            if (pos < 0) pos = 0;
            if (pos > text.Value.Length) pos = text.Value.Length;
            if (endpos < pos) endpos = pos;
            if (endpos > text.Value.Length) endpos = text.Value.Length;

            // Use C# Regex.Match with starting position
            var match = _regex.Match(text.Value, pos, endpos - pos);
            return match.Success ? new PySreMatch(match, text.Value) : PyNone.Instance;
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:1899-1970 (pattern_search_impl)
        /// </summary>
        private PyObject Search(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("search() missing 1 required positional argument: 'string'");

            var text = args[0].ToStr();
            var match = _regex.Match(text.Value);
            return match.Success ? new PySreMatch(match, text.Value) : PyNone.Instance;
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:2079-2154 (pattern_findall_impl)
        /// </summary>
        private PyObject FindAll(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("findall() missing 1 required positional argument: 'string'");

            var text = args[0].ToStr();
            var matches = _regex.Matches(text.Value);
            var result = new List<PyObject>();

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var groups = new List<PyObject>();
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        groups.Add(new PyString(match.Groups[i].Value));
                    }
                    result.Add(new PyTuple(groups.ToArray()));
                }
                else
                {
                    result.Add(new PyString(match.Value));
                }
            }

            return new PyList(result.ToArray());
        }

        private PyObject FindIter(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("finditer() missing 1 required positional argument: 'string'");

            var text = args[0].ToStr();
            var matches = _regex.Matches(text.Value);
            var matchObjects = new List<PyObject>();

            foreach (Match match in matches)
            {
                matchObjects.Add(new PySreMatch(match, text.Value));
            }

            return new PySreMatchIterator(matchObjects);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:2015-2077 (pattern_sub_impl)
        /// In Python, count=0 means "replace all occurrences" (unlimited)
        /// </summary>
        private PyObject Sub(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create("sub() missing required arguments");

            var repl = args[0].ToStr();
            var text = args[1].ToStr();
            // CPython 3.12: count=0 means unlimited replacements
            // .NET Regex.Replace with count=0 means NO replacements
            int count = 0;
            if (args.Length > 2 && args[2] is PyInt countInt)
                count = (int)countInt.Value;

            // In Python, count <= 0 means replace all
            var result = count <= 0 ?
                _regex.Replace(text.Value, repl.Value) :
                _regex.Replace(text.Value, repl.Value, count);

            return new PyString(result);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:2079-2154 (pattern_subn_impl)
        /// In Python, count=0 means "replace all occurrences" (unlimited)
        /// </summary>
        private PyObject SubN(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create("subn() missing required arguments");

            var repl = args[0].ToStr();
            var text = args[1].ToStr();
            // CPython 3.12: count=0 means unlimited replacements
            int count = 0;
            if (args.Length > 2 && args[2] is PyInt countInt)
                count = (int)countInt.Value;

            var matches = _regex.Matches(text.Value).Count;
            // In Python, count <= 0 means replace all
            var result = count <= 0 ?
                _regex.Replace(text.Value, repl.Value) :
                _regex.Replace(text.Value, repl.Value, count);

            var actualSubstitutions = count <= 0 ? matches : Math.Min(matches, count);
            return new PyTuple(new PyString(result), new PyInt(actualSubstitutions));
        }

        private PyObject Split(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("split() missing 1 required positional argument: 'string'");

            var text = args[0].ToStr();
            var maxsplit = args.Length > 1 && args[1] is PyInt maxInt ? (int)maxInt.Value + 1 : 0;

            var parts = maxsplit > 0 ?
                _regex.Split(text.Value, maxsplit) :
                _regex.Split(text.Value);

            var result = new List<PyObject>();
            foreach (var part in parts)
            {
                result.Add(new PyString(part));
            }

            return new PyList(result.ToArray());
        }

        public override string ToString() => $"re.compile('{_pattern}')";
    }

    #endregion

    #region SRE_Match 클래스

    /// <summary>
    /// CPython SRE_Match - 정규표현식 매치 결과 객체
    /// CPython 3.12: Modules/_sre/sre.c:3139-3156 (match_methods[], match_spec)
    /// </summary>
    public class PySreMatch : PyObject
    {
        private readonly Match _match;
        private readonly string _string;

        public PySreMatch(Match match, string text)
        {
            _match = match;
            _string = text;
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "SRE_Match";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "string":
                    return new PyString(_string);
                case "group":
                    return new PyBuiltinFunction("group", Group);
                case "groups":
                    return new PyBuiltinFunction("groups", Groups);
                case "groupdict":
                    return new PyBuiltinFunction("groupdict", GroupDict);
                case "start":
                    return new PyBuiltinFunction("start", Start);
                case "end":
                    return new PyBuiltinFunction("end", End);
                case "span":
                    return new PyBuiltinFunction("span", Span);
                default:
                    return base.GetAttribute(name);
            }
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:862-959 (match_group_impl)
        /// </summary>
        private PyObject Group(PyObject[] args)
        {
            if (args.Length == 0)
            {
                return new PyString(_match.Value);
            }

            if (args.Length == 1)
            {
                if (args[0] is PyInt groupNum)
                {
                    if (groupNum.Value < 0 || groupNum.Value >= _match.Groups.Count)
                        throw PyIndexError.Create("no such group");
                    return new PyString(_match.Groups[(int)groupNum.Value].Value);
                }
                else if (args[0] is PyString groupName)
                {
                    try
                    {
                        var group = _match.Groups[groupName.Value];
                        return new PyString(group.Value);
                    }
                    catch
                    {
                        throw PyIndexError.Create($"no such group: '{groupName.Value}'");
                    }
                }
            }

            var result = new List<PyObject>();
            foreach (var arg in args)
            {
                if (arg is PyInt groupNum)
                {
                    if (groupNum.Value < 0 || groupNum.Value >= _match.Groups.Count)
                        throw PyIndexError.Create("no such group");
                    result.Add(new PyString(_match.Groups[(int)groupNum.Value].Value));
                }
                else if (arg is PyString groupName)
                {
                    try
                    {
                        var group = _match.Groups[groupName.Value];
                        result.Add(new PyString(group.Value));
                    }
                    catch
                    {
                        throw PyIndexError.Create($"no such group: '{groupName.Value}'");
                    }
                }
            }

            return result.Count == 1 ? result[0] : new PyTuple(result.ToArray());
        }

        private PyObject Groups(PyObject[] args)
        {
            var defaultValue = args.Length > 0 ? args[0] : PyNone.Instance;
            var result = new List<PyObject>();

            for (int i = 1; i < _match.Groups.Count; i++)
            {
                var group = _match.Groups[i];
                if (group.Success)
                    result.Add(new PyString(group.Value));
                else
                    result.Add(defaultValue);
            }

            return new PyTuple(result.ToArray());
        }

        private PyObject GroupDict(PyObject[] args)
        {
            var defaultValue = args.Length > 0 ? args[0] : PyNone.Instance;
            var result = new PyDict();

            foreach (string groupName in _match.Groups.Keys)
            {
                if (groupName != "0")
                {
                    var group = _match.Groups[groupName];
                    var value = group.Success ? new PyString(group.Value) : defaultValue;
                    result.SetItem(new PyString(groupName), value);
                }
            }

            return result;
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:1041-1094 (match_start_impl)
        /// </summary>
        private PyObject Start(PyObject[] args)
        {
            var groupNum = args.Length > 0 && args[0] is PyInt groupInt ? (int)groupInt.Value : 0;

            if (groupNum < 0 || groupNum >= _match.Groups.Count)
                throw PyIndexError.Create("no such group");

            return new PyInt(_match.Groups[groupNum].Index);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:1134-1187 (match_end_impl)
        /// </summary>
        private PyObject End(PyObject[] args)
        {
            var groupNum = args.Length > 0 && args[0] is PyInt groupInt ? (int)groupInt.Value : 0;

            if (groupNum < 0 || groupNum >= _match.Groups.Count)
                throw PyIndexError.Create("no such group");

            var group = _match.Groups[groupNum];
            return new PyInt(group.Index + group.Length);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:1227-1267 (match_span_impl)
        /// </summary>
        private PyObject Span(PyObject[] args)
        {
            var groupNum = args.Length > 0 && args[0] is PyInt groupInt ? (int)groupInt.Value : 0;

            if (groupNum < 0 || groupNum >= _match.Groups.Count)
                throw PyIndexError.Create("no such group");

            var group = _match.Groups[groupNum];
            return new PyTuple(new PyInt(group.Index), new PyInt(group.Index + group.Length));
        }

        public override string ToString() => $"<re.Match object; span=({_match.Index}, {_match.Index + _match.Length}), match='{_match.Value}'>";
    }

    #endregion

    #region SRE Match Iterator

    public class PySreMatchIterator : PyObject
    {
        private readonly List<PyObject> _matches;

        public PySreMatchIterator(List<PyObject> matches)
        {
            _matches = matches;
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "callable_iterator";

        public override PyObject GetIterator()
        {
            return new PySreListIterator(_matches);
        }

        private class PySreListIterator : PyIterator
        {
            private readonly List<PyObject> _items;
            private int _index = 0;

            public PySreListIterator(List<PyObject> items)
            {
                _items = items;
            }

            public override PyObject Next()
            {
                if (_index >= _items.Count)
                    throw PyStopIteration.Create();

                return _items[_index++];
            }

            public override string GetTypeName() => "list_iterator";
        }
    }

    #endregion

    #region 예외 클래스

    public class PyRegexError : PyValueError
    {
        public PyRegexError(string message) : base(message) { }
        public override string GetTypeName() => "error";

        public new static PythonException Create(string message)
        {
            return new PythonException(new PyRegexError(message));
        }
    }

    #endregion
}
