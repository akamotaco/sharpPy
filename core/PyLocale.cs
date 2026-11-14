using System;

namespace SharpPy
{
    /// <summary>
    /// The locale encoding and the stdio encoding of CPython's startup: _Py_GetLocaleEncoding
    /// (Python/fileutils.c), and config_init_stdio_encoding / config_get_stdio_errors
    /// (Python/initconfig.c). open() without an encoding, locale.getencoding() and the standard
    /// streams take theirs from here; UTF-8 Mode is SharpPyConfig.Utf8Mode.
    /// </summary>
    internal static class PyLocale
    {
        /// <summary>
        /// _Py_GetLocaleEncoding: "cp" and the ANSI code page on Windows; UTF-8 where CPython forces
        /// it (Android, and Apple's systems, whose CODESET is always UTF-8); else the codeset of
        /// LC_CTYPE as nl_langinfo(CODESET) gives it, which the environment's locale names.
        /// </summary>
        internal static string LocaleEncoding()
        {
            if (OperatingSystem.IsWindows())
                return "cp" + (uint)PyCodePage.GetACP();
            if (OperatingSystem.IsAndroid() || OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
                return "utf-8";
            string locale = CtypeLocale();
            int dot = locale.IndexOf('.');
            if (dot < 0)
                return IsCLocale(locale) ? "ANSI_X3.4-1968" : "UTF-8";
            string codeset = locale.Substring(dot + 1);
            int at = codeset.IndexOf('@');
            if (at >= 0)
                codeset = codeset.Substring(0, at);
            return codeset.Length == 0 ? "utf-8" : codeset;
        }

        // setlocale(LC_CTYPE, "") on a posix system: LC_ALL, then LC_CTYPE, then LANG; "C" when none.
        private static string CtypeLocale()
        {
            foreach (var name in new[] { "LC_ALL", "LC_CTYPE", "LANG" })
                if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
                    return value;
            return "C";
        }

        private static bool IsCLocale(string locale) => locale is "C" or "POSIX";

        /// <summary>
        /// get_locale_info(LT_CURRENT_LOCALE), the 'n' presentation's: the current LC_NUMERIC's
        /// decimal point, thousands separator and grouping - the C locale's (".", "", none) unless
        /// _locale set another.
        /// </summary>
        internal static (string Point, string Separator, int[] Grouping) CurrentNumeric() =>
            OperatingSystem.IsWindows() ? Modules.LocaleModule.Numeric() : (".", "", Array.Empty<int>());

        /// <summary>
        /// _PyUnicode_InsertThousandsGrouping over GroupGenerator (Objects/stringlib/localeutil.h): the
        /// sizes from the right, 0 repeating the last, CHAR_MAX ending the grouping.
        /// </summary>
        internal static string Group(string digits, string separator, int[] grouping)
        {
            if (grouping.Length == 0 || separator.Length == 0 || digits.Length == 0)
                return digits;
            var parts = new System.Collections.Generic.List<string>();
            int remaining = digits.Length, index = 0, previous = 0;
            while (remaining > 0)
            {
                int size;
                int g = index < grouping.Length ? grouping[index] : 0;
                if (g == 0)
                    size = previous == 0 ? remaining : previous;
                else if (g >= 127)
                    size = remaining;
                else
                {
                    size = previous = g;
                    index++;
                }
                size = Math.Min(size, remaining);
                parts.Insert(0, digits.Substring(remaining - size, size));
                remaining -= size;
            }
            return string.Join(separator, parts);
        }

        /// <summary>PEP 540: UTF-8 Mode is on by default where LC_CTYPE is the C or POSIX locale (not Windows).</summary>
        internal static bool LegacyCLocale() => !OperatingSystem.IsWindows() && IsCLocale(CtypeLocale());

        /// <summary>config_init_stdio_encoding: PYTHONIOENCODING ("encoding[:errors]"), else UTF-8 Mode's or the locale's.</summary>
        internal static (string Encoding, string Errors) StdioEncoding()
        {
            string encoding = null, errors = null;
            var setting = Environment.GetEnvironmentVariable("PYTHONIOENCODING");
            if (!string.IsNullOrEmpty(setting))
            {
                int colon = setting.IndexOf(':');
                string name = colon < 0 ? setting : setting.Substring(0, colon);
                if (colon >= 0 && colon + 1 < setting.Length)
                    errors = setting.Substring(colon + 1);
                if (name.Length != 0)
                {
                    encoding = name;
                    // PYTHONIOENCODING=latin1 behaves as PYTHONIOENCODING=latin1:strict.
                    errors ??= "strict";
                }
            }
            encoding ??= SharpPyConfig.Utf8Mode ? "utf-8" : LocaleEncoding();
            errors ??= StdioErrors();
            return (encoding, errors);
        }

        // config_get_stdio_errors
        private static string StdioErrors()
        {
            if (SharpPyConfig.Utf8Mode)
                return "surrogateescape";
            // On Windows, always use surrogateescape by default; elsewhere so in the C and POSIX locales.
            if (OperatingSystem.IsWindows() || IsCLocale(CtypeLocale()))
                return "surrogateescape";
            return "strict";
        }
    }
}
