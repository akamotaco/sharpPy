using System;
using System.Globalization;

namespace SharpPy.Modules
{
    /// <summary>
    /// _locale (Modules/_localemodule.c) as it is on Windows: setlocale over the C runtime's state of
    /// the five categories, localeconv, strcoll, strxfrm, getencoding and _getdefaultlocale. The "C"
    /// locale, every category's at startup but LC_CTYPE's (which _Py_SetLocaleFromEnv sets to the
    /// user's), is the C runtime's exactly; another locale reads .NET's culture data where the C
    /// runtime reads Windows', and is named the way the C runtime names it ("Korean_Korea.949",
    /// "ko-KR") (docs/todo.md). The posix module (nl_langinfo, gettext, glibc's numbers) is not
    /// here: there locale.py keeps its emulation.
    /// </summary>
    internal static class LocaleModule
    {
        private const int LC_ALL = 0, LC_COLLATE = 1, LC_CTYPE = 2, LC_MONETARY = 3, LC_NUMERIC = 4, LC_TIME = 5;
        private const int CHAR_MAX = 127;
        private static readonly string[] CategoryNames = { "LC_ALL", "LC_COLLATE", "LC_CTYPE", "LC_MONETARY", "LC_NUMERIC", "LC_TIME" };

        // The C runtime's locale of each category: its name, and the culture behind it (null for "C").
        private static readonly string[] Names = { null, "C", "C", "C", "C", "C" };
        private static readonly CultureInfo[] Cultures = new CultureInfo[6];
        private static PyType _error;
        private static bool _started;

        internal static PyModule Create()
        {
            var module = new PyModule("_locale");
            // PyErr_NewException("locale.Error", NULL, NULL)
            _error ??= (PyType)PyType.TypeType.Call(new PyObject[]
            {
                PyString.From("Error"), PyTuple.From(new PyObject[] { PyType.ExceptionType }),
                Dict(("__module__", PyString.From("locale"))),
            }, null);
            module.ModuleDict["Error"] = _error;
            for (int i = 0; i < CategoryNames.Length; i++)
                module.ModuleDict[CategoryNames[i]] = PyInt.From(i);
            module.ModuleDict["CHAR_MAX"] = PyInt.From(CHAR_MAX);
            module.ModuleDict["setlocale"] = new PyBuiltinFunction("setlocale", SetLocale);
            module.ModuleDict["localeconv"] = new PyBuiltinFunction("localeconv", args => LocaleConv());
            module.ModuleDict["strcoll"] = new PyBuiltinFunction("strcoll", StrColl);
            module.ModuleDict["strxfrm"] = PyArgs.MethO("_locale", "strxfrm", args => StrXfrm(args[0]));
            module.ModuleDict["getencoding"] = new PyBuiltinFunction("getencoding", args => PyString.From(PyLocale.LocaleEncoding()));
            module.ModuleDict["_getdefaultlocale"] = new PyBuiltinFunction("_getdefaultlocale", args => GetDefaultLocale());
            return module;
        }

        /// <summary>_Py_SetLocaleFromEnv(LC_CTYPE), which the interpreter's startup does.</summary>
        internal static void ConfigureLocale()
        {
            if (_started || !OperatingSystem.IsWindows())
                return;
            _started = true;
            Set(LC_CTYPE, "");
        }

        private static PyDict Dict(params (string Key, PyObject Value)[] items)
        {
            var dict = new PyDict();
            foreach (var (key, value) in items)
                dict.SetItem(PyString.From(key), value);
            return dict;
        }

        private static Exception Error(string message) =>
            new PythonException((PyBaseException)_error.Call(new PyObject[] { PyString.From(message) }, null));

        // _locale_setlocale_impl: (category, locale=None, /)
        private static PyObject SetLocale(PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("_locale.setlocale() takes no keyword arguments");
            PyArgs.ParseTuple("setlocale", args, 1, 2);
            int category = PyArgs.Int(args[0]);
            string locale = args.Length < 2 || args[1] is PyNone ? null
                : args[1] is PyString text ? text.Value
                : throw PyArgs.BadArgument("setlocale", "argument 2", "str or None", args[1]);
            ConfigureLocale();
            if (category < LC_ALL || category > LC_TIME)
                throw Error("invalid locale category");
            if (locale != null && !Set(category, locale))
                throw Error("unsupported locale setting");
            return PyString.From(Query(category));
        }

        // setlocale(category, NULL): LC_ALL is one name when the categories agree, else each by name.
        private static string Query(int category)
        {
            if (category != LC_ALL)
                return Names[category];
            bool same = true;
            for (int i = LC_COLLATE + 1; i <= LC_TIME; i++)
                same &= Names[i] == Names[LC_COLLATE];
            if (same)
                return Names[LC_COLLATE];
            var parts = new string[5];
            for (int i = LC_COLLATE; i <= LC_TIME; i++)
                parts[i - 1] = CategoryNames[i] + "=" + Names[i];
            return string.Join(";", parts);
        }

        // setlocale(category, locale): false when the C runtime would answer NULL.
        private static bool Set(int category, string locale)
        {
            if (category == LC_ALL && locale.Contains('='))
            {
                // the form LC_ALL's query gives: each category by name
                var assignments = new (int, string, CultureInfo)[5];
                int count = 0;
                foreach (var part in locale.Split(';'))
                {
                    int equals = part.IndexOf('=');
                    int index = equals < 0 ? -1 : Array.IndexOf(CategoryNames, part.Substring(0, equals));
                    if (index <= LC_ALL || !Resolve(part.Substring(equals + 1), out var partName, out var partCulture))
                        return false;
                    assignments[count++] = (index, partName, partCulture);
                }
                for (int i = 0; i < count; i++)
                {
                    Names[assignments[i].Item1] = assignments[i].Item2;
                    Cultures[assignments[i].Item1] = assignments[i].Item3;
                }
                return true;
            }
            if (!Resolve(locale, out var name, out var culture))
                return false;
            for (int i = LC_COLLATE; i <= LC_TIME; i++)
            {
                if (category == LC_ALL || category == i)
                {
                    Names[i] = name;
                    Cultures[i] = culture;
                }
            }
            return true;
        }

        // The C runtime's reading of a locale name: "C"; "" (the user's); a BCP-47 name ("ko-KR"),
        // named as given; "Language_Country.codepage" in English names, or a part of it; a code page
        // alone (".949", ".utf8") with the user's language.
        private static bool Resolve(string locale, out string name, out CultureInfo culture)
        {
            name = null;
            culture = null;
            if (locale == "C")
            {
                name = "C";
                return true;
            }
            if (locale.Length == 0)
                locale = "." + PyCodePage.GetACP();
            string codePage = null;
            int dot = locale.IndexOf('.');
            if (dot >= 0)
            {
                codePage = locale.Substring(dot + 1);
                locale = locale.Substring(0, dot);
                if (codePage.Length == 0)
                    return false;
            }
            if (locale.Length == 0)
                culture = CultureInfo.CurrentCulture;
            else if (codePage == null && IsLocaleTag(locale))
            {
                // A tag-shaped name ("ko-KR", "en_US", "xx_YY"): Windows resolves it, an unknown one
                // to a custom locale, and the C runtime keeps the name as given.
                name = locale;
                culture = TryCulture(locale.Replace('_', '-'), out var tagged) ? tagged : CultureInfo.InvariantCulture;
                return true;
            }
            else if ((culture = FindByEnglishName(locale)) == null)
                return false;
            string page = codePage switch
            {
                null => culture.TextInfo.ANSICodePage.ToString(CultureInfo.InvariantCulture),
                var p when p.Equals("utf8", StringComparison.OrdinalIgnoreCase) || p.Equals("utf-8", StringComparison.OrdinalIgnoreCase) => "utf8",
                var p when p.Equals("ACP", StringComparison.OrdinalIgnoreCase) => culture.TextInfo.ANSICodePage.ToString(CultureInfo.InvariantCulture),
                var p when p.Equals("OCP", StringComparison.OrdinalIgnoreCase) => culture.TextInfo.OEMCodePage.ToString(CultureInfo.InvariantCulture),
                var p when int.TryParse(p, out _) => p,
                _ => null,
            };
            if (page == null)
                return false;
            name = CrtName(culture) + "." + page;
            return true;
        }

        // language[-_]subtag...: two or three letters, then subtags of two to eight letters or digits.
        private static bool IsLocaleTag(string name) =>
            System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z]{2,3}([-_][A-Za-z0-9]{2,8})*$");

        private static bool TryCulture(string tag, out CultureInfo culture)
        {
            try
            {
                culture = CultureInfo.GetCultureInfo(tag, predefinedOnly: true);
                return true;
            }
            catch (CultureNotFoundException)
            {
                culture = null;
                return false;
            }
        }

        // "Language_Country": the English names of the culture's language and region.
        private static string CrtName(CultureInfo culture)
        {
            string language = culture.IsNeutralCulture ? culture.EnglishName : culture.Parent.EnglishName;
            string country;
            try
            {
                country = new RegionInfo(culture.Name).EnglishName;
            }
            catch (ArgumentException)
            {
                return language;
            }
            return language + "_" + country;
        }

        // "Language" or "Language_Country" in English: a language alone is its default country's
        // (Korean is ko-KR, English en-US).
        private static CultureInfo FindByEnglishName(string locale)
        {
            int underscore = locale.IndexOf('_');
            string language = underscore < 0 ? locale : locale.Substring(0, underscore);
            string country = underscore < 0 ? null : locale.Substring(underscore + 1);
            if (country == null)
            {
                foreach (var neutral in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
                {
                    if (neutral.Name.Length == 0 || !neutral.EnglishName.Equals(language, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        return CultureInfo.CreateSpecificCulture(neutral.Name);
                    }
                    catch (ArgumentException)
                    {
                        return null;
                    }
                }
                return null;
            }
            foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                if (!culture.Parent.EnglishName.Equals(language, StringComparison.OrdinalIgnoreCase))
                    continue;
                RegionInfo region;
                try
                {
                    region = new RegionInfo(culture.Name);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                if (region.EnglishName.Equals(country, StringComparison.OrdinalIgnoreCase))
                    return culture;
            }
            return null;
        }

        // _locale_localeconv_impl: the C locale's lconv, or the culture's numbers for LC_NUMERIC and LC_MONETARY.
        private static PyObject LocaleConv()
        {
            ConfigureLocale();
            var monetary = Cultures[LC_MONETARY]?.NumberFormat;
            var numeric = Cultures[LC_NUMERIC]?.NumberFormat;
            PyObject Int(int value) => PyInt.From(value);
            PyObject Str(string value) => PyString.From(value);
            var result = new PyDict();
            void Put(string key, PyObject value) => result.SetItem(PyString.From(key), value);
            if (monetary == null)
            {
                Put("int_curr_symbol", Str(""));
                Put("currency_symbol", Str(""));
                Put("mon_decimal_point", Str(""));
                Put("mon_thousands_sep", Str(""));
                Put("mon_grouping", new PyList());
                Put("positive_sign", Str(""));
                Put("negative_sign", Str(""));
                foreach (var key in new[] { "int_frac_digits", "frac_digits", "p_cs_precedes", "p_sep_by_space", "n_cs_precedes", "n_sep_by_space", "p_sign_posn", "n_sign_posn" })
                    Put(key, Int(CHAR_MAX));
            }
            else
            {
                string isoCurrency;
                try
                {
                    isoCurrency = new RegionInfo(Cultures[LC_MONETARY].Name).ISOCurrencySymbol;
                }
                catch (ArgumentException)
                {
                    isoCurrency = "";
                }
                Put("int_curr_symbol", Str(isoCurrency));
                Put("currency_symbol", Str(monetary.CurrencySymbol));
                Put("mon_decimal_point", Str(monetary.CurrencyDecimalSeparator));
                Put("mon_thousands_sep", Str(monetary.CurrencyGroupSeparator));
                Put("mon_grouping", Grouping(monetary.CurrencyGroupSizes));
                Put("positive_sign", Str(""));
                Put("negative_sign", Str(monetary.NegativeSign));
                Put("int_frac_digits", Int(monetary.CurrencyDecimalDigits));
                Put("frac_digits", Int(monetary.CurrencyDecimalDigits));
                // CurrencyPositivePattern: 0 "$n", 1 "n$", 2 "$ n", 3 "n $"
                int positive = monetary.CurrencyPositivePattern;
                Put("p_cs_precedes", Int(positive is 0 or 2 ? 1 : 0));
                Put("p_sep_by_space", Int(positive is 2 or 3 ? 1 : 0));
                int negative = monetary.CurrencyNegativePattern;
                Put("n_cs_precedes", Int(negative is 0 or 1 or 2 or 9 or 12 or 14 ? 1 : 0));
                Put("n_sep_by_space", Int(negative is 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 ? 1 : 0));
                Put("p_sign_posn", Int(1));
                Put("n_sign_posn", Int(negative is 0 or 4 or 14 or 15 ? 0 : negative is 1 or 5 or 8 or 9 or 12 ? 1 : negative is 3 or 7 or 10 or 13 ? 2 : negative is 2 or 6 ? 3 : 4));
            }
            if (numeric == null)
            {
                Put("decimal_point", Str("."));
                Put("thousands_sep", Str(""));
                Put("grouping", new PyList());
            }
            else
            {
                Put("decimal_point", Str(numeric.NumberDecimalSeparator));
                Put("thousands_sep", Str(numeric.NumberGroupSeparator));
                Put("grouping", Grouping(numeric.NumberGroupSizes));
            }
            return result;
        }

        /// <summary>localeconv()'s LC_NUMERIC part: decimal_point, thousands_sep and the grouping sizes.</summary>
        internal static (string Point, string Separator, int[] Grouping) Numeric()
        {
            ConfigureLocale();
            var numeric = Cultures[LC_NUMERIC]?.NumberFormat;
            if (numeric == null)
                return (".", "", Array.Empty<int>());
            var sizes = numeric.NumberGroupSizes;
            var grouping = new int[sizes.Length + (sizes.Length > 0 && sizes[^1] != 0 ? 1 : 0)];
            sizes.CopyTo(grouping, 0);
            return (numeric.NumberDecimalSeparator, numeric.NumberGroupSeparator, grouping);
        }

        // copy_grouping: the C runtime's "3;0" is [3, 0].
        private static PyList Grouping(int[] sizes)
        {
            var list = new PyList();
            foreach (var size in sizes)
                list.Append(PyInt.From(size));
            if (sizes.Length > 0 && sizes[^1] != 0)
                list.Append(PyInt.From(0));
            return list;
        }

        // _locale_strcoll_impl: wcscoll - wcscmp in the C locale, the culture's comparison else.
        private static PyObject StrColl(PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("strcoll() takes no keyword arguments");
            PyArgs.ParseTuple("strcoll", args, 2, 2);
            string a = args[0] is PyString s1 ? s1.Value : throw PyArgs.BadArgument("strcoll", "argument 1", "str", args[0]);
            string b = args[1] is PyString s2 ? s2.Value : throw PyArgs.BadArgument("strcoll", "argument 2", "str", args[1]);
            ConfigureLocale();
            var culture = Cultures[LC_COLLATE];
            int order = culture == null ? string.CompareOrdinal(a, b) : culture.CompareInfo.Compare(a, b);
            return PyInt.From(Math.Sign(order));
        }

        // _locale_strxfrm_impl: wcsxfrm - the string itself in the C locale; else a text whose
        // ordinal order is the culture's order (its sort key).
        private static PyObject StrXfrm(PyObject value)
        {
            string text = value is PyString s ? s.Value : throw PyArgs.BadArgument("strxfrm", "argument", "str", value);
            if (text.IndexOf('\0') >= 0)
                throw PyValueError.Create("embedded null character");
            ConfigureLocale();
            var culture = Cultures[LC_COLLATE];
            if (culture == null)
                return value;
            var key = culture.CompareInfo.GetSortKey(text).KeyData;
            var chars = new char[key.Length];
            for (int i = 0; i < key.Length; i++)
                chars[i] = (char)(key[i] + 1);
            return PyString.From(new string(chars));
        }

        // _locale__getdefaultlocale_impl: LOCALE_SISO639LANGNAME_LOCALE_SISO3166CTRYNAME of the user's
        // locale, and "cp" with the ANSI code page.
        private static PyObject GetDefaultLocale()
        {
            var culture = CultureInfo.CurrentCulture;
            string encoding = "cp" + (uint)PyCodePage.GetACP();
            try
            {
                var region = new RegionInfo(culture.Name);
                return PyTuple.From(new PyObject[] { PyString.From(culture.TwoLetterISOLanguageName + "_" + region.TwoLetterISORegionName), PyString.From(encoding) });
            }
            catch (ArgumentException)
            {
                return PyTuple.From(new PyObject[] { PyNone.Instance, PyString.From(encoding) });
            }
        }
    }
}
