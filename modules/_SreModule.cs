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
                var regex = BuildRegex(pattern.Value, flags, out var names, out var count);
                var match = regex.Match(text.Value);

                return match.Success
                    ? new PySreMatch(match, text.Value, 0, names, count)
                    : PyNone.Instance;
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
                var regex = BuildRegex(pattern.Value, flags, out var names, out var count);
                var match = regex.Match(text.Value);

                return match.Success
                    ? new PySreMatch(match, text.Value, 0, names, count)
                    : PyNone.Instance;
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

        /// <summary>
        /// Translate Python-only regex constructs to .NET Regex syntax. The engine
        /// is System.Text.RegularExpressions, which uses (?&lt;name&gt;...) for named
        /// groups and \k&lt;name&gt; for named backreferences, whereas Python writes
        /// (?P&lt;name&gt;...) and (?P=name). Fast-path returns unchanged when no
        /// "(?P" is present, so ordinary patterns are untouched.
        /// </summary>
        /// <summary>
        /// CPython 3.12: re/_parser.py parse_template. A replacement template is its own
        /// language, not a .NET replacement string: \g<name> and \g<1> substitute a group,
        /// \1..\99 substitute a group number, \0 is an octal NUL rather than group 0,
        /// the C escapes (\a \b \f \n \r \t \v) and \x/\u/\U produce characters, a backslash
        /// before any other non-letter is simply dropped, and a backslash before an unknown
        /// letter raises "bad escape \q at position 0".
        /// .NET only understands $1 / ${name}, and a literal $ has to be doubled.
        /// Known limit: numeric references use .NET group numbering (see P0-17), so a pattern
        /// that declares a named group before an unnamed one still substitutes the wrong group.
        /// </summary>
        internal static string ToDotNetReplacement(string repl, Dictionary<string, int> names = null)
        {
            if (string.IsNullOrEmpty(repl))
                return repl;

            var sb = new System.Text.StringBuilder(repl.Length + 8);
            for (int i = 0; i < repl.Length; i++)
            {
                char c = repl[i];
                if (c != '\\')
                {
                    if (c == '$')
                        sb.Append("$$");
                    else
                        sb.Append(c);
                    continue;
                }

                if (i + 1 >= repl.Length)
                    throw PyRegexError.Create($"bad escape (end of pattern) at position {i}");

                char n = repl[i + 1];
                if (n == 'g')
                {
                    if (i + 2 < repl.Length && repl[i + 2] == '<')
                    {
                        int close = repl.IndexOf('>', i + 3);
                        if (close < 0)
                            throw PyRegexError.Create(
                                $"missing >, previously used identifier at position {i + 2}");
                        sb.Append(GroupTemplate(repl.Substring(i + 3, close - i - 3), names));
                        i = close;
                        continue;
                    }
                    throw PyRegexError.Create($"bad escape \\g at position {i}");
                }

                if (n >= '1' && n <= '9')
                {
                    int j = i + 1;
                    int num = 0;
                    // CPython takes at most two digits for a group reference.
                    while (j < repl.Length && j < i + 3 && repl[j] >= '0' && repl[j] <= '9')
                    {
                        num = num * 10 + (repl[j] - '0');
                        j++;
                    }
                    sb.Append(GroupTemplate(num.ToString(), names));
                    i = j - 1;
                    continue;
                }

                if (n >= '0' && n <= '7')
                {
                    // \0 and friends are octal character escapes, not group 0.
                    int j = i + 1;
                    int val = 0;
                    while (j < repl.Length && j < i + 4 && repl[j] >= '0' && repl[j] <= '7')
                    {
                        val = val * 8 + (repl[j] - '0');
                        j++;
                    }
                    sb.Append((char)val);
                    i = j - 1;
                    continue;
                }

                switch (n)
                {
                    case '\\': sb.Append('\\'); i++; break;
                    case 'a': sb.Append('\a'); i++; break;
                    case 'b': sb.Append('\b'); i++; break;
                    case 'f': sb.Append('\f'); i++; break;
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case 'v': sb.Append('\v'); i++; break;
                    case 'x':
                    case 'u':
                    case 'U':
                    {
                        int want = n == 'x' ? 2 : (n == 'u' ? 4 : 8);
                        if (i + 1 + want > repl.Length)
                            throw PyRegexError.Create($"incomplete escape \\{n} at position {i}");
                        int val = 0;
                        for (int k = 0; k < want; k++)
                        {
                            int d = HexDigit(repl[i + 2 + k]);
                            if (d < 0)
                                throw PyRegexError.Create(
                                    $"incomplete escape \\{n} at position {i}");
                            val = val * 16 + d;
                        }
                        sb.Append(char.ConvertFromUtf32(val));
                        i += 1 + want;
                        break;
                    }
                    default:
                        if (char.IsLetter(n))
                            throw PyRegexError.Create($"bad escape \\{n} at position {i}");
                        // A backslash before punctuation is dropped, matching parse_template.
                        sb.Append(n);
                        i++;
                        break;
                }
            }
            return sb.ToString();
        }

        // A template reference points at the canonical name when the pattern was canonically
        // named; otherwise it keeps the numeric form .NET understands.
        private static string GroupTemplate(string reference, Dictionary<string, int> names)
        {
            if (names != null)
            {
                if (int.TryParse(reference, out int byNumber))
                    return "${__p" + byNumber + "}";
                if (names.TryGetValue(reference, out int byName))
                    return "${__p" + byName + "}";
            }
            return "${" + reference + "}";
        }

        private static int HexDigit(char c) =>
            c >= '0' && c <= '9' ? c - '0' :
            c >= 'a' && c <= 'f' ? c - 'a' + 10 :
            c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

        /// <summary>
        /// CPython 3.12: re/_parser.py assigns capture-group numbers by the position of '(', and
        /// _compiler.py resolves every reference to that number before the matcher ever looks at
        /// pattern text again. .NET numbers capture ids with the unnamed groups first, so its ids
        /// are a *different* numbering and cannot be trusted. This translation pins CPython's
        /// numbering by renaming every capture group to a canonical name that carries the CPython
        /// number, and rewriting every reference (\N and (?P=name)) to that name.
        /// Returns null when the pattern uses something the scanner does not model, so the caller
        /// keeps the plain translation instead of silently producing wrong matches.
        /// </summary>
        internal static string ToCanonicalPattern(string p, out Dictionary<string, int> names, out int count)
        {
            names = new Dictionary<string, int>(StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder(p.Length + 16);
            int num = 0;

            for (int i = 0; i < p.Length;)
            {
                char c = p[i];

                if (c == '\\')
                {
                    // \1..\99 become a reference to the canonical group holding that number.
                    if (i + 1 < p.Length && p[i + 1] >= '1' && p[i + 1] <= '9')
                    {
                        int j = i + 1;
                        int g = 0;
                        while (j < p.Length && j < i + 3 && p[j] >= '0' && p[j] <= '9')
                        {
                            g = g * 10 + (p[j] - '0');
                            j++;
                        }
                        sb.Append("\\k<__p").Append(g).Append('>');
                        i = j;
                        continue;
                    }
                    sb.Append(c);
                    if (i + 1 < p.Length)
                    {
                        sb.Append(p[i + 1]);
                        i += 2;
                    }
                    else
                        i++;
                    continue;
                }

                if (c == '[')
                {
                    // A '(' inside a character class is literal.
                    sb.Append(c);
                    i++;
                    if (i < p.Length && p[i] == ']')
                    {
                        sb.Append(p[i]);
                        i++;
                    }
                    while (i < p.Length && p[i] != ']')
                    {
                        if (p[i] == '\\' && i + 1 < p.Length)
                        {
                            sb.Append(p[i]).Append(p[i + 1]);
                            i += 2;
                        }
                        else
                        {
                            sb.Append(p[i]);
                            i++;
                        }
                    }
                    if (i >= p.Length)
                    {
                        names = null;
                        count = 0;
                        return null;
                    }
                    sb.Append(p[i]);
                    i++;
                    continue;
                }

                if (c == '(' && i + 1 < p.Length && p[i + 1] == '?')
                {
                    if (i + 2 < p.Length && p[i + 2] == 'P')
                    {
                        if (i + 3 < p.Length && p[i + 3] == '<')
                        {
                            int close = p.IndexOf('>', i + 4);
                            if (close < 0)
                            {
                                names = null;
                                count = 0;
                                return null;
                            }
                            num++;
                            names[p.Substring(i + 4, close - i - 4)] = num;
                            sb.Append("(?<__p").Append(num).Append('>');
                            i = close + 1;
                            continue;
                        }
                        if (i + 3 < p.Length && p[i + 3] == '=')
                        {
                            int close = p.IndexOf(')', i + 4);
                            if (close < 0)
                            {
                                names = null;
                                count = 0;
                                return null;
                            }
                            // Deferred: the name may be declared later in the pattern.
                            sb.Append('\x01').Append(p.Substring(i + 4, close - i - 4)).Append('\x02');
                            i = close + 1;
                            continue;
                        }
                        names = null;
                        count = 0;
                        return null;
                    }
                    if (i + 2 < p.Length && (p[i + 2] == '(' || p[i + 2] == '#'))
                    {
                        // Conditionals and comments are outside what this scanner models.
                        names = null;
                        count = 0;
                        return null;
                    }
                    // (?: (?= (?! (?<= (?<! (?i: are non-capturing; copy the prefix verbatim.
                    sb.Append(c).Append(p[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == '(')
                {
                    num++;
                    sb.Append("(?<__p").Append(num).Append('>');
                    i++;
                    continue;
                }

                sb.Append(c);
                i++;
            }

            count = num;
            string result = sb.ToString();
            if (result.IndexOf('\x01') < 0)
                return result;

            var resolved = new System.Text.StringBuilder(result.Length);
            for (int i = 0; i < result.Length;)
            {
                if (result[i] == '\x01')
                {
                    int end = result.IndexOf('\x02', i + 1);
                    if (end < 0)
                    {
                        names = null;
                        count = 0;
                        return null;
                    }
                    if (!names.TryGetValue(result.Substring(i + 1, end - i - 1), out int g))
                    {
                        names = null;
                        count = 0;
                        return null;
                    }
                    resolved.Append("\\k<__p").Append(g).Append('>');
                    i = end + 1;
                    continue;
                }
                resolved.Append(result[i]);
                i++;
            }
            return resolved.ToString();
        }

        // One place decides whether a pattern is canonically named: only patterns that actually
        // name a group need it, because when every group is unnamed .NET's numbering and
        // CPython's already agree (measured).
        internal static Regex BuildRegex(string pattern, RegexOptions flags,
            out Dictionary<string, int> names, out int count)
        {
            if (pattern != null && pattern.IndexOf("(?P", StringComparison.Ordinal) >= 0)
            {
                var canonical = ToCanonicalPattern(pattern, out names, out count);
                if (canonical != null)
                    return new Regex(canonical, flags);
            }
            names = null;
            count = 0;
            return new Regex(ToDotNetPattern(pattern), flags);
        }

        // Group numbers seen by Python are CPython positional numbers; with a canonical map the
        // group is reached by the name carrying that number.
        internal static Group GroupAt(Match m, int pyNumber, Dictionary<string, int> names) =>
            names == null ? m.Groups[pyNumber] : m.Groups["__p" + pyNumber];

        internal static string ToDotNetPattern(string p)
        {
            if (p == null || p.IndexOf("(?P", StringComparison.Ordinal) < 0)
                return p;
            // (?P=name) named backreference -> \k<name>.
            // C# trap: the replacement of Regex.Replace is NOT a regex, only "$" is special, so
            // one backslash is already literal. Writing "\\k" emitted a literal backslash plus
            // 'k', which .NET read as an escaped 'k' and the pattern silently never matched.
            p = System.Text.RegularExpressions.Regex.Replace(p, @"\(\?P=(\w+)\)", @"\k<$1>");
            // (?P<name>... named group -> (?<name>...
            p = p.Replace("(?P<", "(?<");
            return p;
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
        // CPython group number -> nothing (the number is in the canonical name), plus the
        // user-visible name -> number table. null means the pattern has no named groups.
        private readonly Dictionary<string, int> _names;
        private readonly int _count;

        public PySrePattern(string pattern, RegexOptions flags)
        {
            _pattern = pattern;
            _flags = flags;
            _regex = _SreModule.BuildRegex(pattern, flags, out _names, out _count);
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
                case "fullmatch":
                    return new PyBuiltinFunction("fullmatch", FullMatch);
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
            return match.Success ? new PySreMatch(match, text.Value, 0, _names, _count) : PyNone.Instance;
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:1827-1897 (pattern_fullmatch_impl)
        /// Signature: fullmatch(string, pos=0, endpos=sys.maxsize)
        /// Try to apply the pattern to all of the string, returning a Match object, or None if no match was found.
        /// </summary>
        private PyObject FullMatch(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("fullmatch() missing 1 required positional argument: 'string'");

            var text = args[0].ToStr();
            int pos = args.Length > 1 && args[1] is PyInt posInt ? (int)posInt.Value : 0;
            int endpos = args.Length > 2 && args[2] is PyInt endposInt ? (int)endposInt.Value : text.Value.Length;

            // Clamp pos and endpos to valid range
            if (pos < 0) pos = 0;
            if (pos > text.Value.Length) pos = text.Value.Length;
            if (endpos < pos) endpos = pos;
            if (endpos > text.Value.Length) endpos = text.Value.Length;

            // fullmatch requires the entire string (or substring) to match
            string substring = text.Value.Substring(pos, endpos - pos);
            var match = _regex.Match(substring);

            // Check if match covers the entire substring
            if (match.Success && match.Index == 0 && match.Length == substring.Length)
            {
                return new PySreMatch(match, text.Value, pos, _names, _count);
            }
            return PyNone.Instance;
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
            return match.Success ? new PySreMatch(match, text.Value, 0, _names, _count) : PyNone.Instance;
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
                // CPython findall: no groups -> whole match; exactly ONE group ->
                // that group's string; multiple groups -> tuple of groups 1..n.
                int ncap = _names == null ? match.Groups.Count - 1 : _count;
                if (ncap > 1)
                {
                    var groups = new List<PyObject>();
                    for (int i = 1; i <= ncap; i++)
                    {
                        groups.Add(new PyString(_SreModule.GroupAt(match, i, _names).Value));
                    }
                    result.Add(new PyTuple(groups.ToArray()));
                }
                else if (ncap == 1)
                {
                    result.Add(new PyString(_SreModule.GroupAt(match, 1, _names).Value));
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
                matchObjects.Add(new PySreMatch(match, text.Value, 0, _names, _count));
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

            PyObject replObj = args[0];
            var text = args[1].ToStr();
            // CPython 3.12: count=0 means unlimited replacements
            // .NET Regex.Replace with count=0 means NO replacements
            int count = 0;
            if (args.Length > 2 && args[2] is PyInt countInt)
                count = (int)countInt.Value;

            // CPython: repl may be a callable receiving the Match object.
            if (replObj.IsCallable())
            {
                MatchEvaluator evaluator = m =>
                {
                    var pyMatch = new PySreMatch(m, text.Value, 0, _names, _count);
                    var r = replObj.Call(new PyObject[] { pyMatch }, null);
                    return r is PyString rs ? rs.Value : r.ToStr().Value;
                };
                // In Python, count <= 0 means replace all
                var callableResult = count <= 0 ?
                    _regex.Replace(text.Value, evaluator) :
                    _regex.Replace(text.Value, evaluator, count);
                return new PyString(callableResult);
            }

            var repl = _SreModule.ToDotNetReplacement(replObj.ToStr().Value, _names);
            // In Python, count <= 0 means replace all
            var result = count <= 0 ?
                _regex.Replace(text.Value, repl) :
                _regex.Replace(text.Value, repl, count);

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

            PyObject replObj = args[0];
            var text = args[1].ToStr();
            // CPython 3.12: count=0 means unlimited replacements
            int count = 0;
            if (args.Length > 2 && args[2] is PyInt countInt)
                count = (int)countInt.Value;

            var matches = _regex.Matches(text.Value).Count;
            string result;
            if (replObj.IsCallable())
            {
                MatchEvaluator evaluator = m =>
                {
                    var pyMatch = new PySreMatch(m, text.Value, 0, _names, _count);
                    var r = replObj.Call(new PyObject[] { pyMatch }, null);
                    return r is PyString rs ? rs.Value : r.ToStr().Value;
                };
                result = count <= 0 ?
                    _regex.Replace(text.Value, evaluator) :
                    _regex.Replace(text.Value, evaluator, count);
            }
            else
            {
                var repl = _SreModule.ToDotNetReplacement(replObj.ToStr().Value, _names);
                // In Python, count <= 0 means replace all
                result = count <= 0 ?
                    _regex.Replace(text.Value, repl) :
                    _regex.Replace(text.Value, repl, count);
            }

            var actualSubstitutions = count <= 0 ? matches : Math.Min(matches, count);
            return new PyTuple(new PyString(result), new PyInt(actualSubstitutions));
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:1397-1485 (pattern_split)
        /// Python maxsplit semantics:
        /// - maxsplit=0: unlimited splits (default)
        /// - maxsplit=N: split at most N times, resulting in at most N+1 substrings
        /// C# Regex.Split semantics:
        /// - Split(input): unlimited splits
        /// - Split(input, count): return at most 'count' substrings
        /// </summary>
        private PyObject Split(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("split() missing 1 required positional argument: 'string'");

            var text = args[0].ToStr();
            // Python: maxsplit=0 means unlimited, maxsplit=N means at most N splits
            // C#: Split(text, count) returns at most 'count' parts, so we need count = maxsplit + 1
            int maxsplit = 0;
            if (args.Length > 1 && args[1] is PyInt maxInt)
            {
                maxsplit = (int)maxInt.Value;
            }

            string[] parts;
            if (maxsplit <= 0)
            {
                // Unlimited splits
                parts = _regex.Split(text.Value);
            }
            else
            {
                // At most maxsplit splits → maxsplit+1 parts
                parts = _regex.Split(text.Value, maxsplit + 1);
            }

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
        private readonly int _posOffset;  // Offset for fullmatch when substring is used
        // null when the pattern has no named groups, in which case .NET's capture ids already
        // match CPython's numbering; otherwise every group is reached through __p<number>.
        private readonly Dictionary<string, int> _names;
        private readonly int _count;

        public PySreMatch(Match match, string text, int posOffset = 0,
            Dictionary<string, int> names = null, int count = 0)
        {
            _match = match;
            _string = text;
            _posOffset = posOffset;
            _names = names;
            _count = count;
        }

        // Group numbers coming from Python are CPython positional numbers.
        private Group GroupByNumber(long n)
        {
            if (_names == null)
            {
                if (n < 0 || n >= _match.Groups.Count)
                    throw PyIndexError.Create("no such group");
                return _match.Groups[(int)n];
            }
            if (n < 0 || n > _count)
                throw PyIndexError.Create("no such group");
            // Group 0 is the whole match and has no canonical name.
            if (n == 0)
                return _match.Groups[0];
            return _match.Groups["__p" + (int)n];
        }

        private Group GroupByName(string name)
        {
            if (_names != null)
            {
                if (!_names.TryGetValue(name, out int number))
                    throw PyIndexError.Create("no such group");
                return _match.Groups["__p" + number];
            }
            try
            {
                return _match.Groups[name];
            }
            catch
            {
                throw PyIndexError.Create("no such group");
            }
        }

        // CPython match_group_impl: a group that did not participate yields None, not the empty
        // string, so Success has to be consulted before the value is read.
        private PyObject GroupValue(Group group) =>
            group.Success ? new PyString(group.Value) : PyNone.Instance;

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
                    return GroupValue(GroupByNumber((long)groupNum.Value));
                else if (args[0] is PyString groupName)
                    return GroupValue(GroupByName(groupName.Value));
            }

            var result = new List<PyObject>();
            foreach (var arg in args)
            {
                if (arg is PyInt groupNum)
                {
                    result.Add(GroupValue(GroupByNumber((long)groupNum.Value)));
                }
                else if (arg is PyString groupName)
                {
                    result.Add(GroupValue(GroupByName(groupName.Value)));
                }
            }

            return result.Count == 1 ? result[0] : new PyTuple(result.ToArray());
        }

        private PyObject Groups(PyObject[] args)
        {
            var defaultValue = args.Length > 0 ? args[0] : PyNone.Instance;
            var result = new List<PyObject>();

            // CPython reports groups in positional order, which is what the canonical names
            // encode; .NET capture-id order is not the same thing.
            int ncap = _names == null ? _match.Groups.Count - 1 : _count;
            for (int i = 1; i <= ncap; i++)
            {
                var group = _SreModule.GroupAt(_match, i, _names);
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

            if (_names != null)
            {
                // The .NET name table now holds canonical names; report the user's names, in
                // declaration order, as CPython's groupindex does.
                foreach (var entry in _names)
                {
                    var group = _match.Groups["__p" + entry.Value];
                    result.SetItem(new PyString(entry.Key),
                        group.Success ? new PyString(group.Value) : defaultValue);
                }
                return result;
            }

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

            return new PyInt(GroupByNumber(groupNum).Index);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:1134-1187 (match_end_impl)
        /// </summary>
        private PyObject End(PyObject[] args)
        {
            var groupNum = args.Length > 0 && args[0] is PyInt groupInt ? (int)groupInt.Value : 0;

            var group = GroupByNumber(groupNum);
            return new PyInt(group.Index + group.Length);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:1227-1267 (match_span_impl)
        /// </summary>
        private PyObject Span(PyObject[] args)
        {
            var groupNum = args.Length > 0 && args[0] is PyInt groupInt ? (int)groupInt.Value : 0;

            var group = GroupByNumber(groupNum);
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
