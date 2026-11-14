using System;

namespace SharpPy.Modules
{
    /// <summary>
    /// _winapi (Modules/_winapi.c), on Windows only as there. Its functions call the Win32 API,
    /// which SharpPy, being pure .NET for Godot, cannot: it has every name of CPython's module
    /// (WinapiModule.g.cs), and the functions .NET can answer work - GetACP (encodings' mbcs alias
    /// and the locale encoding) and NeedCurrentDirectoryForExePath (shutil.which). Every other
    /// function raises OSError when called, so a module like subprocess imports and then refuses
    /// at the call. CopyFile2 is left out: shutil asks for it with hasattr and otherwise copies the
    /// file itself, which also reads Godot's paths (docs/todo.md).
    /// </summary>
    internal static partial class WinapiModule
    {
        internal static PyModule Create()
        {
            var module = new PyModule("_winapi");
            foreach (var (name, value) in Constants)
                module.ModuleDict[name] = PyInt.From(System.Numerics.BigInteger.Parse(value));
            foreach (var (name, value) in TextConstants)
                module.ModuleDict[name] = value == null ? PyNone.Instance : PyString.From(value);
            foreach (var name in Functions)
            {
                if (name == "CopyFile2")
                    continue;
                string function = name;
                module.ModuleDict[name] = new PyBuiltinFunction(name, (args, kwargs) =>
                    throw PyOSError.Create($"_winapi.{function}() is not available: SharpPy has no Win32 API"));
            }
            // _winapi_GetACP_impl
            module.ModuleDict["GetACP"] = new PyBuiltinFunction("GetACP", args =>
            {
                if (args.Length != 0)
                    throw PyTypeError.Create($"GetACP() takes no arguments ({args.Length} given)");
                return PyInt.From(PyCodePage.GetACP());
            });
            // _winapi_NeedCurrentDirectoryForExePath_impl: the documented rule of the API - a name with
            // a backslash always searches the current directory; otherwise it is searched unless
            // NoDefaultCurrentDirectoryInExePath is set.
            module.ModuleDict["NeedCurrentDirectoryForExePath"] = PyArgs.MethO("_winapi", "NeedCurrentDirectoryForExePath", args =>
            {
                string name = args[0] is PyString text ? text.Value
                    : throw PyArgs.BadArgument("NeedCurrentDirectoryForExePath", "argument", "str", args[0]);
                if (name.IndexOf('\0') >= 0)
                    throw PyValueError.Create("embedded null character");
                bool need = name.Contains('\\') || Environment.GetEnvironmentVariable("NoDefaultCurrentDirectoryInExePath") == null;
                return PyBool.FromBool(need);
            });
            module.ModuleDict["LCMapStringEx"] = new PyBuiltinFunction("LCMapStringEx", LCMapStringEx);
            return module;
        }

        private const uint LCMAP_LOWERCASE = 0x100, LCMAP_UPPERCASE = 0x200, LCMAP_LINGUISTIC_CASING = 0x01000000;
        private const uint LCMAP_SORTKEY = 0x400, LCMAP_BYTEREV = 0x800, LCMAP_HASH = 0x40000, LCMAP_SORTHANDLE = 0x20000000;

        /// <summary>
        /// _winapi_LCMapStringEx_impl (ntpath.normcase): the case mappings, which .NET has -
        /// without LCMAP_LINGUISTIC_CASING the locale-independent simple mapping of each character,
        /// with it the locale's own rules. The other transforms raise OSError, the sort keys
        /// ValueError as there.
        /// </summary>
        private static PyObject LCMapStringEx(PyObject[] args, PyDict kwargs)
        {
            var p = PyArgs.Unpack("LCMapStringEx", args, kwargs, new[] { "locale", "flags", "src" }, 0, 3, 3);
            // LPCWSTR converter
            string locale = p[0] is PyString name ? name.Value : throw PyTypeError.Create($"argument must be str, not {p[0].GetPyType().FullName}");
            if (locale.IndexOf('\0') >= 0)
                throw PyValueError.Create("embedded null character");
            uint flags = PyArgs.UnsignedIntMask(p[1]);
            string source = p[2] is PyString text ? text.Value : throw PyArgs.BadArgument("LCMapStringEx", "argument 3", "str", p[2]);
            if ((flags & (LCMAP_SORTHANDLE | LCMAP_HASH | LCMAP_BYTEREV | LCMAP_SORTKEY)) != 0)
                throw PyValueError.Create("unsupported flags");
            uint casing = flags & (LCMAP_LOWERCASE | LCMAP_UPPERCASE);
            if (flags == 0 || casing == (LCMAP_LOWERCASE | LCMAP_UPPERCASE))
                throw PyOSError.CreateWindowsFileError(1004, null);   // ERROR_INVALID_FLAGS
            if (casing == 0 || (flags & ~(casing | LCMAP_LINGUISTIC_CASING)) != 0)
                throw PyOSError.Create("_winapi.LCMapStringEx() is not available for these flags: SharpPy has no Win32 API");
            if (source.Length == 0)
                throw PyOSError.CreateWindowsFileError(87, null);     // ERROR_INVALID_PARAMETER
            bool lower = casing == LCMAP_LOWERCASE;
            if ((flags & LCMAP_LINGUISTIC_CASING) == 0)
            {
                var mapped = new char[source.Length];
                for (int i = 0; i < source.Length; i++)
                    mapped[i] = lower ? SimpleLower(source[i]) : SimpleUpper(source[i]);
                return PyString.From(new string(mapped));
            }
            System.Globalization.CultureInfo culture;
            try
            {
                culture = locale switch
                {
                    "" => System.Globalization.CultureInfo.InvariantCulture,
                    "!x-sys-default-locale" => System.Globalization.CultureInfo.InstalledUICulture,
                    _ => System.Globalization.CultureInfo.GetCultureInfo(locale),
                };
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                throw PyOSError.CreateWindowsFileError(87, null);
            }
            return PyString.From(lower ? culture.TextInfo.ToLower(source) : culture.TextInfo.ToUpper(source));
        }

        // The file system's casing: one UTF-16 unit to one, U+0130 and U+0131 left as they are.
        private static char SimpleLower(char c) => c is '\u0130' or '\u0131' ? c : char.ToLowerInvariant(c);

        private static char SimpleUpper(char c) => c is '\u0130' or '\u0131' ? c : char.ToUpperInvariant(c);
    }
}
