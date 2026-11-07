using System;
using System.Text;
using System.Globalization;
using System.Numerics;

namespace SharpPy
{
    /// <summary>
    /// Python str 타입 구현 - C# string을 기반으로 한 문자열
    /// </summary>
    /// <summary>
    /// Helpers implementing CPython's unicode_new argument grammar (Objects/unicodeobject.c).
    /// </summary>
    internal static class PyStringNewArguments
    {
        /// <param name="hasReceiver">true when args[0] is the class receiver of str.__new__, false
        /// for the ordinary str(...) type call.</param>
        internal static void Project(PyObject[] args, PyDict kwargs, bool hasReceiver,
            out PyObject objectArg, out PyObject encodingArg, out PyObject errorsArg)
        {
            int offset = hasReceiver ? 1 : 0;
            int count = System.Math.Max(0, args.Length - offset);
            if (count > 3)
                throw PyTypeError.Create($"str() takes at most 3 arguments ({count} given)");

            objectArg = count > 0 ? args[offset] : null;
            encodingArg = count > 1 ? args[offset + 1] : null;
            errorsArg = count > 2 ? args[offset + 2] : null;

            if (kwargs != null)
            {
                foreach (var entry in kwargs.GetInternalDict())
                {
                    var key = entry.Key.ToClrString();
                    switch (key)
                    {
                        case "object":
                            objectArg ??= entry.Value;
                            break;
                        case "encoding":
                            encodingArg ??= entry.Value;
                            break;
                        case "errors":
                            errorsArg ??= entry.Value;
                            break;
                        default:
                            throw PyTypeError.Create(
                                $"'{key}' is an invalid keyword argument for str()");
                    }
                }
            }
        }

        // unicode_new_impl: PyObject_Str of the object, the object itself as it comes back, or the
        // decoded text (from a decoder, through PyString.From).
        internal static PyString Build(PyObject objectArg, PyObject encodingArg, PyObject errorsArg)
        {
            bool decodingRequested = encodingArg != null || errorsArg != null;

            if (!decodingRequested)
            {
                if (objectArg == null)
                    return PyString.Empty;
                // Measured: str(65) is '65', not 'A' - unicode_new runs PyObject_Str, and chr() is a
                // different function. An earlier draft here converted ints with chr() and was wrong.
                return objectArg.ToStr();
            }

            // With encoding or errors present, CPython never accepts a str and never calls str().
            if (objectArg is PyString)
                throw PyTypeError.Create("decoding str is not supported");
            if (encodingArg != null && encodingArg is not PyString)
                throw PyTypeError.Create(
                    $"str() argument 'encoding' must be str, not {encodingArg.GetTypeName()}");
            if (errorsArg != null && errorsArg is not PyString)
                throw PyTypeError.Create(
                    $"str() argument 'errors' must be str, not {errorsArg.GetTypeName()}");

            byte[] data = RawBytesOrNull(objectArg);
            if (data == null)
                throw PyTypeError.Create(
                    $"decoding to str: need a bytes-like object, {objectArg.GetTypeName()} found");

            string encodingName = (encodingArg as PyString)?.Value ?? "utf-8";
            string errorName = (errorsArg as PyString)?.Value;
            return PyString.From(DecodeBytes(data, encodingName, errorName));
        }

        /// <summary>
        /// CPython resolves codecs through its alias table; .NET's name table is different enough that
        /// GetEncoding("utf8") fails here, so the names CPython's own tests and documentation use are
        /// mapped explicitly and anything else is CPython's LookupError.
        /// </summary>
        internal static string NormalizeCodecName(string name)
        {
            var lowered = name.ToLowerInvariant().Replace("_", "-");
            return lowered switch
            {
                "u8" or "utf" or "utf-8" or "utf8" => "utf-8",
                "latin" or "latin-1" or "latin1" or "iso-8859-1" or "iso8859-1" or "8859" or "cp819"
                    or "l1" => "latin-1",
                "ascii" or "us-ascii" or "646" or "ansi_x3.4-1968" => "ascii",
                // utf-16/utf-32 are BOM codecs in CPython; folding them onto -le dropped the
                // preamble, so "ab".encode("utf-16") answered b'a\x00b\x00'. Measured:
                //   "ab".encode("utf-16") -> b'\xff\xfea\x00b\x00'
                //   "ab".encode("utf-32") -> b'\xff\xfe\x00\x00a\x00\x00\x00b\x00\x00\x00'
                //   "".encode("utf-16")   -> b'\xff\xfe'
                "utf-16" or "utf16" or "u16" => "utf-16",
                "utf-16le" or "utf-16-le" => "utf-16-le",
                "utf-16be" or "utf-16-be" => "utf-16-be",
                "utf-32" or "utf32" or "u32" => "utf-32",
                "utf-32le" or "utf-32-le" => "utf-32-le",
                "utf-32be" or "utf-32-be" => "utf-32-be",
                "big5" or "big5-tw" or "csbig5" => "big5",
                "gbk" or "936" => "gbk",
                "shift_jis" or "shift-jis" or "sjis" or "csshiftjis" => "shift_jis",
                "koi8-r" or "koi8" => "koi8-r",
                "cp1250" or "windows-1250" => "cp1250",
                "cp1251" or "windows-1251" => "cp1251",
                "cp1252" or "windows-1252" => "cp1252",
                "idna" => "idna",
                _ => lowered,
            };
        }

        internal static System.Text.Encoding ResolveCodecPublic(string name) => ResolveCodec(name);

        private static System.Text.Encoding ResolveCodec(string name)
        {
            switch (NormalizeCodecName(name))
            {
                case "utf-8": return new System.Text.UTF8Encoding(false);
                case "latin-1": return System.Text.Encoding.Latin1;
                case "ascii": return new System.Text.ASCIIEncoding();
                // BOM codecs: little-endian with a detectable preamble, which is CPython's "utf-16".
                // (UnicodeEncoding's first argument is big-endian, not "emit a BOM"; passing true here
                // decoded BOM-less b'a\x00b\x00' as big-endian and left the BOM in the result.)
                case "utf-16": return new System.Text.UnicodeEncoding(false, true);
                case "utf-16-le": return System.Text.UnicodeEncoding.Unicode;
                case "utf-16-be": return System.Text.UnicodeEncoding.BigEndianUnicode;
                case "utf-32": return new System.Text.UTF32Encoding(false, true);
                case "utf-32-le": return new System.Text.UTF32Encoding(false, true);
                case "utf-32-be": return new System.Text.UTF32Encoding(true, false);
                default:
                    try
                    {
                        return System.Text.CodePagesEncodingProvider.Instance
                            .GetEncoding(NormalizeCodecName(name));
                    }
                    catch (ArgumentException)
                    {
                        return null;
                    }
                    catch (NotSupportedException)
                    {
                        return null;
                    }
            }
        }

        private static byte[] RawBytesOrNull(PyObject value)
        {
            if (value is PyBytes bytesValue)
                return bytesValue.Value;
            if (value is PyByteArray byteArrayValue)
                return byteArrayValue.ToByteArrayCopy();
            if (value is PyMemoryView viewValue)
                return viewValue.Data;
            return null;
        }

        internal static string DecodeBytes(byte[] data, string encodingName, string errorName)
        {
            System.Text.Encoding encoding = ResolveCodec(encodingName);
            if (encoding == null)
            {
                // The registry decides which of CPython's two refusals this is: a codec that exists
                // but is not about text ('base64') is reported as such, a name nothing implements is
                // the unknown encoding. Measured: str(b'ab', 'base64') ->
                // LookupError: 'base64' is not a text encoding; use codecs.decode() ...
                var viaRegistry = PyCodecRegistry.DecodeText(PyBytes.From(data), encodingName, errorName ?? "strict");
                return viaRegistry is PyString text ? text.Value
                    : throw PyTypeError.Create($"'{encodingName}' decoder returned '{viaRegistry.GetTypeName()}' instead of 'str'; use codecs.decode() to decode to arbitrary types");
            }

            string normalized = NormalizeCodecName(encodingName);
            if (normalized == "utf-8")
                return PyBytes.DecodeUtf8Python(data, errorName ?? "strict");
            if (normalized == "ascii")
                return PyBytes.DecodeAsciiPython(data, errorName ?? "strict");

            switch (normalized)
            {
                case "utf-16": return PyBytes.DecodeUtf16Python(data, errorName ?? "strict", 0);
                case "utf-16-le": return PyBytes.DecodeUtf16Python(data, errorName ?? "strict", -1);
                case "utf-16-be": return PyBytes.DecodeUtf16Python(data, errorName ?? "strict", 1);
                case "utf-32": return PyBytes.DecodeUtf32Python(data, errorName ?? "strict", 0);
                case "utf-32-le": return PyBytes.DecodeUtf32Python(data, errorName ?? "strict", -1);
                case "utf-32-be": return PyBytes.DecodeUtf32Python(data, errorName ?? "strict", 1);
            }

            bool strict = errorName == null || errorName == "strict";
            if (!strict && errorName != "ignore" && errorName != "replace" &&
                errorName != "surrogateescape" && errorName != "backslashreplace" && errorName != "surrogatepass")
                throw PyLookupError.Create($"'{errorName}' is not a known error handler");

            // .NET's GetString replaces invalid input silently, so strict mode needs an encoding that
            // throws. UTF-8 has one. For other codecs .NET only offers replacement text, so an invalid
            // byte there yields U+FFFD instead of UnicodeDecodeError; that divergence is recorded in
            // docs/known_gaps.md rather than hidden.
            if (strict && normalized == "utf-8")
            {
                try
                {
                    return new System.Text.UTF8Encoding(false, true).GetString(data);
                }
                catch (DecoderFallbackException exc)
                {
                    int position = exc.Index;
                    byte bad = position >= 0 && position < data.Length ? data[position] : (byte)0;
                    throw PyUnicodeDecodeError.Failure("utf-8", data, position, position + 1, "invalid start byte");
                }
            }

            string decoded = encoding.GetString(data);
            if (errorName == "ignore")
                decoded = decoded.Replace("�", "");
            return decoded;
        }
    }

    public class PyString : PyObject
    {
        static PyString()
        {
            InitializeStringDescriptors();
        }

        /// <summary>
        /// Initialize str type descriptors - called from BuiltinsModule
        /// CPython 호환: _PyType_Ready()와 유사하게 한 번만 초기화
        /// Phase 3: Directly populate TypeDict instead of Descriptors
        /// </summary>
        public static void InitializeStringDescriptors()
        {
            var strType = PyType.StrType;

            // join method descriptor - types.py:48에서 필요
            strType.TypeDict["join"] = new PyMethodDescriptor(
                "join", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"join() takes exactly one argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "join");

                    return str.Join(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            // 기타 주요 메서드들도 등록
            strType.TypeDict["upper"] = new PyMethodDescriptor(
                "upper", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.upper() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "upper");
                    return PyString.Fresh(PyUnicodeCase.Upper(str.Value));
                },
                minArgs: 0, maxArgs: 0
            );

            strType.TypeDict["lower"] = new PyMethodDescriptor(
                "lower", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.lower() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "lower");
                    return PyString.Fresh(PyUnicodeCase.Lower(str.Value));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:10607-10614 - unicode_capitalize_impl
            // Return a capitalized version of the string.
            // More specifically, make the first character have upper case and the rest lower case.
            strType.TypeDict["capitalize"] = new PyMethodDescriptor(
                "capitalize", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.capitalize() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "capitalize");

                    // Empty string remains empty
                    if (str.Value.Length == 0)
                        return str;

                    // CPython 3.12: Objects/unicodeobject.c do_capitalize - the first character goes
                    // through the TITLE mapping (not upper) and the rest through the full lower
                    // mapping, which is why "ǅ".capitalize() keeps ǅ and "ß".capitalize() is "Ss".
                    return PyString.Fresh(PyUnicodeCase.Capitalize(str.Value));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: str.title() - titlecase the string
            strType.TypeDict["title"] = new PyMethodDescriptor(
                "title", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.title() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "title");

                    // CPython str.title() uses Unicode cased/uncased word boundaries, so an apostrophe
                    // keeps the next letter uppercase: "it's a test-case 2nd".title() ->
                    // "It'S A Test-Case 2Nd". System.Globalization.TextInfo.ToTitleCase follows .NET
                    // word-breaking, so the word rule lives in PyUnicodeCase.Title (do_title).
                    return PyString.Fresh(PyUnicodeCase.Title(str.Value));
                },
                minArgs: 0, maxArgs: 0
            );

            strType.TypeDict["split"] = new PyMethodDescriptor(
                "split", strType,
                (self, args, kwargs) => {
                    if (args.Length > 2)
                        throw PyTypeError.Create($"split() takes at most 2 arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "split");

                    var (sep, maxsplit) = SplitArguments(ProjectKeywordSlots(args, kwargs, "sep", "maxsplit"));

                    return str.Split(sep, maxsplit);
                },
                minArgs: 0, maxArgs: 2
, acceptsKwargs: true);

            strType.TypeDict["strip"] = new PyMethodDescriptor(
                "strip", strType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create($"strip() takes at most 1 argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "strip");
                    // do_argstrip: None or nothing is whitespace, else the characters of a str.
                    if (args.Length == 0 || args[0] is PyNone)
                        return str.StripText(BothStrip, null);
                    if (args[0] is not PyString chars)
                        throw PyTypeError.Create("strip arg must be None or str");
                    return str.StripText(BothStrip, chars.Value);
                },
                minArgs: 0, maxArgs: 1
            );

            strType.TypeDict["replace"] = new PyMethodDescriptor(
                "replace", strType,
                (self, args, kwargs) => {
                    if (args.Length < 2 || args.Length > 3)
                        throw PyTypeError.Create($"replace() takes 2 or 3 arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "replace");

                    var old = args[0] is PyString oldStr ? oldStr.Value : throw PyTypeError.Create("replace() old must be str");
                    var newStr = args[1] is PyString newPyStr ? newPyStr.Value : throw PyTypeError.Create("replace() new must be str");

                    // count is "n": PyLong_AsSsize_t after _PyNumber_Index.
                    long count = args.Length == 3 ? PyArgs.Ssize(args[2]) : -1;
                    // replace(): "if (str1 == str2) goto nothing" - the same object for old and new.
                    if (ReferenceEquals(args[0], args[1]))
                        return str.ResultUnchanged();
                    return str.Replace(old, newStr, count);
                },
                minArgs: 2, maxArgs: 3
            );

            // CPython 3.12: str.zfill(width) - pad string with zeros on the left
            strType.TypeDict["zfill"] = new PyMethodDescriptor(
                "zfill", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"zfill() takes exactly one argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "zfill");

                    if (args[0] is not PyInt widthInt)
                        throw PyTypeError.Create("zfill() width must be an integer");

                    int width = PyArgs.Width(widthInt);
                    string value = str.Value;

                    // CPython Objects/unicodeobject.c unicode_zfill pads to the character length.
                    int chars = str.CodePointLength();
                    if (chars >= width)
                        return str.ResultUnchanged();

                    int fillCount = width - chars;

                    // Check if string starts with '+' or '-'
                    if (value.Length > 0 && (value[0] == '+' || value[0] == '-'))
                    {
                        // Move sign to beginning: sign + zeros + rest
                        return PyString.From(value[0] + new string('0', fillCount) + value.Substring(1));
                    }
                    else
                    {
                        // Just prepend zeros
                        return PyString.From(new string('0', fillCount) + value);
                    }
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:11800-11817 - unicode_isdigit_impl
            // Return True if the string is a digit string, False otherwise.
            // A string is a digit string if all characters are digits.
            strType.TypeDict["isdigit"] = new PyMethodDescriptor(
                "isdigit", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.isdigit() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "isdigit");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    // CPython IS_DIGIT is Nd plus a handful of characters carrying the digit
                    // property (superscripts, circled digits); the Nd part is what this covers,
                    // which now includes astral decimal digits. The rest is a table gap in
                    // docs/known_gaps.md.
                    foreach (int cp in str.CodePoints())
                        if (cp >= 0xD800 && cp < 0xE000 || System.Globalization.CharUnicodeInfo.GetDigitValue(char.ConvertFromUtf32(cp), 0) < 0)
                            return PyBool.False;
                    return PyBool.True;
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11736-11753 - unicode_isalpha_impl
            // Return True if the string is an alphabetic string, False otherwise.
            strType.TypeDict["isalpha"] = new PyMethodDescriptor(
                "isalpha", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.isalpha() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "isalpha");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    foreach (int cp in str.CodePoints())
                        if (!IsLetterCp(cp))
                            return PyBool.False;
                    return PyBool.True;
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11755-11772 - unicode_isalnum_impl
            // Return True if the string is an alphanumeric string, False otherwise.
            strType.TypeDict["isalnum"] = new PyMethodDescriptor(
                "isalnum", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.isalnum() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "isalnum");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    foreach (int cp in str.CodePoints())
                        if (!IsLetterOrDigitCp(cp))
                            return PyBool.False;
                    return PyBool.True;
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:12002-12019 - unicode_isspace_impl
            // Return True if the string is a whitespace string, False otherwise.
            strType.TypeDict["isspace"] = new PyMethodDescriptor(
                "isspace", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.isspace() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "isspace");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    foreach (int cp in str.CodePoints())
                        if (!IsWhiteSpaceCp(cp))
                            return PyBool.False;
                    return PyBool.True;
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11933-11950 - unicode_isupper_impl
            // Return True if the string is an uppercase string, False otherwise.
            strType.TypeDict["isupper"] = new PyMethodDescriptor(
                "isupper", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.isupper() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "isupper");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    // unicode_isupper_impl: no lowercase/titlecase character, at least one uppercase.
                    return PyBool.FromBool(PyUnicodeCase.IsUpper(str.Value));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11910-11927 - unicode_islower_impl
            // Return True if the string is a lowercase string, False otherwise.
            strType.TypeDict["islower"] = new PyMethodDescriptor(
                "islower", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.islower() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "islower");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    // unicode_islower_impl: no uppercase/titlecase character, at least one lowercase.
                    return PyBool.FromBool(PyUnicodeCase.IsLower(str.Value));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:9652-9686 - unicode_find
            // Return the lowest index where substring sub is found
            strType.TypeDict["find"] = new PyMethodDescriptor(
                "find", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create(args.Length < 1 ? "find() takes at least 1 argument (0 given)" : $"find() takes from 1 to 3 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "find");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    return FindIndexOf(str, sub.Value, args, backward: false, raiseWhenMissing: false);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:9688-9722 - unicode_index
            // Like find() but raise ValueError when the substring is not found
            strType.TypeDict["index"] = new PyMethodDescriptor(
                "index", strType,
                (self, args, kwargs) => {
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("str.index() takes no keyword arguments");
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create(args.Length < 1 ? "index() takes at least 1 argument (0 given)" : $"index() takes from 1 to 3 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "index");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    return FindIndexOf(str, sub.Value, args, backward: false, raiseWhenMissing: true);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:9724-9758 - unicode_rfind
            // Return the highest index where substring sub is found
            strType.TypeDict["rfind"] = new PyMethodDescriptor(
                "rfind", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create(args.Length < 1 ? "rfind() takes at least 1 argument (0 given)" : $"rfind() takes from 1 to 3 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "rfind");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    return FindIndexOf(str, sub.Value, args, backward: true, raiseWhenMissing: false);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:9760-9794 - unicode_rindex
            // Like rfind() but raise ValueError when substring is not found
            strType.TypeDict["rindex"] = new PyMethodDescriptor(
                "rindex", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create(args.Length < 1 ? "rindex() takes at least 1 argument (0 given)" : $"rindex() takes from 1 to 3 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "rindex");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    return FindIndexOf(str, sub.Value, args, backward: true, raiseWhenMissing: true);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:9877-9911 - unicode_count
            // Return the number of non-overlapping occurrences of substring sub
            // CPython 3.12: str exposes its sequence/number protocols as attributes.
            // These particular names come from generated slot wrappers (typeobject.c
            // slotdefs: nb_add, sq_item, sq_contains), so their argcount and wrong-self
            // wording is the slot style ("expected 1 argument, got N", "descriptor
            // '<name>' requires a 'str' object but received a '<T>'"), unlike the named
            // str methods above. All texts measured against CPython 3.12.15.
            // NOTE: self must be an exact str; str-subclass instances lack string storage
            // in SharpPy today (even len(SS(...)) fails) - tracked separately.
            // CPython 3.12: str.__len__ (sq_length slot; len() also reaches it virtually).
            strType.TypeDict["__len__"] = new PyWrapperDescriptor(
                "__len__", strType,
                (self, args, kwargs) =>
                {
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("str.__len__() takes no keyword arguments");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
                    if (self is not PyString selfStr){
                            selfStr = AsStrReceiver(self);
                            if (selfStr == null){
                                throw PyTypeError.Create($"descriptor '__len__' requires a 'str' object but received a '{self.GetTypeName()}'");
                        }
                    }

                    return PyInt.From(selfStr.Value.Length);
                });

            // CPython 3.12: str.__dict__['__str__'] is a slot wrapper; unicode_tp_str
            // returns self, so subclass instances pass through unchanged (str()/print
            // then re-dispatch type(x).__str__).
            strType.TypeDict["__str__"] = new PyWrapperDescriptor(
                "__str__", strType,
                (self, args, kwargs) =>
                {
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("str.__str__() takes no keyword arguments");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
                    if (self is PyString text)
                        return text.Str();
                    // Measured CPython 3.12.15: str.__str__(CS("w")) == "w" (raw content,
                    // exact str), NOT the user __str__ result; str(CS("w")) dispatches
                    // type(x).__str__ separately.
                    throw PyTypeError.Create($"descriptor '__str__' requires a 'str' object but received a '{self.GetTypeName()}'");
                });
            strType.TypeDict["__repr__"] = new PyWrapperDescriptor(
                "__repr__", strType,
                (self, args, kwargs) =>
                {
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("str.__repr__() takes no keyword arguments");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"expected 0 arguments, got {args.Length}");
                    var reprSelf = AsStrReceiver(self);
                    if (reprSelf == null)
                        throw PyTypeError.Create($"descriptor '__repr__' requires a 'str' object but received a '{self.GetTypeName()}'");
                    return PyString.From("'" + reprSelf.Value + "'");
                });

            // Objects/unicodeobject.c: unicode_getnewargs - pickle and copy reconstruct a str (or a
            // str subclass) from this tuple, so a subclass needs the value, not the wrapper.
            strType.TypeDict["__getnewargs__"] = new PyMethodDescriptor(
                "__getnewargs__", strType,
                (self, args, kwargs) => PyTuple.From(new PyObject[]
                {
                    PyString.From(((PyString)self).Value)
                }),
                minArgs: 0, maxArgs: 0);

            // CPython 3.12: str % (nb_remainder via PyUnicode_Format) slot wrapper.
            strType.TypeDict["__mod__"] = new PyWrapperDescriptor(
                "__mod__", strType,
                (self, args, kwargs) =>
                {
                    var recv = AsStrReceiver(self);
                    if (recv == null)
                        throw PyTypeError.Create($"descriptor '__mod__' requires a 'str' object but received a '{self.GetTypeName()}'");
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("str.__mod__() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    return recv.Modulo(args[0]);
                });

            // CPython 3.12: str tp_richcompare wrappers; non-str other -> NotImplemented,
            // bad self -> slot-wrapper TypeError (both measured).
            string[] strComparisonNames = { "__lt__", "__le__", "__eq__", "__ne__", "__gt__", "__ge__" };
            for (int i = 0; i < strComparisonNames.Length; i++)
            {
                var op = (CompareOp)i;
                string name = strComparisonNames[i];
                strType.TypeDict[name] = new PyWrapperDescriptor(name, strType, (self, args, kwargs) =>
                {
                    var recv = AsStrReceiver(self);
                    if (recv == null)
                        throw PyTypeError.Create($"descriptor '{name}' requires a 'str' object but received a '{self.GetTypeName()}'");
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create($"str.{name}() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    return recv.TpRichCompare(args[0], op);
                });
            }

            strType.TypeDict["__add__"] = new PyWrapperDescriptor(
                "__add__", strType,
                (self, args, kwargs) =>
                {
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("str.__add__() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    if (self is not PyString selfStr)
                    {
                        selfStr = AsStrReceiver(self);
                        if (selfStr == null)
                            throw PyTypeError.Create($"descriptor '__add__' requires a 'str' object but received a '{self.GetTypeName()}'");
                    }

                    var concat = selfStr.Add(args[0]);
                    if (ReferenceEquals(concat, PyNotImplemented.Instance))
                        throw PyTypeError.Create($"can only concatenate str (not \"{args[0].GetTypeName()}\") to str");
                    return concat;
                });

            strType.TypeDict["__getitem__"] = new PyWrapperDescriptor(
                "__getitem__", strType,
                (self, args, kwargs) =>
                {
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("str.__getitem__() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    if (self is not PyString selfStr)
                    {
                        selfStr = AsStrReceiver(self);
                        if (selfStr == null)
                            throw PyTypeError.Create($"descriptor '__getitem__' requires a 'str' object but received a '{self.GetTypeName()}'");
                    }

                    return selfStr.MpSubscript(args[0]);
                });

            strType.TypeDict["__contains__"] = new PyWrapperDescriptor(
                "__contains__", strType,
                (self, args, kwargs) =>
                {
                    if (kwargs != null && kwargs.InternalDict.Count > 0)
                        throw PyTypeError.Create("str.__contains__() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    if (self is not PyString selfStr)
                    {
                        selfStr = AsStrReceiver(self);
                        if (selfStr == null)
                            throw PyTypeError.Create($"descriptor '__contains__' requires a 'str' object but received a '{self.GetTypeName()}'");
                    }

                    // unicode_contains: the slot itself, not PyObject_Contains (a subclass would re-enter it).
                    return selfStr.ContainsItem(args[0]);
                });

            strType.TypeDict["count"] = new PyMethodDescriptor(
                "count", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create(args.Length < 1 ? "count() takes at least 1 argument (0 given)" : $"count() takes from 1 to 3 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "count");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    // CPython Objects/stringlib/count.h: the empty pattern counts the boundary
                    // positions of the window ('abc'.count("", 1, 2) == 2, "".count("") == 1,
                    // 'abc'.count("", 9) == 0), otherwise it is a non-overlapping scan in which a
                    // match has to begin at a character boundary.
                    TextWindow win = str.AdjustedWindow(args);
                    if (sub.Value.Length == 0)
                    {
                        int boundaries = win.End - win.Start + 1;
                        return PyInt.From(boundaries < 0 ? 0 : boundaries);
                    }

                    int count = 0;
                    int pos = win.UnitStart;
                    while (pos + sub.Value.Length <= win.UnitEnd)
                    {
                        int foundIndex = PyFastSearch.IndexOf(str.Value, sub.Value, pos, win.UnitEnd);
                        if (foundIndex == -1)
                            break;
                        if (StartsCharacter(str.Value, sub.Value, foundIndex))
                        {
                            count++;
                            pos = foundIndex + sub.Value.Length;
                        }
                        else
                        {
                            pos = foundIndex + 1;
                        }
                    }
                    return PyInt.From(count);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:10037-10060 - unicode_startswith
            // Return True if string starts with the specified prefix
            strType.TypeDict["startswith"] = new PyMethodDescriptor(
                "startswith", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"startswith() takes from 1 to 3 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "startswith");

                    // CPython unicode_startswith accepts a str or a tuple of str.
                    var prefixes = NormalizePrefixTuple(args[0], "startswith");

                    // CPython Objects/unicodeobject.c unicode_startswith -> tailmatch with
                    // direction 1: PyUnicode_Tailmatch normalises the window on the character
                    // length and fails when start > end.
                    TextWindow win = str.AdjustedWindow(args);
                    foreach (var prefix in prefixes)
                        if (TailMatch(str.Value, prefix, win, fromEnd: false))
                            return PyBool.True;
                    return PyBool.False;
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:10062-10085 - unicode_endswith
            // Return True if string ends with the specified suffix
            strType.TypeDict["endswith"] = new PyMethodDescriptor(
                "endswith", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"endswith() takes from 1 to 3 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "endswith");

                    // CPython unicode_endswith accepts a str or a tuple of str.
                    var suffixes = NormalizePrefixTuple(args[0], "endswith");

                    TextWindow win = str.AdjustedWindow(args);
                    foreach (var suffix in suffixes)
                        if (TailMatch(str.Value, suffix, win, fromEnd: true))
                            return PyBool.True;
                    return PyBool.False;
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:9442-9471 - unicode_lstrip
            // Return a copy with leading whitespace removed
            strType.TypeDict["lstrip"] = new PyMethodDescriptor(
                "lstrip", strType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create($"lstrip() takes at most 1 argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "lstrip");
                    // do_argstrip: None or nothing is whitespace, else the characters of a str.
                    if (args.Length == 0 || args[0] is PyNone)
                        return str.StripText(LeftStrip, null);
                    if (args[0] is not PyString chars)
                        throw PyTypeError.Create("lstrip arg must be None or str");
                    return str.StripText(LeftStrip, chars.Value);
                },
                minArgs: 0, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:9473-9502 - unicode_rstrip
            // Return a copy with trailing whitespace removed
            strType.TypeDict["rstrip"] = new PyMethodDescriptor(
                "rstrip", strType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create($"rstrip() takes at most 1 argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "rstrip");
                    // do_argstrip: None or nothing is whitespace, else the characters of a str.
                    if (args.Length == 0 || args[0] is PyNone)
                        return str.StripText(RightStrip, null);
                    if (args[0] is not PyString chars)
                        throw PyTypeError.Create("rstrip arg must be None or str");
                    return str.StripText(RightStrip, chars.Value);
                },
                minArgs: 0, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:10200-10246 - unicode_rsplit
            // Return a list of the words in the string, using sep as delimiter, from right
            strType.TypeDict["rsplit"] = new PyMethodDescriptor(
                "rsplit", strType,
                (self, args, kwargs) => {
                    if (args.Length > 2)
                        throw PyTypeError.Create($"rsplit() takes at most 2 arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "rsplit");

                    var (sep, maxsplit) = SplitArguments(ProjectKeywordSlots(args, kwargs, "sep", "maxsplit"));

                    return str.Split(sep, maxsplit, fromRight: true);
                },
                minArgs: 0, maxArgs: 2
, acceptsKwargs: true);

            // CPython 3.12: Objects/unicodeobject.c:10248-10289 - unicode_splitlines
            // Return a list of the lines in the string, breaking at line boundaries
            strType.TypeDict["splitlines"] = new PyMethodDescriptor(
                "splitlines", strType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create($"splitlines() takes at most 1 argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "splitlines");

                    // CPython unicode_splitlines is METH_FASTCALL|METH_KEYWORDS with keepends=False,
                    // so 'a\nb'.splitlines(keepends=True) -> ['a\n', 'b']. The delegate read only the
                    // positional array, so the keyword is projected into its slot and resolved with
                    // CPython truthiness instead of matching PyBool/PyInt only.
                    PyObject keependsArg = args.Length >= 1
                        ? args[0]
                        : KwValue(kwargs, "keepends");
                    bool keepends = keependsArg != null && keependsArg.ToBool();

                    // CPython unicode_splitlines: \n \r \r\n \v \f \x1c \x1d \x1e \x85 \u2028 \u2029
                    return new PyList(SplitLinesCore(str.Value, keepends));
                },
                minArgs: 0, maxArgs: 1
, acceptsKwargs: true);

            // CPython 3.12: Objects/unicodeobject.c:10533-10550 - unicode_swapcase
            // Return a copy with uppercase characters converted to lowercase and vice versa
            strType.TypeDict["swapcase"] = new PyMethodDescriptor(
                "swapcase", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.swapcase() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "swapcase");

                    // do_swapcase decides by the uppercase/lowercase PROPERTIES, and the lowercase
                    // branch needs the sigma context, so it is not a per-char ToLower/ToUpper loop.
                    return PyString.Fresh(PyUnicodeCase.SwapCase(str.Value));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11674-11691 - unicode_casefold
            // Return a casefolded copy (aggressive lowercase for caseless matching)
            strType.TypeDict["casefold"] = new PyMethodDescriptor(
                "casefold", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.casefold() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "casefold");

                    return PyString.Fresh(PyUnicodeCase.CaseFold(str.Value));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:10402-10425 - unicode_center
            // Return a centered string of length width
            strType.TypeDict["center"] = new PyMethodDescriptor(
                "center", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create($"center() takes from 1 to 2 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "center");

                    if (args[0] is not PyInt widthInt)
                        throw PyTypeError.Create("center() width must be an integer");

                    int width = PyArgs.Width(widthInt);
                    string fillchar = " ";

                    if (args.Length >= 2)
                    {
                        fillchar = FillCharacter(args[1]);
                    }

                    int chars = str.CodePointLength();
                    if (chars >= width)
                        return str.ResultUnchanged();

                    // CPython 3.12 Objects/stringlib/transmogrify.h:203-206:
                    // left = marg/2 + (marg & width & 1); right = marg - left.
                    int marg = width - chars;
                    int leftPad = marg / 2 + (marg & width & 1);
                    int rightPad = marg - leftPad;

                    return PyString.From(Repeat(fillchar, leftPad) + str.Value + Repeat(fillchar, rightPad));
                },
                minArgs: 1, maxArgs: 2
            );

            // CPython 3.12: Objects/unicodeobject.c:10427-10450 - unicode_ljust
            // Return a left-justified string of length width
            strType.TypeDict["ljust"] = new PyMethodDescriptor(
                "ljust", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create($"ljust() takes from 1 to 2 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "ljust");

                    if (args[0] is not PyInt widthInt)
                        throw PyTypeError.Create("ljust() width must be an integer");

                    int width = PyArgs.Width(widthInt);
                    string fillchar = " ";

                    if (args.Length >= 2)
                    {
                        fillchar = FillCharacter(args[1]);
                    }

                    int chars = str.CodePointLength();
                    if (chars >= width)
                        return str.ResultUnchanged();

                    return PyString.From(str.Value + Repeat(fillchar, width - chars));
                },
                minArgs: 1, maxArgs: 2
            );

            // CPython 3.12: Objects/unicodeobject.c:10452-10475 - unicode_rjust
            // Return a right-justified string of length width
            strType.TypeDict["rjust"] = new PyMethodDescriptor(
                "rjust", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 2)
                        throw PyTypeError.Create($"rjust() takes from 1 to 2 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "rjust");

                    if (args[0] is not PyInt widthInt)
                        throw PyTypeError.Create("rjust() width must be an integer");

                    int width = PyArgs.Width(widthInt);
                    string fillchar = " ";

                    if (args.Length >= 2)
                    {
                        fillchar = FillCharacter(args[1]);
                    }

                    int chars = str.CodePointLength();
                    if (chars >= width)
                        return str.ResultUnchanged();

                    return PyString.From(Repeat(fillchar, width - chars) + str.Value);
                },
                minArgs: 1, maxArgs: 2
            );

            // CPython 3.12: Objects/unicodeobject.c:11693-11710 - unicode_isascii
            // Return True if all characters are ASCII
            strType.TypeDict["isascii"] = new PyMethodDescriptor(
                "isascii", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.isascii() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "isascii");

                    foreach (char c in str.Value)
                    {
                        if (c > 127)
                            return PyBool.False;
                    }
                    return PyBool.True;
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11819-11836 - unicode_isdecimal
            // Return True if all characters are decimal characters
            strType.TypeDict["isdecimal"] = new PyMethodDescriptor(
                "isdecimal", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.isdecimal() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "isdecimal");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    // CPython IS_DECIMAL is every Nd character, not just ASCII:
                    // '\u0663'.isdecimal() and '\U0001d7d9'.isdecimal() are both True.
                    foreach (int cp in str.CodePoints())
                        if (!IsDecimalDigitCp(cp))
                            return PyBool.False;
                    return PyBool.True;
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11838-11855 - unicode_isnumeric
            // Return True if all characters are numeric characters
            strType.TypeDict["isnumeric"] = new PyMethodDescriptor(
                "isnumeric", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.isnumeric() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "isnumeric");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    foreach (int cp in str.CodePoints())
                        if (!IsNumericCp(cp))
                            return PyBool.False;
                    return PyBool.True;
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11857-11874 - unicode_isidentifier
            // Return True if string is a valid Python identifier
            strType.TypeDict["isidentifier"] = new PyMethodDescriptor(
                "isidentifier", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.isidentifier() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "isidentifier");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    return ScanIdentifier(str.Value) ? PyBool.True : PyBool.False;
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11952-11969 - unicode_isprintable
            // Return True if all characters are printable
            strType.TypeDict["isprintable"] = new PyMethodDescriptor(
                "isprintable", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.isprintable() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "isprintable");

                    foreach (int cp in str.CodePoints())
                        if (!IsPrintableCp(cp))
                            return PyBool.False;
                    return PyBool.True;
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:12021-12038 - unicode_istitle
            // Return True if string is titlecased
            strType.TypeDict["istitle"] = new PyMethodDescriptor(
                "istitle", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.istitle() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "istitle");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    return PyBool.FromBool(PyUnicodeCase.IsTitle(str.Value));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:10291-10314 - unicode_partition
            // Partition string at first occurrence of sep, return 3-tuple
            strType.TypeDict["partition"] = new PyMethodDescriptor(
                "partition", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"partition() takes exactly one argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "partition");

                    if (args[0] is not PyString sep)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    if (sep.Value.Length == 0)
                        throw PyValueError.Create("empty separator");

                    int index = str.Value.IndexOf(sep.Value, StringComparison.Ordinal);
                    if (index == -1)
                        return PyTuple.From(new PyObject[] { str, PyString.From(""), PyString.From("") });

                    return PyTuple.From(new PyObject[] {
                        PyString.From(str.Value.Substring(0, index)),
                        sep,
                        PyString.From(str.Value.Substring(index + sep.Value.Length))
                    });
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:10316-10339 - unicode_rpartition
            // Partition string at last occurrence of sep, return 3-tuple
            strType.TypeDict["rpartition"] = new PyMethodDescriptor(
                "rpartition", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"rpartition() takes exactly one argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "rpartition");

                    if (args[0] is not PyString sep)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    if (sep.Value.Length == 0)
                        throw PyValueError.Create("empty separator");

                    int index = str.Value.LastIndexOf(sep.Value, StringComparison.Ordinal);
                    if (index == -1)
                        return PyTuple.From(new PyObject[] { PyString.From(""), PyString.From(""), str });

                    return PyTuple.From(new PyObject[] {
                        PyString.From(str.Value.Substring(0, index)),
                        sep,
                        PyString.From(str.Value.Substring(index + sep.Value.Length))
                    });
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:12096-12117 - unicode_removeprefix
            // Remove prefix from string (Python 3.9+)
            strType.TypeDict["removeprefix"] = new PyMethodDescriptor(
                "removeprefix", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"removeprefix() takes exactly one argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "removeprefix");

                    if (args[0] is not PyString prefix)
                        throw PyTypeError.Create("prefix must be str, not " + args[0].GetTypeName());

                    if (str.Value.StartsWith(prefix.Value, StringComparison.Ordinal))
                        return PyString.From(str.Value.Substring(prefix.Value.Length));

                    return str.ResultUnchanged();
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:12119-12140 - unicode_removesuffix
            // Remove suffix from string (Python 3.9+)
            strType.TypeDict["removesuffix"] = new PyMethodDescriptor(
                "removesuffix", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"removesuffix() takes exactly one argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "removesuffix");

                    if (args[0] is not PyString suffix)
                        throw PyTypeError.Create("suffix must be str, not " + args[0].GetTypeName());

                    if (str.Value.EndsWith(suffix.Value, StringComparison.Ordinal))
                        return PyString.From(str.Value.Substring(0, str.Value.Length - suffix.Value.Length));

                    return str.ResultUnchanged();
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:10477-10500 - unicode_expandtabs
            // Return a copy where tab characters are expanded
            strType.TypeDict["expandtabs"] = new PyMethodDescriptor(
                "expandtabs", strType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create($"expandtabs() takes at most 1 argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "expandtabs");

                    // unicode_expandtabs_impl: tabsize is a C int; columns count code points, a
                    // newline or carriage return starts a new one, and no tab is the text unchanged.
                    int tabsize = 8;
                    PyObject[] paTab = ProjectKeywordSlots(args, kwargs, "tabsize");
                    if (paTab.Length >= 1 && paTab[0] != null)
                        tabsize = PyArgs.Int(paTab[0]);
                    string text = str.Value;
                    if (text.IndexOf('\t') < 0)
                        return str.ResultUnchanged();
                    // First pass: the size of the result ("new string is too long" past Py_ssize_t),
                    // allocated once; nothing is built before it is known to fit.
                    long size = 0;
                    long column = 0;
                    for (int u = 0; u < text.Length; u += PairWidth(text, u))
                    {
                        char c = text[u];
                        if (c == '\t')
                        {
                            if (tabsize > 0)
                            {
                                long incr = tabsize - column % tabsize;
                                if (size > long.MaxValue - incr)
                                    throw PyOverflowError.Create("new string is too long");
                                size += incr;
                                column += incr;
                            }
                        }
                        else
                        {
                            size += PairWidth(text, u);
                            column = c == '\n' || c == '\r' ? 0 : column + 1;
                        }
                    }
                    var result = new System.Text.StringBuilder(PyAllocation.StringLength(size));
                    column = 0;
                    for (int u = 0; u < text.Length; u += PairWidth(text, u))
                    {
                        char c = text[u];
                        if (c == '\t')
                        {
                            if (tabsize > 0)
                            {
                                int spaces = (int)(tabsize - column % tabsize);
                                result.Append(' ', spaces);
                                column += spaces;
                            }
                        }
                        else
                        {
                            result.Append(text, u, PairWidth(text, u));
                            column = c == '\n' || c == '\r' ? 0 : column + 1;
                        }
                    }
                    return PyString.From(result.ToString());
                },
                minArgs: 0, maxArgs: 1
, acceptsKwargs: true);

            // CPython 3.12: Objects/unicodeobject.c:11246-11250 - unicode_encode_impl
            // Encode the string using the specified encoding
            strType.TypeDict["encode"] = new PyMethodDescriptor(
                "encode", strType,
                (self, args, kwargs) => {
                    if (args.Length > 2)
                        throw PyTypeError.Create($"encode() takes at most 2 arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "encode");

                    string encoding = "utf-8";
                    string errors = "strict";

                    PyObject[] paEnc = ProjectKeywordSlots(args, kwargs, "encoding", "errors");
                    if (paEnc.Length >= 1 && paEnc[0] != null)
                    {
                        if (paEnc[0] is PyString encodingStr)
                            encoding = encodingStr.Value;
                        else
                            throw PyTypeError.Create(
                                $"encode() argument 'encoding' must be str, not {paEnc[0].GetTypeName()}");
                    }

                    if (paEnc.Length >= 2 && paEnc[1] != null)
                    {
                        if (paEnc[1] is PyString errorsStr)
                            errors = errorsStr.Value;
                        else
                            throw PyTypeError.Create(
                                $"encode() argument 'errors' must be str, not {paEnc[1].GetTypeName()}");
                    }

                    // One encoder. This descriptor kept its own codec table, so str.encode had two
                    // implementations: Encoding.ASCII turned 'é' into b'?' here, while the faithful
                    // refusal that PyString.Encode already had was reached from nowhere.
                    return str.Encode(encoding, errors);
                },
                minArgs: 0, maxArgs: 2
, acceptsKwargs: true);

            // CPython 3.12: Objects/stringlib/unicode_format.h:940-958 - do_string_format
            // Format string using .format() method with {} placeholders
            strType.TypeDict["format"] = new PyMethodDescriptor(
                "format", strType,
                (self, args, kwargs) => {
                    PyString str = AsStrReceiver(self, "format");
                    var formatted = PyStringFormatter.Format(str.Value, args, kwargs);
                    return SoleLiteral(str, str.Value.IndexOf('{') < 0 && str.Value.IndexOf('}') < 0) ?? formatted;
                }
, acceptsKwargs: true);

            // CPython 3.12: Objects/stringlib/unicode_format.h:961-964 - do_string_format_map
            // Format string using .format_map() method with mapping object
            strType.TypeDict["format_map"] = new PyMethodDescriptor(
                "format_map", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"format_map() takes exactly one argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "format_map");
                    var formatted = PyStringFormatter.Format(str.Value, null, args[0]);
                    return SoleLiteral(str, str.Value.IndexOf('{') < 0 && str.Value.IndexOf('}') < 0) ?? formatted;
                },
                minArgs: 1, maxArgs: 1
, acceptsKwargs: true);

            // CPython 3.12: Objects/unicodeobject.c:12889-12988 - unicode_maketrans_impl
            // Create translation table for translate()
            var maketransMethod = new PyMethodDescriptor(
                "maketrans", strType,
                (self, args, kwargs) => {
                    // self is the class (PyType) when called as classmethod
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"maketrans() takes 1 to 3 positional arguments but {args.Length} were given");

                    PyDict table = new PyDict();

                    if (args.Length == 1)
                    {
                        // Single dict argument
                        if (args[0] is not PyDict dict)
                            throw PyTypeError.Create("if you give only one argument to maketrans it must be a dict");

                        // Copy entries, converting string keys to int keys
                        var items = dict.Items();
                        foreach (PyObject item in items.Items)
                        {
                            if (item is not PyTuple pair || pair.Items.Length != 2)
                                continue;

                            var key = pair.Items[0];
                            var value = pair.Items[1];

                            if (key is PyString keyStr)
                            {
                                if (keyStr.Value.Length != 1)
                                    throw PyValueError.Create("string keys in translate table must be of length 1");

                                int codepoint = keyStr.Value[0];
                                table.SetItem(PyInt.From(codepoint), value);
                            }
                            else if (key is PyInt)
                            {
                                table.SetItem(key, value);
                            }
                            else
                            {
                                throw PyTypeError.Create("keys in translate table must be strings or integers");
                            }
                        }
                    }
                    else
                    {
                        // Two or three arguments: x, y, [z]
                        if (args[0] is not PyString x || args[1] is not PyString y)
                            throw PyTypeError.Create("first maketrans argument must be a string if there is a second argument");

                        if (x.Value.Length != y.Value.Length)
                            throw PyValueError.Create("the first two maketrans arguments must have equal length");

                        // Create mapping from x to y
                        for (int i = 0; i < x.Value.Length; i++)
                        {
                            int fromChar = x.Value[i];
                            int toChar = y.Value[i];
                            table.SetItem(PyInt.From(fromChar), PyInt.From(toChar));
                        }

                        // If z is provided, map those characters to None (delete)
                        if (args.Length >= 3)
                        {
                            if (args[2] is not PyString z)
                                throw PyTypeError.Create("third maketrans argument must be a string");

                            foreach (char c in z.Value)
                            {
                                table.SetItem(PyInt.From(c), PyNone.Instance);
                            }
                        }
                    }

                    return table;
                },
                minArgs: 1, maxArgs: 3
            );
            // unicode_maketrans: METH_VARARGS|METH_STATIC.
            strType.TypeDict["maketrans"] = PyStaticmethod.ForBuiltin(strType, "maketrans", (args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create("str.maketrans() takes no keyword arguments");
                return maketransMethod._implementation(strType, args, kwargs);
            });

            // CPython 3.12: Objects/unicodeobject.c:13010-13014 - unicode_translate
            // Translate string using translation table
            strType.TypeDict["translate"] = new PyMethodDescriptor(
                "translate", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"translate() takes exactly one argument ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "translate");

                    if (args[0] is not PyDict table)
                        throw PyTypeError.Create("translate() argument must be a dict");

                    var result = new System.Text.StringBuilder(str.Value.Length);

                    foreach (char c in str.Value)
                    {
                        var key = PyInt.From(c);
                        if (table.Contains(key).Value)
                        {
                            var value = table.GetItem(key);
                            if (value == PyNone.Instance)
                            {
                                // Delete character
                                continue;
                            }
                            else if (value is PyInt intValue)
                            {
                                result.Append((char)intValue.Value);
                            }
                            else if (value is PyString strValue)
                            {
                                result.Append(strValue.Value);
                            }
                            else if (value is PyNone)
                            {
                                // Delete character
                                continue;
                            }
                            else
                            {
                                throw PyTypeError.Create($"character mapping must return integer, None or str, not {value.GetTypeName()}");
                            }
                        }
                        else
                        {
                            // Character not in table, keep as is
                            result.Append(c);
                        }
                    }

                    return PyString.From(result.ToString());
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:14632-14670 - unicode__format___impl
            // str.__format__(format_spec) - Format the string according to format_spec
            strType.TypeDict["__format__"] = new PyMethodDescriptor(
                "__format__", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"__format__() takes 1 positional argument ({args.Length} given)");
                    if (self is not PyString strObj){
                            strObj = AsStrReceiver(self);
                            if (strObj == null){
                                throw PyTypeError.Create($"descriptor '__format__' requires a 'str' object but received a '{self.GetTypeName()}'");
                        }
                    }
                    if (args[0] is not PyString specStr)
                        throw PyTypeError.Create($"__format__() argument 1 must be str, not {args[0].GetTypeName()}");

                    // unicode__format__: an empty spec is str(self).
                    return specStr.Value.Length == 0 ? self.ToStr() : PyString.From(FormatString(strObj.Value, specStr.Value));
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:14711-14730 (unicode_new_impl)
            // str.__new__(cls, value='', encoding=None, errors=None)
            strType.TypeDict["__new__"] = PyStaticBuiltinMethod.ForNew(
                strType,
                (args, kwargs) =>
                {
                    // args[0] is the type, which tp_new_wrapper has checked.
                    var cls = (PyType)args[0];

                    // CPython unicode_new takes (object='', encoding='utf-8', errors='strict'), and
                    // the encoding/errors pair changes the meaning of the call completely: bytes get
                    // decoded, a str is refused ("decoding str is not supported"), and a non
                    // bytes-like object is refused ("decoding to str: need a bytes-like object, int
                    // found"). None of that was here - the arguments were dropped and everything went
                    // through str(), so H(b"ab", "utf8") produced "b'ab'" instead of 'ab'.
                    PyStringNewArguments.Project(args, kwargs, true,
                        out PyObject objectArg, out PyObject encodingArg, out PyObject errorsArg);
                    PyString text = PyStringNewArguments.Build(objectArg, encodingArg, errorsArg);
                    string value = text.Value;

                    // CPython: Line 14726-14728
                    // if (unicode != NULL && type != &PyUnicode_Type) {
                    //     Py_SETREF(unicode, unicode_subtype_new(type, unicode));
                    // }
                    if (cls != PyType.StrType)
                    {
                        // This is a str subclass (like StrEnum)
                        // CPython: unicode_subtype_new (Objects/unicodeobject.c:14733-14826)
                        // Creates PyUnicodeObject with subtype's ob_type
                        if (args[0] is PyClass classObj)
                        {
                            // CPython: line 14744 - self = type->tp_alloc(type, 0);
                            // line 14751-14764 - copy unicode data from original
                            return new PyStrSubclass(classObj, value);
                        }
                        else
                        {
                            // cls is a PyType but not PyClass, shouldn't happen normally
                            throw PyTypeError.Create($"str.__new__: expected class, got {cls.GetTypeName()}");
                        }
                    }
                    else
                    {
                        // unicode_new_impl: for str itself the result is the object as it came.
                        return text;
                    }
                }
            );
        }

        #region Core Properties

        public string Value { get; }

        public PyString(string value) => Value = value ?? "";

        public override PyType GetPyType() => _customType ?? PyType.StrType;
        public override string GetTypeName() => _customType?.Name ?? "str";

        // CPython 3.12: Objects/unicodeobject.c - unicode_bool
        // Optimized: Direct length check, no MRO traversal
        protected internal override bool Bool() => Value.Length > 0;

        #endregion

        #region String Representation

        // Python str() - PyString은 이미 문자열이므로 자신을 반환
        protected internal override PyString Str() => GetType() == typeof(PyString) ? this : PyString.From(Value);

        protected internal override PyString Repr()
        {
            // CPython-compatible repr() implementation
            bool hasSingleQuote = Value.Contains('\'');
            bool hasDoubleQuote = Value.Contains('"');

            char quoteChar;
            if (hasSingleQuote && !hasDoubleQuote)
            {
                // Contains single quotes but no double quotes -> use double quotes
                quoteChar = '"';
            }
            else
            {
                // Default to single quotes (covers: no quotes, only double quotes, or both)
                quoteChar = '\'';
            }

            // Efficient single-pass escape processing with StringBuilder
            var result = new System.Text.StringBuilder(Value.Length + 10); // Pre-allocate with some extra space
            result.Append(quoteChar);

            for (int index = 0; index < Value.Length; index++)
            {
                char c = Value[index];
                if (char.IsHighSurrogate(c) && char.IsSurrogatePair(Value, index))
                {
                    int code = char.ConvertToUtf32(Value, index);
                    var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(Value, index);
                    if (IsPrintableCategory(category)) result.Append(Value, index, 2);
                    else result.Append($"\\U{code:x8}");
                    index++;
                    continue;
                }
                // Handle special characters based on CPython patterns
                if (c == '\\')
                {
                    result.Append("\\\\");
                }
                else if (c == '\n')
                {
                    result.Append("\\n");
                }
                else if (c == '\t')
                {
                    result.Append("\\t");
                }
                else if (c == '\r')
                {
                    result.Append("\\r");
                }
                else if (c == '\'' && quoteChar == '\'')
                {
                    result.Append("\\'");  // Escape single quote only when using single quotes
                }
                else if (c == '"' && quoteChar == '"')
                {
                    result.Append("\\\""); // Escape double quote only when using double quotes
                }
                else if (!IsPrintableForRepr(c))
                {
                    // Non-printable characters → hex format (following CPython's isprintable() logic)
                    result.Append(c <= 0xff ? $"\\x{(int)c:x2}" : $"\\u{(int)c:x4}");
                }
                else
                {
                    // Printable ASCII characters (32-126) → as-is
                    result.Append(c);
                }
            }

            result.Append(quoteChar);
            return PyString.From(result.ToString());
        }

        /// <summary>
        /// Determines if a character is printable for repr() output, following CPython's isprintable() logic
        /// </summary>
        private static bool IsPrintableForRepr(char c)
        {
            // CPython's isprintable() logic is quite specific
            // We need to match it exactly for compatibility

            if (c < 128) return c >= ' ' && c != 127;
            return IsPrintableCategory(char.GetUnicodeCategory(c));
        }

        private static bool IsPrintableCategory(System.Globalization.UnicodeCategory category) => category is not
            (System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format or
             System.Globalization.UnicodeCategory.Surrogate or System.Globalization.UnicodeCategory.PrivateUse or
             System.Globalization.UnicodeCategory.OtherNotAssigned or System.Globalization.UnicodeCategory.SpaceSeparator or
             System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator);

        #endregion

        #region Hash and Equality

        // unicode_hash
        protected internal override long Hash() => PyHash.Text(Value);

        public override int ToHash() => Value.GetHashCode();

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(Value == otherStr.Value),
                // CPython unicode_richcompare: content comparison also for str subclasses.
                _ => PyNotImplemented.Instance
            };
        }

        // unicode_richcompare answers Py_NE itself, on the contents; a subclass's __eq__ is not asked.
        protected override PyObject PyNotEquals(PyObject other) =>
            PyEquals(other) is PyBool equal ? PyBool.FromBool(!equal.IsTrue()) : PyNotImplemented.Instance;

        #endregion

        #region Comparison Operations


        #region Code-Point Coordinates

        // CPython never translates a coordinate. PyUnicode stores 1, 2 or 4 byte characters
        // (Include/cpython/unicodeobject.h, PyUnicode_KIND/PyUnicode_READ), so an index, a slice
        // bound, a find() result, a pad width and a precision are all code point indices by
        // construction. SharpPy stores a .NET string, which is UTF-16 code units: an astral
        // character occupies two units, so every Python visible coordinate has to be translated,
        // and the translation lives here instead of at the ~150 places that read Value.
        //
        // A pair is a high surrogate followed by a low surrogate. That is also the one place where
        // this representation cannot be exact: CPython can hold U+D800 and U+DC00 as two
        // independent code points, while SharpPy reads the same .NET string as U+10000. Recorded in
        // docs/known_gaps.md; astral text, which is what programs contain, now behaves.

        /// <summary>True when unit is the low half of a pair, i.e. not a character boundary.</summary>
        internal static bool IsContinuation(string text, int unit) =>
            unit > 0 && char.IsLowSurrogate(text[unit]) && char.IsHighSurrogate(text[unit - 1]);

        /// <summary>Units occupied by the character that starts at unit.</summary>
        internal static int PairWidth(string text, int unit) =>
            unit + 1 < text.Length && char.IsHighSurrogate(text[unit]) && char.IsLowSurrogate(text[unit + 1])
                ? 2
                : 1;

        internal static int CodePointAtUnit(string text, int unit) =>
            PairWidth(text, unit) == 2 ? char.ConvertToUtf32(text[unit], text[unit + 1]) : text[unit];

        internal static bool HasSurrogatePairs(string text)
        {
            for (int i = 0; i + 1 < text.Length; i++)
                if (char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1]))
                    return true;
            return false;
        }

        // Decided on first use and kept, because ordinary and BMP text is the overwhelming
        // majority: for it a code point index *is* a unit index and every conversion below is a
        // comparison. Only text containing a pair pays a scan.
        private sbyte _pairState;

        private bool HasPairs
        {
            get
            {
                if (_pairState == 0)
                    _pairState = (sbyte)(HasSurrogatePairs(Value) ? 2 : 1);
                return _pairState == 2;
            }
        }

        /// <summary>The length Python reports: one per character, not one per code unit.</summary>
        internal static int CodePointCount(string text)
        {
            if (!HasSurrogatePairs(text))
                return text.Length;
            int n = 0;
            for (int i = 0; i < text.Length; i++)
                if (!IsContinuation(text, i))
                    n++;
            return n;
        }

        /// <summary>Unit offset at which character number cpIndex begins.</summary>
        internal static int CodePointToUnit(string text, int cpIndex)
        {
            if (!HasSurrogatePairs(text))
                return cpIndex < 0 ? 0 : (cpIndex > text.Length ? text.Length : cpIndex);
            int unit = 0;
            for (int seen = 0; seen < cpIndex; seen++)
            {
                if (unit >= text.Length)
                    return text.Length;
                unit += PairWidth(text, unit);
            }
            return unit > text.Length ? text.Length : unit;
        }

        /// <summary>Character number of the character occupying unit offset.</summary>
        internal static int UnitToCodePoint(string text, int unit)
        {
            if (!HasSurrogatePairs(text))
                return unit;
            int n = 0;
            int stop = unit < text.Length ? unit : text.Length;
            for (int i = 0; i < stop; i++)
                if (!IsContinuation(text, i))
                    n++;
            return n;
        }

        /// <summary>A code point coordinate range of text.</summary>
        internal static string SubstringCodePoints(string text, int cpStart, int cpStop)
        {
            int from = CodePointToUnit(text, cpStart);
            int to = CodePointToUnit(text, cpStop);
            return to <= from ? string.Empty : text.Substring(from, to - from);
        }

        /// <summary>
        /// text[start:stop:step] with every coordinate in code points. CPython walks characters in
        /// Objects/unicodeobject.c:11368 unicode_getitem / unicode_slice using PyUnicode_READ; the
        /// step here advances by characters too, and stays O(n) by moving one character at a time
        /// instead of re-converting from the front.
        /// </summary>
        private string StepCodePoints(int start, int stop, int step)
        {
            int count = PySlice.GetLength(start, stop, step);
            if (count <= 0)
                return string.Empty;

            var sb = new StringBuilder(count);
            int unit = CodePointToUnit(Value, start);
            int cp = start;
            for (int n = 0; n < count; n++)
            {
                if (unit >= Value.Length)
                    break;
                int width = PairWidth(Value, unit);
                sb.Append(Value, unit, width);
                // The last character needs no walk to the place after it.
                if (n == count - 1)
                    break;

                int next = cp + step;
                if (next == cp)
                    break;
                if (next > cp)
                {
                    for (int k = cp; k < next && unit < Value.Length; k++)
                        unit += PairWidth(Value, unit);
                }
                else
                {
                    for (int k = cp; k > next; k--)
                    {
                        unit--;
                        if (IsContinuation(Value, unit))
                            unit--;
                    }
                }
                cp = next;
            }
            return sb.ToString();
        }

        /// <summary>
        /// The (start, end) pair a method's optional arguments describe, in both coordinate
        /// systems. CPython normalises once with ADJUST_INDICES (Objects/unicodeobject.c:9091) on a
        /// length that is already in code points: end is clamped to the length and to zero, a
        /// negative start is shifted, and start is deliberately *not* clamped down to end. That last
        /// detail is observable: 'abc'.find('', 4) is -1 while 'abc'.find('', 3) is 3.
        /// </summary>
        internal readonly struct TextWindow
        {
            internal readonly int Chars;
            internal readonly int Start;
            internal readonly int End;
            internal readonly int UnitStart;
            internal readonly int UnitEnd;

            internal TextWindow(int chars, int start, int end, string text)
            {
                Chars = chars;
                Start = start;
                End = end;
                UnitStart = CodePointToUnit(text, start);
                UnitEnd = CodePointToUnit(text, end);
            }

            internal bool IsEmpty => End <= Start;
        }

        /// <summary>
        /// tailmatch (Objects/unicodeobject.c): after ADJUST_INDICES, end -= len(sub); end &lt; start
        /// fails, an empty sub then matches, else the code points at start (or ending at end) are
        /// compared. The match has to begin and end on a character boundary of the text.
        /// </summary>
        private static bool TailMatch(string text, string sub, TextWindow win, bool fromEnd)
        {
            if (win.End - CodePointCount(sub) < win.Start)
                return false;
            if (sub.Length == 0)
                return true;
            int at = fromEnd ? win.UnitEnd - sub.Length : win.UnitStart;
            if (at < win.UnitStart || at + sub.Length > win.UnitEnd)
                return false;
            return string.CompareOrdinal(text, at, sub, 0, sub.Length) == 0
                && StartsCharacter(text, sub, at)
                && (at + sub.Length == text.Length || !IsContinuation(text, at + sub.Length));
        }

        internal TextWindow AdjustedWindow(PyObject[] args)
        {
            // ADJUST_INDICES on PyUnicode_GET_LENGTH, not a subclass's __len__.
            int chars = CodePointLength();
            ParseStrStartStop(args, chars, out int start, out int end);
            if (end > chars)
                end = chars;
            else if (end < 0)
            {
                end += chars;
                if (end < 0)
                    end = 0;
            }
            if (start < 0)
            {
                start += chars;
                if (start < 0)
                    start = 0;
            }
            return new TextWindow(chars, start, end, Value);
        }

        /// <summary>
        /// One implementation behind find, index, rfind and rindex. CPython has four entry points
        /// (Objects/unicodeobject.c:11348 unicode_find and friends) that all funnel into
        /// any_find_slice (Objects/unicodeobject.c:9106); SharpPy had four copies of the same index
        /// arithmetic. Coordinates in and out are code points, the search runs on .NET's unit index
        /// and is converted at both edges.
        /// </summary>
        private static PyObject FindIndexOf(PyString str, string pattern, PyObject[] args,
                                            bool backward, bool raiseWhenMissing)
        {
            TextWindow win = str.AdjustedWindow(args);
            string text = str.Value;

            // any_find_slice: if (end - start < len2) return -1;
            if (win.End - win.Start < CodePointCount(pattern))
                return NotFound(raiseWhenMissing);

            if (pattern.Length == 0)
            {
                // The empty pattern matches at the first position of the window going forward and
                // at the last one going backward ('abc'.find('', 1) -> 1, 'abc'.rfind('', 0, 2) -> 2).
                return PyInt.From(backward ? UnitToCodePoint(text, win.UnitEnd)
                                          : UnitToCodePoint(text, win.UnitStart));
            }

            int found;
            if (!backward)
            {
                found = PyFastSearch.IndexOf(text, pattern, win.UnitStart, win.UnitEnd);
                while (found != -1 && !StartsCharacter(text, pattern, found))
                {
                    int from = found + 1;
                    found = from < win.UnitEnd ? PyFastSearch.IndexOf(text, pattern, from, win.UnitEnd) : -1;
                }
            }
            else
            {
                found = win.UnitEnd == 0
                    ? -1
                    : text.LastIndexOf(pattern, win.UnitEnd - 1, win.UnitEnd - win.UnitStart, StringComparison.Ordinal);
                while (found != -1 && !StartsCharacter(text, pattern, found))
                {
                    found = found > win.UnitStart + 1
                        ? text.LastIndexOf(pattern, found - 1, found - 1 - win.UnitStart, StringComparison.Ordinal)
                        : -1;
                }
            }

            return found == -1 ? NotFound(raiseWhenMissing) : PyInt.From(UnitToCodePoint(text, found));
        }

        /// <summary>
        /// A UTF-16 search can match inside a surrogate pair, which is not a match for CPython
        /// because it compares code points (Objects/stringlib/codecs.h). Reject a candidate that
        /// begins in the middle of a pair, or whose pair width disagrees with the pattern's first
        /// character: S.find(chr(0xDC00)) is -1 for exactly this reason.
        /// </summary>
        private static bool StartsCharacter(string text, string pattern, int found) =>
            !IsContinuation(text, found) && PairWidth(text, found) == PairWidth(pattern, 0);

        private static PyObject NotFound(bool raiseWhenMissing)
        {
            if (raiseWhenMissing)
                throw PyValueError.Create("substring not found");
            return PyInt.From(-1);
        }

        /// <summary>
        /// The fill of ljust/rjust/center is one character, which may be astral, so it is stored as
        /// a string and repeated as a unit. CPython's wording is not method specific.
        /// </summary>
        // CPython distinguishes the two refusals (Objects/unicodeobject.c:get_string_or_char style):
        // a non-str argument is "must be a unicode character, not int", a str of the wrong length is
        // "must be exactly one character long".
        private static string FillCharacter(PyObject fillArg)
        {
            if (fillArg is not PyString fill)
                throw PyTypeError.Create(
                    $"The fill character must be a unicode character, not {fillArg.GetTypeName()}");
            if (CodePointCount(fill.Value) != 1)
                throw PyTypeError.Create("The fill character must be exactly one character long");
            return fill.Value;
        }

        private static string Repeat(string unit, int count)
        {
            if (count <= 0 || unit.Length == 0)
                return string.Empty;
            if (unit.Length == 1)
                return new string(unit[0], count);
            var sb = new StringBuilder(unit.Length * count);
            for (int i = 0; i < count; i++)
                sb.Append(unit);
            return sb.ToString();
        }

        // The character class tests have to read characters too. CPython asks its generated
        // tables for one character at a time (Objects/unicodetype_db.h, the IS_ALPHA / IS_DIGIT /
        // IS_SPACE / IS_PRINTABLE macros used by unicode_isalpha_impl and friends); iterating .NET
        // units asks that question about half of an astral character, which is why
        // 'a\U00010000'.isalpha() answered False.
        private static System.Globalization.UnicodeCategory CategoryOf(int cp) =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(cp);

        internal static bool IsLetterCp(int cp)
        {
            switch (CategoryOf(cp))
            {
                case System.Globalization.UnicodeCategory.UppercaseLetter:
                case System.Globalization.UnicodeCategory.LowercaseLetter:
                case System.Globalization.UnicodeCategory.TitlecaseLetter:
                case System.Globalization.UnicodeCategory.ModifierLetter:
                case System.Globalization.UnicodeCategory.OtherLetter:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsDecimalDigitCp(int cp) =>
            CategoryOf(cp) == System.Globalization.UnicodeCategory.DecimalDigitNumber;

        internal static bool IsNumericCp(int cp)
        {
            switch (CategoryOf(cp))
            {
                case System.Globalization.UnicodeCategory.DecimalDigitNumber:
                case System.Globalization.UnicodeCategory.LetterNumber:
                case System.Globalization.UnicodeCategory.OtherNumber:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsLetterOrDigitCp(int cp) => IsLetterCp(cp) || IsDecimalDigitCp(cp);

        /// <summary>A code point at an index; a lone surrogate stands for itself.</summary>
        internal static int CodePointAt(string s, int index, out int width)
        {
            if (char.IsHighSurrogate(s[index]) && index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]))
            {
                width = 2;
                return char.ConvertToUtf32(s[index], s[index + 1]);
            }
            width = 1;
            return s[index];
        }

        internal static string CodePointText(int cp) => cp >= 0x10000 ? char.ConvertFromUtf32(cp) : ((char)cp).ToString();

        // XID_Start and XID_Continue of Objects/unicodetype_db.h, from the general category plus
        // Other_ID_Start, Other_ID_Continue and what NFKC takes out of them.
        private static bool IsXidExcluded(int cp) =>
            cp is 0x037A or 0x0E33 or 0x0EB3 or 0x309B or 0x309C or 0xFDFA or 0xFDFB or 0xFE70 or 0xFE72 or 0xFE74 or 0xFE76
                or 0xFE78 or 0xFE7A or 0xFE7C or 0xFE7E or 0xFF9E or 0xFF9F || (cp >= 0xFC5E && cp <= 0xFC63);

        internal static bool IsXidStart(int cp)
        {
            if (cp < 0x80)
                return (cp >= 'a' && cp <= 'z') || (cp >= 'A' && cp <= 'Z');
            if (cp is 0x1885 or 0x1886 or 0x2118 or 0x212E)
                return true;
            if (IsXidExcluded(cp))
                return false;
            switch (CategoryOf(cp))
            {
                case System.Globalization.UnicodeCategory.UppercaseLetter:
                case System.Globalization.UnicodeCategory.LowercaseLetter:
                case System.Globalization.UnicodeCategory.TitlecaseLetter:
                case System.Globalization.UnicodeCategory.ModifierLetter:
                case System.Globalization.UnicodeCategory.OtherLetter:
                case System.Globalization.UnicodeCategory.LetterNumber:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsXidContinue(int cp)
        {
            if (cp < 0x80)
                return (cp >= 'a' && cp <= 'z') || (cp >= 'A' && cp <= 'Z') || (cp >= '0' && cp <= '9') || cp == '_';
            if (IsXidStart(cp) || cp is 0x00B7 or 0x0387 or 0x19DA or 0x0E33 or 0x0EB3 or 0xFF9E or 0xFF9F || (cp >= 0x1369 && cp <= 0x1371))
                return true;
            switch (CategoryOf(cp))
            {
                case System.Globalization.UnicodeCategory.NonSpacingMark:
                case System.Globalization.UnicodeCategory.SpacingCombiningMark:
                case System.Globalization.UnicodeCategory.DecimalDigitNumber:
                case System.Globalization.UnicodeCategory.ConnectorPunctuation:
                    return true;
                default:
                    return false;
            }
        }

        // _PyUnicode_ScanIdentifier, whether the whole string is scanned
        internal static bool ScanIdentifier(string s)
        {
            if (s.Length == 0)
                return false;
            int index = 0;
            bool first = true;
            while (index < s.Length)
            {
                int cp = CodePointAt(s, index, out int width);
                if (first ? !(IsXidStart(cp) || cp == '_') : !IsXidContinue(cp))
                    return false;
                first = false;
                index += width;
            }
            return true;
        }

        // CPython's whitespace set is exactly these code points (the IS_SPACE line of
        // Objects/unicodetype_db.h): Zs plus U+0085 and the ASCII control whitespace.
        internal static bool IsWhiteSpaceCp(int cp) =>
            (cp >= 0x09 && cp <= 0x0D) || (cp >= 0x1C && cp <= 0x1F) || cp == 0x20 ||
            cp == 0x85 || cp == 0xA0 || cp == 0x1680 || (cp >= 0x2000 && cp <= 0x200A) ||
            cp == 0x2028 || cp == 0x2029 || cp == 0x3000;

        /// <summary>
        /// CPython Objects/unicodeobject.c unicode_isprintable_impl rejects Cc, Cf, Cs, Co, Cn, Zl,
        /// Zp and Zs, and allows nothing else but U+0020 out of the separators. \n, \r and \t are
        /// control characters, so they are not printable; a hand-written rule that carved them out
        /// said True where CPython says False.
        /// </summary>
        internal static bool IsPrintableCp(int cp)
        {
            if (cp == 0x20)
                return true;
            switch (CategoryOf(cp))
            {
                case System.Globalization.UnicodeCategory.Control:
                case System.Globalization.UnicodeCategory.Format:
                case System.Globalization.UnicodeCategory.Surrogate:
                case System.Globalization.UnicodeCategory.PrivateUse:
                case System.Globalization.UnicodeCategory.LineSeparator:
                case System.Globalization.UnicodeCategory.ParagraphSeparator:
                case System.Globalization.UnicodeCategory.SpaceSeparator:
                    // CPython also rejects Cn (unassigned). .NET exposes no unassigned category, so
                    // that half of the rule is a table gap recorded in docs/known_gaps.md.
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>The characters of this value, one per Python character.</summary>
        internal System.Collections.Generic.IEnumerable<int> CodePoints()
        {
            for (int i = 0; i < Value.Length; i += PairWidth(Value, i))
                yield return CodePointAtUnit(Value, i);
        }

        /// <summary>
        /// The characters of text as one-character str objects. Four places needed this and each had
        /// its own unit loop (PyString.AsList behind list(), PySet's string conversion, the starred
        /// unpacking fast path in the VM, and the exception-group member list). Iterating a str
        /// yields characters in CPython (Objects/unicodeobject.c unicode_iterator), so list(),
        /// set(), (*s) and ExceptionGroup('m', s) all disagreed for astral text.
        /// </summary>
        internal static PyObject[] Characters(string text)
        {
            var items = new PyObject[CodePointCount(text)];
            int n = 0;
            for (int i = 0; i < text.Length; i += PairWidth(text, i))
                items[n++] = PyString.From(text.Substring(i, PairWidth(text, i)));
            return items;
        }

        #endregion

        /// <summary>
        /// The single place Python text ordering is decided. Everything that compares str values
        /// (str, str subclasses, and anything that sorts them) must go through here.
        ///
        /// CPython compares code points (Objects/unicodeobject.c unicode_compare_startend reads one
        /// character at a time with PyUnicode_READ). UTF-16 code unit order is a different order: a
        /// surrogate pair starts with U+D800..U+DBFF, so unit order sorts astral characters between
        /// U+D7FF and U+E000 instead of above U+FFFF. That is why sorted(['\U00010000',
        /// '\ue000', '\uffff', 'A']) used to put the astral one second. string.CompareOrdinal
        /// stays as the fast path, and it is exact whenever no pair is present.
        /// </summary>
        internal static int CompareText(string left, string right)
        {
            if (!HasSurrogatePairs(left) && !HasSurrogatePairs(right))
                return string.CompareOrdinal(left, right);

            int li = 0;
            int ri = 0;
            while (li < left.Length && ri < right.Length)
            {
                int lc = CodePointAtUnit(left, li);
                int rc = CodePointAtUnit(right, ri);
                if (lc != rc)
                    return lc < rc ? -1 : 1;
                li += PairWidth(left, li);
                ri += PairWidth(right, ri);
            }
            // Same characters so far: the shorter one is smaller, as in CPython.
            int lrest = CodePointCountFrom(left, li);
            int rrest = CodePointCountFrom(right, ri);
            if (lrest == rrest)
                return 0;
            return lrest < rrest ? -1 : 1;
        }

        private static int CodePointCountFrom(string text, int unit)
        {
            int n = 0;
            for (int i = unit; i < text.Length; i++)
                if (!IsContinuation(text, i))
                    n++;
            return n;
        }

        /// <summary>
        /// The value of the character at a Python index, for ord() and anything else that wants a
        /// code point. CodePointToUnit keeps this correct for astral text.
        /// </summary>
        internal int CodePointAt(int index) => CodePointAtUnit(Value, CodePointToUnit(Value, index));

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(CompareText(Value, otherStr.Value) < 0),
                _ => PyNotImplemented.Instance
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(CompareText(Value, otherStr.Value) <= 0),
                _ => PyNotImplemented.Instance
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(CompareText(Value, otherStr.Value) > 0),
                _ => PyNotImplemented.Instance
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(CompareText(Value, otherStr.Value) >= 0),
                _ => PyNotImplemented.Instance
            };
        }

        #endregion

        #region String Operations

        /// <summary>
        /// CPython 3.12: str.__add__ - String concatenation
        /// CPython: Objects/unicodeobject.c:unicode_concatenate (lines ~11800-11850)
        /// </summary>
        public override PyObject Add(PyObject other)
        {
            if (other is not PyString otherStr)
            {
                // CPython 3.12: PyUnicode_Concat gates on PyUnicode_Check, which is true
                // for str subclass instances; they concatenate by content
                // (Objects/unicodeobject.c:11060-11067).
                return PyNotImplemented.Instance;
            }

            // PyUnicode_Concat's shortcuts: the empty str and the other operand is PyUnicode_FromObject
            // of the other (itself when exact, an exact copy of a subclass).
            if (IsEmptySingleton) return otherStr.ResultUnchanged();
            if (otherStr.IsEmptySingleton) return ResultUnchanged();
            return PyString.From(Value + otherStr.Value);
        }

        // --- Shared primitives for CPython-exact string operations ---

        // CPython unicode_startswith/endswith: str or tuple-of-str; a non-str member
        // raises the combined message.
        private static System.Collections.Generic.List<string> NormalizePrefixTuple(PyObject arg, string what)
        {
            var list = new System.Collections.Generic.List<string>();
            if (arg is PyString single)
            {
                list.Add(single.Value);
                return list;
            }
            if (arg is PyTuple tuple)
            {
                foreach (var item in tuple.Items)
                {
                    if (item is not PyString part)
                        throw PyTypeError.Create($"tuple for {what} must only contain str, not {item.GetTypeName()}");
                    list.Add(part.Value);
                }
                return list;
            }
            throw PyTypeError.Create($"{what} first arg must be str or a tuple of str, not {arg.GetTypeName()}");
        }

        private static bool IsLineBreak(char c) =>
            c == '\n' || c == '\r' || c == '\v' || c == '\f' ||
            c == '\x1c' || c == '\x1d' || c == '\x1e' || c == '\x85' ||
            c == '\u2028' || c == '\u2029';

        internal static System.Collections.Generic.List<PyObject> SplitLinesCore(string s, bool keepends)
        {
            var items = new System.Collections.Generic.List<PyObject>();
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (!IsLineBreak(s[i]))
                    continue;
                int brkLen = s[i] == '\r' && i + 1 < s.Length && s[i + 1] == '\n' ? 2 : 1;
                items.Add(PyString.From(keepends ? s.Substring(start, i + brkLen - start) : s.Substring(start, i - start)));
                i += brkLen - 1;
                start = i + 1;
            }
            if (start < s.Length)
                items.Add(PyString.From(s.Substring(start)));
            return items;
        }

        // Receiver resolution for str descriptors: exact str or the content storage of a
        // str subclass instance (CPython looks through the subclass via the unicode type).
        /// <summary>
        /// CPython str find/index/rfind/rindex/count/startswith/endswith parse start/end
        /// via PyNumber_Index: bool and __index__ are accepted, None keeps the default,
        /// and any other type raises with this exact wording.
        /// </summary>
        private static void ParseStrStartStop(PyObject[] args, int length, out int start, out int end)
        {
            start = 0;
            end = length;
            if (args.Length >= 2 && args[1] != PyNone.Instance)
            {
                start = PyArgs.SliceIndex(args[1], 0);
            }
            if (args.Length >= 3 && args[2] != PyNone.Instance)
            {
                end = PyArgs.SliceIndex(args[2], length);
            }
        }

        /// <summary>
        /// CPython builtins declared METH_FASTCALL|METH_KEYWORDS resolve a keyword into its named
        /// parameter slot (see Objects/clinic/unicodeobject.c.h for split/rsplit/encode/expandtabs).
        /// SharpPy delegates read the positional array, so keywords are projected into that array and
        /// the existing positional parsing - defaults, type checks, messages - then applies unchanged.
        /// Arity checks keep using the original args array because CPython counts positional arguments
        /// separately from keywords.
        /// </summary>
        private static PyObject[] ProjectKeywordSlots(PyObject[] args, PyDict kwargs, params string[] names)
        {
            if (kwargs == null || kwargs.Length() == 0)
                return args;

            var projected = new PyObject[Math.Max(args.Length, names.Length)];
            Array.Copy(args, projected, args.Length);
            for (int i = 0; i < names.Length; i++)
            {
                if (i < args.Length)
                    continue;
                var value = KwValue(kwargs, names[i]);
                if (value != null)
                    projected[i] = value;
            }
            return projected;
        }

        internal static PyString AsStrReceiver(PyObject self)
        {
            return self as PyString;
        }

        /// <summary>
        /// Receiver resolution plus CPython's own complaint. Objects/descrobject.c descr_check
        /// produces "descriptor 'NAME' for 'str' objects doesn't apply to a 'X' object"; every str
        /// method descriptor used to hand-write "requires a 'str' object but received a 'X'", which
        /// is a wording CPython never emits. One helper, one message (P0-29).
        /// </summary>
        internal static PyString AsStrReceiver(PyObject self, string name)
        {
            PyString value = AsStrReceiver(self);
            if (value == null)
                throw PyTypeError.Create($"descriptor '{name}' for 'str' objects doesn't apply to a '{self.GetTypeName()}' object");
            return value;
        }

        /// <summary>
        /// CPython 3.12: str.__mul__ and str.__rmul__ - String repetition
        /// CPython: Objects/unicodeobject.c:unicode_repeat (lines ~10500-10570)
        /// Note: __rmul__ is handled by binary_op1 and sequence_repeat in the VM (PyVM.BinaryOp1)
        /// </summary>
        public override PyObject Multiply(PyObject other)
        {
            if (other is not PyInt count)
                return PyNotImplemented.Instance;

            long times = PyAllocation.IndexSized(count.Value);
            if (times <= 0)
                return PyString.From("");
            // unicode_repeat: once is the text unchanged.
            if (times == 1)
                return ResultUnchanged();
            // unicode_repeat: "repeated string is too long" past Py_ssize_t, then PyUnicode_New.
            PyAllocation.Product(CodePointLength(), times, "repeated string is too long");
            PyAllocation.StringLength(PyAllocation.Product(Value.Length, times, "repeated string is too long"));
            if (Value.Length == 0)
                return PyString.From("");

            // Performance: Eliminated LINQ (Enumerable.Repeat) - manual string repetition
            var sb = new StringBuilder(Value.Length * (int)times);
            for (long i = 0; i < times; i++)
            {
                sb.Append(Value);
            }
            return PyString.From(sb.ToString());
        }

        /// <summary>
        /// 문자열 포맷팅 (% 연산자) - CPython 3.12 호환
        /// 예: "Hello %s" % "world" -> "Hello world"
        ///     "_%s__" % (name,) -> "_name__"
        /// </summary>
        public override PyObject Modulo(PyObject other)
        {
            // Full CPython 3.12 unicode_format rework: flags/width/precision/mapping
            // keys and the o/x/X/c/d/i/a conversions with CPython-exact error texts.
            var formatted = FormatPercent(Value, other);
            return SoleLiteral(this, Value.IndexOf('%') < 0) ?? PyString.From(formatted);
        }

        /// <summary>
        /// _PyUnicodeWriter_Finish when the writer holds nothing but the whole format string, written
        /// once (WriteSubstring of the full range is WriteStr, which keeps the object itself): the
        /// format string comes back as it is, a str subclass instance included. An empty one is "".
        /// </summary>
        private static PyString SoleLiteral(PyString format, bool noFields) =>
            noFields && format.Value.Length > 0 ? format : null;

        private static string FormatPercent(string fmt, PyObject arg)
        {
            // CPython unicode_format: a non-tuple right operand is one argument
            // (or a mapping); a tuple is a positional argument list.
            bool positional = arg is PyTuple;
            PyObject[] args = positional ? ((PyTuple)arg).Items : null;
            int ai = 0;
            bool singleUsed = false;
            var sb = new StringBuilder();
            int i = 0;

            PyObject NextPositional()
            {
                if (!positional)
                {
                    if (singleUsed)
                        throw PyTypeError.Create("not enough arguments for format string");
                    singleUsed = true;
                    return arg;
                }
                if (ai >= args.Length)
                    throw PyTypeError.Create("not enough arguments for format string");
                return args[ai++];
            }

            // unicode_format_arg_parse: a "*" takes the next argument, which PyLong_Check must pass.
            PyObject NextStar()
            {
                var v = NextPositional();
                if (v is not PyInt && v is not PyBool)
                    throw PyTypeError.Create("* wants int");
                return v;
            }

            while (i < fmt.Length)
            {
                char c = fmt[i];
                if (c != '%')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                i++;
                if (i >= fmt.Length)
                    throw PyValueError.Create("incomplete format");
                if (fmt[i] == '%')
                {
                    sb.Append('%');
                    i++;
                    continue;
                }

                PyObject value = null;
                if (fmt[i] == '(')
                {
                    if (!positional)
                    {
                        // Mapping lookup goes through the object's __getitem__.
                        // CPython gates on PyMapping_Check: dict/list/user objects;
                        // str/int/float raise "format requires a mapping" instead.
                        if (!(arg is PyDict || arg is PyList || arg is PyClassInstance))
                            throw PyTypeError.Create("format requires a mapping");
                        i++;
                        int keyStart = i;
                        int depth = 1;
                        while (i < fmt.Length)
                        {
                            if (fmt[i] == '(')
                                depth++;
                            else if (fmt[i] == ')')
                            {
                                depth--;
                                if (depth == 0)
                                    break;
                            }
                            i++;
                        }
                        if (i >= fmt.Length)
                            throw PyValueError.Create("incomplete format; also it should contain a right parenthesis");
                        string key = fmt.Substring(keyStart, i - keyStart);
                        i++; // ')'
                        value = arg.GetItem(PyString.From(key));
                    }
                    else
                    {
                        throw PyTypeError.Create("format requires a mapping");
                    }
                }
                else
                {
                    if (!positional && !(arg is PyTuple))
                    {
                        // Mapping argument but no (key): CPython requires the key.
                        if (IsMappingLike(arg))
                            throw PyTypeError.Create("format requires a mapping");
                    }
                    // Values are consumed after the spec is parsed (CPython order),
                    // so a '*' width claims its argument before the conversion value.
                }

                bool alt = false, leftAdjust = false, signPlus = false, signSpace = false, zeroPad = false;
                while (i < fmt.Length)
                {
                    char f = fmt[i];
                    if (f == '#') { alt = true; i++; }
                    else if (f == '-') { leftAdjust = true; i++; }
                    else if (f == '0') { zeroPad = true; i++; }
                    else if (f == '+') { signPlus = true; i++; }
                    else if (f == ' ') { signSpace = true; i++; }
                    else break;
                }

                // unicode_format_arg_parse: the width is a Py_ssize_t (a negative "*" one left-adjusts),
                // the precision a C int (a negative "*" one is 0); digits past those are "too big".
                long widthValue = -1;
                if (i < fmt.Length && fmt[i] == '*')
                {
                    i++;
                    widthValue = PyArgs.Ssize(NextStar());
                    if (widthValue < 0)
                    {
                        leftAdjust = true;
                        widthValue = widthValue == long.MinValue ? long.MaxValue : -widthValue;
                    }
                }
                else if (i < fmt.Length && char.IsAsciiDigit(fmt[i]))
                {
                    widthValue = 0;
                    while (i < fmt.Length && char.IsAsciiDigit(fmt[i]))
                    {
                        int d = fmt[i] - '0';
                        if (widthValue > (long.MaxValue - d) / 10)
                            throw PyValueError.Create("width too big");
                        widthValue = widthValue * 10 + d;
                        i++;
                    }
                }
                // A result no CLR string can hold is the MemoryError of its allocation.
                if (widthValue > int.MaxValue)
                    throw PyMemoryError.Create();
                int width = (int)widthValue;

                int precision = 6;
                bool hasPrecision = false;
                if (i < fmt.Length && fmt[i] == '.')
                {
                    hasPrecision = true;
                    i++;
                    if (i < fmt.Length && fmt[i] == '*')
                    {
                        i++;
                        precision = PyArgs.Int(NextStar());
                        if (precision < 0)
                            precision = 0;
                    }
                    else
                    {
                        precision = 0;
                        while (i < fmt.Length && char.IsAsciiDigit(fmt[i]))
                        {
                            int d = fmt[i] - '0';
                            if (precision > (int.MaxValue - d) / 10)
                                throw PyValueError.Create("precision too big");
                            precision = precision * 10 + d;
                            i++;
                        }
                    }
                }

                // unicode_format_arg_parse: one "h", "l" or "L" length prefix is ignored ("%ld").
                if (i < fmt.Length && fmt[i] is 'h' or 'l' or 'L')
                    i++;
                if (i >= fmt.Length)
                    throw PyValueError.Create("incomplete format");
                char conv = fmt[i];
                int convIndex = i;
                i++;
                value ??= NextPositional();

                string body;
                string sign = "";
                bool numeric = false;
                switch (conv)
                {
                    case 's':
                        body = value.ToStr().Value;
                        if (hasPrecision && body.Length > precision)
                            body = body.Substring(0, precision);
                        break;
                    case 'r':
                        body = value.ToRepr().Value;
                        if (hasPrecision && body.Length > precision)
                            body = body.Substring(0, precision);
                        break;
                    case 'a':
                        body = AsciiEscape(value.ToRepr().Value);
                        if (hasPrecision && body.Length > precision)
                            body = body.Substring(0, precision);
                        break;
                    case 'd':
                    case 'i':
                    case 'u':
                    {
                        numeric = true;
                        BigInteger ival = PyPercentFormat.Integer(value, conv);
                        if (ival.Sign < 0) { sign = "-"; ival = -ival; }
                        else if (signPlus) sign = "+";
                        else if (signSpace) sign = " ";
                        body = ival.ToString(CultureInfo.InvariantCulture);
                        // _PyUnicode_FormatLong: the precision is a minimum of digits.
                        if (hasPrecision && body.Length < precision)
                            body = new string('0', precision - body.Length) + body;
                        break;
                    }
                    case 'o':
                    case 'x':
                    case 'X':
                    {
                        numeric = true;
                        BigInteger ival = PyPercentFormat.Integer(value, conv);
                        if (ival.Sign < 0) { sign = "-"; ival = -ival; }
                        else if (signPlus) sign = "+";
                        else if (signSpace) sign = " ";
                        string digits = conv == 'o' ? ToBase(ival, 8, false)
                            : ToBase(ival, 16, conv == 'X');
                        if (hasPrecision && digits.Length < precision)
                            digits = new string('0', precision - digits.Length) + digits;
                        string prefix = alt
                            ? (conv == 'o' ? "0o" : conv == 'X' ? "0X" : "0x")
                            : "";
                        body = prefix + digits;
                        break;
                    }
                    case 'c':
                    {
                        // formatchar
                        if (value is PyString character)
                        {
                            if (character.CodePointLength() != 1)
                                throw PyTypeError.Create("%c requires int or char");
                            body = character.Value;
                            break;
                        }
                        var code = PyPercentFormat.Character(value) ??
                            throw PyTypeError.Create("%c requires int or char");
                        if (code < 0 || code > 0x10FFFF)
                            throw PyOverflowError.Create("%c arg not in range(0x110000)");
                        body = code < 0x10000 ? ((char)(int)code).ToString() : char.ConvertFromUtf32((int)code);
                        break;
                    }
                    case 'f':
                    case 'F':
                    case 'e':
                    case 'E':
                    case 'g':
                    case 'G':
                    {
                        numeric = true;
                        double dv = Modules.PyMathFunction.RealNumber(value);
                        // formatfloat: PyOS_double_to_string, one writer with format().
                        if (double.IsNaN(dv)) body = char.IsUpper(conv) ? "NAN" : "nan";
                        else if (double.IsInfinity(dv)) body = char.IsUpper(conv) ? "INF" : "inf";
                        else body = PyFloatFormatter.DoubleToString(Math.Abs(dv), conv, precision, alt);
                        if (!double.IsNaN(dv) && double.IsNegative(dv)) sign = "-";
                        else if (signPlus) sign = "+";
                        else if (signSpace) sign = " ";
                        break;
                    }
                    default:
                        throw PyValueError.Create($"unsupported format character '{conv}' (0x{(int)conv:x}) at index {convIndex}");
                }

                // Assemble sign + body into the field, CPython style.
                string core = numeric ? body : body;
                string head = sign + core;
                if (numeric && alt && (conv == 'o' || conv == 'x' || conv == 'X') && zeroPad && !leftAdjust && width > head.Length)
                {
                    int pad = width - head.Length;
                    // The zeros go between the sign and prefix and the digits.
                    int prefix = core.Length > 1 && core[0] == '0' && !char.IsDigit(core[1]) ? 2 : 0;
                    sb.Append(sign).Append(core, 0, prefix).Append('0', Math.Max(0, pad)).Append(core, prefix, core.Length - prefix);
                }
                else if (leftAdjust)
                {
                    sb.Append(head);
                    if (width > head.Length)
                        sb.Append(' ', width - head.Length);
                }
                else if (zeroPad && numeric && !signPlus && !signSpace)
                {
                    if (width > head.Length)
                    {
                        sb.Append(sign);
                        string digitsPart = core;
                        if (alt && (conv == 'x' || conv == 'X' || conv == 'o') && digitsPart.Length > 0 && digitsPart[0] == '0')
                        {
                            sb.Append(digitsPart.Substring(0, 1));
                            sb.Append('0', width - head.Length);
                            sb.Append(digitsPart.Substring(1));
                        }
                        else
                        {
                            sb.Append('0', width - head.Length).Append(digitsPart);
                        }
                    }
                    else
                        sb.Append(head);
                }
                else if (zeroPad && numeric && (signPlus || signSpace))
                {
                    // sign occupies the first char; zeros fill after the sign
                    sb.Append(sign);
                    if (width > head.Length)
                        sb.Append('0', width - head.Length);
                    sb.Append(core);
                }
                else
                {
                    if (width > head.Length)
                        sb.Append(' ', width - head.Length);
                    sb.Append(head);
                }
            }

            // PyUnicode_Format: ctx.dict is a mapping that is no tuple or str.
            if ((positional && ai < args.Length) || (!positional && !singleUsed &&
                (arg is PyString || !arg.HasTypeSlot("__getitem__"))))
                throw PyTypeError.Create("not all arguments converted during string formatting");
            return sb.ToString();
        }

        private static bool IsMappingLike(PyObject o) => o is PyDict;

        private static string ToBase(BigInteger v, int radix, bool upper)
        {
            if (v.IsZero)
                return "0";
            var chars = new List<char>();
            while (v > 0)
            {
                v = BigInteger.DivRem(v, radix, out var rem);
                int d = (int)rem;
                chars.Add(d < 10 ? (char)('0' + d) : (char)((upper ? 'A' : 'a') + d - 10));
            }
            chars.Reverse();
            return new string(chars.ToArray());
        }

        internal static string AsciiEscape(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch < 128)
                    sb.Append(ch);
                else if (ch < 256)
                    sb.Append($"\\x{(int)ch:x2}");
                else
                    sb.Append($"\\u{(int)ch:x4}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 인덱스 접근 str[i]
        /// </summary>
        public PyString GetItem(int index)
        {
            // CPython: Objects/unicodeobject.c:11368 unicode_getitem bounds-checks against
            // PyUnicode_GET_LENGTH and returns unicode_char(PyUnicode_READ(kind, data, index)).
            int chars = CodePointLength();
            if (index < 0) index += chars;

            if (index < 0 || index >= chars)
                throw PyIndexError.Create("string index out of range");

            int unit = CodePointToUnit(Value, index);
            return PyString.From(Value.Substring(unit, PairWidth(Value, unit)));
        }
        
        /// <summary>
        /// PyObject.GetItem 오버라이드 - 인덱싱 및 슬라이싱 지원
        /// </summary>
        protected internal override PyObject MpSubscript(PyObject index)
        {
            if (index is PyInt pyInt)
            {
                // unicode_subscript: PyNumber_AsSsize_t(item, PyExc_IndexError).
                return GetItem(PyObject.GetIndex(pyInt, "string"));
            }
            else if (index is PySlice slice)
            {
                // CPython: unicode_subscript -> PySlice_GetIndicesEx on the character length,
                // then a character at a time. StepCodePoints is the one implementation of that walk.
                var (start, stop, step) = slice.StorageIndices(CodePointLength());
                return PyString.From(StepCodePoints(start, stop, step));
            }
            else
            {
                // CPython 3.12: Objects/unicodeobject.c unicode_subscript uses
                // PyNumber_AsSsize_t for non-slice keys (accepts __index__) and reports
                // "string indices must be integers, not 'T'" - different wording from list.
                try
                {
                    int idx = PyObject.GetIndex(index, "string");
                    return GetItem(idx);
                }
                catch (PythonException)
                {
                    throw PyTypeError.Create($"string indices must be integers, not '{index.GetTypeName()}'");
                }
            }
        }

        /// <summary>
        /// 슬라이싱 str[start:end]
        /// </summary>
        public PyString GetSlice(int? start = null, int? end = null, int step = 1)
        {
            if (step == 0)
                throw PyValueError.Create("slice step cannot be zero");
            
            // Same coordinate system as GetItem: characters, not code units.
            int len = CodePointLength();
            int actualStart = start ?? (step > 0 ? 0 : len - 1);
            int actualEnd = end ?? (step > 0 ? len : -1);

            if (actualStart < 0) actualStart += len;
            if (actualEnd < 0) actualEnd += len;

            if (step > 0)
            {
                if (actualStart < 0) actualStart = 0;
                if (actualEnd > len) actualEnd = len;
            }
            else
            {
                if (actualStart > len - 1) actualStart = len - 1;
                if (actualEnd < -1) actualEnd = -1;
            }

            return PyString.From(StepCodePoints(actualStart, actualEnd, step));
        }

        #endregion

        #region String Methods

        private static (string, long) SplitArguments(PyObject[] arguments)
        {
            string separator = null;
            if (arguments.Length >= 1 && arguments[0] != null && arguments[0] is not PyNone)
            {
                separator = arguments[0] switch
                {
                    PyString text => text.Value,
                    var other => throw PyTypeError.Create($"must be str or None, not {other.GetTypeName()}"),
                };
                if (separator.Length == 0)
                    throw PyValueError.Create("empty separator");
            }
            long limit = -1;
            if (arguments.Length >= 2 && arguments[1] != null)
                limit = PyArgs.Ssize(arguments[1]);
            return (separator, limit);
        }

        internal const int LeftStrip = 0, RightStrip = 1, BothStrip = 2;

        public PyString Strip(string chars = null) => StripText(BothStrip, chars);
        public PyString LStrip(string chars = null) => StripText(LeftStrip, chars);
        public PyString RStrip(string chars = null) => StripText(RightStrip, chars);

        /// <summary>
        /// do_strip and _PyUnicode_XStrip (Objects/unicodeobject.c): whole code points are stripped,
        /// Py_UNICODE_ISSPACE ones when chars is null, else those of chars; the result is
        /// PyUnicode_Substring, the text unchanged (unicode_result_unchanged) when nothing went.
        /// </summary>
        internal PyString StripText(int striptype, string chars)
        {
            string text = Value;
            HashSet<int> set = null;
            if (chars != null)
            {
                set = new HashSet<int>();
                for (int u = 0; u < chars.Length; u += PairWidth(chars, u))
                    set.Add(CodePointAtUnit(chars, u));
            }
            bool Stripped(int cp) => set == null ? IsWhiteSpaceCp(cp) : set.Contains(cp);
            int i = 0, j = text.Length;
            if (striptype != RightStrip)
                while (i < j && Stripped(CodePointAtUnit(text, i)))
                    i += PairWidth(text, i);
            if (striptype != LeftStrip)
                while (j > i)
                {
                    int k = j - 1;
                    if (IsContinuation(text, k)) k--;
                    if (!Stripped(CodePointAtUnit(text, k))) break;
                    j = k;
                }
            return i == 0 && j == text.Length ? ResultUnchanged() : PyString.From(text.Substring(i, j - i));
        }

        /// <summary>
        /// unicode_result_unchanged (Objects/unicodeobject.c): an exact str is its own result, a
        /// subclass instance gives an exact copy.
        /// </summary>
        // unicode_get_empty: every exact empty str is the one singleton in CPython.
        private bool IsEmptySingleton => Value.Length == 0 && !HasCustomType;

        internal PyString ResultUnchanged() => HasCustomType ? PyString.From(Value) : this;

        // replace (Objects/unicodeobject.c): the text is walked once, what was put in is not searched.
        public PyString Replace(string old, string newStr, long count = -1)
        {
            if (count < 0) count = long.MaxValue;
            if (count == 0) return ResultUnchanged();
            // replace(): the matches are counted first, then the new size is checked
            // ("replace string is too long" past Py_ssize_t) and allocated once.
            long matches = 0;
            if (old.Length == 0)
                matches = Math.Min(count, (long)CodePointLength() + 1);
            else
                for (int at = Value.IndexOf(old, StringComparison.Ordinal); at >= 0 && matches < count;
                     at = Value.IndexOf(old, at + old.Length, StringComparison.Ordinal))
                    matches++;
            if (matches == 0) return ResultUnchanged();
            // replace(): only a growing replacement can pass PY_SSIZE_T_MAX.
            long growth = newStr.Length > old.Length
                ? PyAllocation.Product(newStr.Length - old.Length, matches, "replace string is too long")
                : (long)(newStr.Length - old.Length) * matches;
            var output = new StringBuilder(PyAllocation.StringLength(Value.Length + growth));
            int position = 0;
            if (old.Length == 0)
            {
                // Before every character, and after the last one.
                long inserted = 0;
                while (true)
                {
                    if (inserted < count)
                    {
                        output.Append(newStr);
                        inserted++;
                    }
                    if (position >= Value.Length) break;
                    int width = PairWidth(Value, position);
                    output.Append(Value, position, width);
                    position += width;
                    if (inserted >= count)
                    {
                        output.Append(Value, position, Value.Length - position);
                        break;
                    }
                }
                return PyString.Fresh(output.ToString());
            }
            for (long done = 0; done < count; done++)
            {
                int index = Value.IndexOf(old, position, StringComparison.Ordinal);
                if (index < 0) break;
                output.Append(Value, position, index - position).Append(newStr);
                position = index + old.Length;
            }
            if (position == 0) return ResultUnchanged();
            output.Append(Value, position, Value.Length - position);
            return PyString.Fresh(output.ToString());
        }

        public PyInt Find(string sub, int start = 0, int? end = null)
        {
            var actualEnd = end ?? Value.Length;
            if (start < 0) start = 0;
            if (actualEnd > Value.Length) actualEnd = Value.Length;

            // CPython 3.12: Objects/unicodeobject.c:13500-13550 - unicode_find
            // If start >= end, return -1 (empty string search)
            if (start >= actualEnd) return PyInt.From(-1);

            var searchIn = start == 0 && actualEnd == Value.Length
                ? Value
                : Value.Substring(start, actualEnd - start);

            var index = searchIn.IndexOf(sub, StringComparison.Ordinal);
            return PyInt.From(index == -1 ? -1 : index + start);
        }

        public PyInt Index(string sub, int start = 0, int? end = null)
        {
            var result = Find(sub, start, end);
            if (((PyInt)result).Value == -1)
                throw PyValueError.Create("substring not found");
            return (PyInt)result;
        }

        public PyInt Count(string sub)
        {
            if (string.IsNullOrEmpty(sub)) return PyInt.From(Value.Length + 1);
            
            int count = 0;
            int index = 0;
            while ((index = Value.IndexOf(sub, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += sub.Length;
            }
            return PyInt.From(count);
        }

        public PyBool StartsWith(string prefix) => PyBool.FromBool(Value.StartsWith(prefix, StringComparison.Ordinal));
        public PyBool EndsWith(string suffix) => PyBool.FromBool(Value.EndsWith(suffix, StringComparison.Ordinal));

        /// <summary>
        /// str.split and str.rsplit: Objects/stringlib/split.h (split_whitespace, split,
        /// rsplit_whitespace, rsplit), a negative maxcount being PY_SSIZE_T_MAX. The whitespace test
        /// is Py_UNICODE_ISSPACE, which no surrogate passes, so the walk can be over code units.
        /// When nothing was split an exact str is its own only element.
        /// </summary>
        public PyList Split(string sep, long maxcount, bool fromRight = false)
        {
            string text = Value;
            int len = text.Length;
            if (maxcount < 0) maxcount = long.MaxValue;
            var parts = new List<PyObject>();
            PyObject Piece(int start, int end) =>
                start == 0 && end == len && !HasCustomType ? this : PyString.From(text.Substring(start, end - start));
            if (sep == null && !fromRight)
            {
                int i = 0, j;
                while (maxcount-- > 0)
                {
                    while (i < len && IsWhiteSpaceCp(text[i])) i++;
                    if (i == len) break;
                    j = i; i++;
                    while (i < len && !IsWhiteSpaceCp(text[i])) i++;
                    parts.Add(Piece(j, i));
                }
                if (i < len)
                {
                    // only when maxcount was reached: the rest, without its leading whitespace
                    while (i < len && IsWhiteSpaceCp(text[i])) i++;
                    if (i != len) parts.Add(Piece(i, len));
                }
            }
            else if (sep == null)
            {
                int i = len - 1, j;
                while (maxcount-- > 0)
                {
                    while (i >= 0 && IsWhiteSpaceCp(text[i])) i--;
                    if (i < 0) break;
                    j = i; i--;
                    while (i >= 0 && !IsWhiteSpaceCp(text[i])) i--;
                    parts.Add(Piece(i + 1, j + 1));
                }
                if (i >= 0)
                {
                    while (i >= 0 && IsWhiteSpaceCp(text[i])) i--;
                    if (i >= 0) parts.Add(Piece(0, i + 1));
                }
                parts.Reverse();
            }
            else if (!fromRight)
            {
                int i = 0;
                while (maxcount-- > 0)
                {
                    int pos = text.IndexOf(sep, i, StringComparison.Ordinal);
                    if (pos < 0) break;
                    parts.Add(Piece(i, pos));
                    i = pos + sep.Length;
                }
                parts.Add(Piece(i, len));
            }
            else
            {
                int j = len;
                while (maxcount-- > 0)
                {
                    int pos = j < sep.Length ? -1 : text.LastIndexOf(sep, j - 1, j, StringComparison.Ordinal);
                    if (pos < 0) break;
                    parts.Add(Piece(pos + sep.Length, j));
                    j = pos;
                }
                parts.Add(Piece(0, j));
                parts.Reverse();
            }
            return new PyList(parts);
        }

        public PyString Join(PyObject iterable)
        {
            // stringlib join: PySequence_Fast consumes the iterable before checking element types.
            PyObject[] values = PyList.SequenceFast(iterable, "can only join an iterable");
            if (values.Length == 0) return PyString.From("");
            if (values.Length == 1 && values[0].GetType() == typeof(PyString)) return (PyString)values[0];
            var strings = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is not PyString item)
                    throw PyTypeError.Create($"sequence item {i}: expected str instance, {values[i].GetTypeName()} found");
                strings[i] = item.Value;
            }
            // _PyUnicode_JoinArray: the size first, then one allocation.
            long size = (long)Value.Length * (strings.Length - 1);
            foreach (var piece in strings) size += piece.Length;
            PyAllocation.StringLength(size);
            return PyString.Fresh(string.Join(Value, strings));
        }

        #endregion

        #region Length and Contains

        // CPython: PyUnicode_GET_LENGTH counts characters. Everything above this line that needs a
        // unit offset converts through CodePointToUnit.
        protected internal override long Len() => HasPairs ? CodePointCount(Value) : Value.Length;
        
        public PyBool Contains(string substring) => PyBool.FromBool(Value.Contains(substring));
        
        /// <summary>
        /// CPython __contains__ 메소드 구현 - PyObject 버전
        /// </summary>
        // CPython 3.12: Objects/unicodeobject.c unicode_contains (sq_contains): substring
        // test where a non-str key raises TypeError. Converting the key via ToStr() was
        // wrong: `9 in "abc"` answered False where CPython raises
        // "'in <string>' requires string as left operand, not int".
        protected internal override PyBool ContainsItem(PyObject item)
        {
            if (item is PyString pyStr)
                return PyBool.FromBool(Value.Contains(pyStr.Value));

            throw PyTypeError.Create($"'in <string>' requires string as left operand, not {item.GetTypeName()}");
        }

        #endregion

        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyString → C# basic types) ===
        
        /// <summary>
        /// CPython PyObject_IsTrue 호환: PyString에서 C# bool 값 추출
        /// </summary>
        
        /// <summary>
        /// CPython PyUnicode_AsLong 호환: PyString에서 C# int 값 추출
        /// </summary>
        public override int ToInt()
        {
            if (int.TryParse(Value.Trim(), out int result))
                return result;
            throw PyValueError.Create($"invalid literal for int() with base 10: '{Value}'");
        }
        
        public override double ToFloat() => PyFloat.FromObject(this).Value;

        // === As* Methods: Type Conversion (PyString → PyObject types) ===
        
        /// <summary>
        /// C# 네이티브 타입 변환: PyString → C# string
        /// </summary>
        public override string ToClrString() => Value;
        
        /// <summary>
        /// CPython 호환: PyString을 PyInt로 변환
        /// </summary>
        
        /// <summary>
        /// CPython 호환: PyString을 PyFloat로 변환
        /// </summary>
        
        /// <summary>
        /// CPython 호환: PyString을 PyBool로 변환
        /// </summary>
        public override PyBool AsBool()
        {
            return PyBool.FromBool(Value.Length > 0);
        }
        
        #endregion

        #region Encoding

        /// <summary>
        /// The single strict encoder behind str.encode() and the _codecs.*_encode entry points.
        /// There used to be two implementations: the live descriptor picked a .NET Encoding itself,
        /// so .NET replaced whatever it could not encode - 'é'.encode('ascii') answered b'?' and a
        /// lone surrogate answered b'\xef\xbf\xbd'. Silent corruption. CPython refuses, and these
        /// refusals are measured with CHARACTER positions (a surrogate pair is one character, a lone
        /// surrogate is one character):
        ///   'utf-8' codec can't encode character '\ud800' in position 0: surrogates not allowed
        ///   'ascii' codec can't encode character '\xe9' in position 1: ordinal not in range(128)
        ///   'latin-1' codec can't encode characters in position 0-1: ordinal not in range(256)
        /// The error handler is resolved on first use, not upfront, because CPython only consults it
        /// when something actually fails: 'e'.encode('ascii', 'bogus') succeeds.
        /// </summary>
        public PyBytes Encode(string encoding = "utf-8", string errors = null)
        {
            string codec = PyStringNewArguments.NormalizeCodecName(encoding ?? "utf-8");
            var ctx = new EncodeContext(codec, errors);

            switch (codec)
            {
                case "utf-8":
                    return PyBytes.From(EncodeCodePoints(ctx, bigEndian: false));
                case "utf-16":
                    return PyBytes.From(WithPreamble(
                        EncodeCodePoints(ctx, bigEndian: false), new byte[] { 0xFF, 0xFE }));
                case "utf-16-le":
                    return PyBytes.From(EncodeCodePoints(ctx, bigEndian: false));
                case "utf-16-be":
                    return PyBytes.From(EncodeCodePoints(ctx, bigEndian: true));
                case "utf-32":
                    return PyBytes.From(WithPreamble(
                        EncodeCodePoints(ctx, bigEndian: false), new byte[] { 0xFF, 0xFE, 0x00, 0x00 }));
                case "utf-32-le":
                    return PyBytes.From(EncodeCodePoints(ctx, bigEndian: false));
                case "utf-32-be":
                    return PyBytes.From(EncodeCodePoints(ctx, bigEndian: true));
                case "ascii":
                case "latin-1":
                    return PyBytes.From(EncodeCodePoints(ctx, bigEndian: false));
                default:
                    // Not in the native table: the registry answers. A non-text codec is refused by
                    // name ('ab'.encode('rot13') -> "'rot13' is not a text encoding; use
                    // codecs.encode() to handle arbitrary codecs"), a registered text codec is
                    // called, and anything else is still CPython's LookupError naming the caller's
                    // spelling. Codecs with no implementation here stay in docs/known_gaps.md.
                    PyObject viaRegistry = PyCodecRegistry.EncodeText(this, encoding, ctx.Errors);
                    if (!(viaRegistry is PyBytes encoded))
                        throw PyTypeError.Create(
                            $"encoding with '{encoding}' must return bytes, not {viaRegistry.GetTypeName()}");
                    return encoded;
            }
        }

        /// <summary>Everything the byte walk needs to know about the requested codec.</summary>
        private sealed class EncodeContext
        {
            internal EncodeContext(string codec, string errors)
            {
                Codec = codec;
                // PyCodec_LookupError takes the name as given.
                Errors = errors ?? "strict";
                IsUtf = codec == "utf-8" || codec == "utf-16" || codec == "utf-16-le" ||
                        codec == "utf-16-be" || codec == "utf-32" || codec == "utf-32-le" ||
                        codec == "utf-32-be";
                Limit = codec == "ascii" ? 0x7F : codec == "latin-1" ? 0xFF : int.MaxValue;
                Width = codec == "utf-8" ? 0 : codec.StartsWith("utf-16", StringComparison.Ordinal) ? 2
                      : codec.StartsWith("utf-32", StringComparison.Ordinal) ? 4 : 1;
            }

            internal readonly string Codec;
            internal readonly string Errors;
            internal readonly bool IsUtf;
            internal readonly int Limit;
            internal readonly int Width;
        }

        /// <summary>
        /// Walk code points and emit bytes: one pass, refusal included. Fixed-width codecs write
        /// 1/2/4 bytes per character (UTF-16 splits an astral character into a surrogate pair, which
        /// is why "chr(0x10400).encode('utf-16-be')" measures b'\xd8\x01\xdc\x00'); UTF-8 writes a
        /// length that depends on the value.
        /// </summary>
        private byte[] EncodeCodePoints(EncodeContext ctx, bool bigEndian)
        {
            var bytes = new List<byte>(Value.Length * 2);
            int cpIndex = 0;
            int i = 0;
            while (i < Value.Length)
            {
                int cp = Value[i];
                int unitWidth = 1;
                if (char.IsHighSurrogate(Value[i]) && i + 1 < Value.Length &&
                    char.IsLowSurrogate(Value[i + 1]))
                {
                    cp = char.ConvertToUtf32(Value[i], Value[i + 1]);
                    unitWidth = 2;
                }

                // A lone surrogate is out of range for every codec: CPython refuses
                // chr(0xD800).encode('latin-1') too, with "ordinal not in range(256)". Only the UTF
                // codecs get the "surrogates not allowed" wording, so the two refusals stay apart.
                bool loneSurrogate = unitWidth == 1 && char.IsSurrogate(Value[i]);
                bool outOfRange = cp > ctx.Limit;
                bool unencodable = outOfRange || loneSurrogate && ctx.IsUtf;
                bool surrogateRefusal = loneSurrogate && ctx.IsUtf;

                if (!unencodable)
                {
                    Emit(bytes, cp, ctx, bigEndian);
                    i += unitWidth;
                    cpIndex++;
                    continue;
                }

                switch (ctx.Errors)
                {
                    case "ignore":
                        i += unitWidth;
                        cpIndex++;
                        continue;
                    case "replace":
                        // One '?' per character, including astral ones. Measured.
                        bytes.Add((byte)'?');
                        i += unitWidth;
                        cpIndex++;
                        continue;
                    case "surrogatepass":
                        if (!surrogateRefusal) throw RangeError(ctx, cpIndex, cp, unitWidth);
                        Emit(bytes, Value[i], ctx, bigEndian);
                        i += unitWidth;
                        cpIndex++;
                        continue;
                    case "backslashreplace":
                        // Measured: 'a' + chr(0xE9) + chr(0xEA) + 'b' under ascii gives
                        // b'a\\xe9\\xeab', chr(0x10400) gives b'\\U00010400', a lone surrogate gives
                        // b'\\ud800'. The escape is ASCII, so it is written raw for every codec.
                        AppendEscape(bytes, loneSurrogate ? Value[i] : cp);
                        i += unitWidth;
                        cpIndex++;
                        continue;
                    case "xmlcharrefreplace":
                        AppendAscii(bytes, $"&#{(loneSurrogate ? Value[i] : cp)};");
                        i += unitWidth;
                        cpIndex++;
                        continue;
                    case "surrogateescape":
                        // CPython maps only U+DC80..U+DCFF back to the bytes 0x80..0xFF; any other
                        // surrogate is refused with the strict message, which is why
                        // chr(0xD800).encode('utf-8', 'surrogateescape') still raises.
                        if (loneSurrogate && ctx.Codec == "utf-8" && Value[i] >= 0xDC80 &&
                            Value[i] <= 0xDCFF)
                        {
                            bytes.Add((byte)(Value[i] - 0xDC00));
                            i += unitWidth;
                            cpIndex++;
                            continue;
                        }
                        throw surrogateRefusal ? SurrogateError(ctx, cpIndex, Value[i])
                                               : RangeError(ctx, cpIndex, cp, unitWidth);
                    case "strict":
                        throw surrogateRefusal ? SurrogateError(ctx, cpIndex, Value[i])
                                               : RangeError(ctx, cpIndex, cp, unitWidth);
                    default:
                        (i, cpIndex) = CallEncodeHandler(bytes, ctx, bigEndian, cpIndex, unitWidth, surrogateRefusal, cp);
                        continue;
                }
            }
            return bytes.ToArray();
        }

        /// <summary>
        /// unicode_encode_call_errorhandler for the run of unencodable characters starting at
        /// cpIndex: the handler's str is encoded again and must be encodable (ASCII for the UTF
        /// codecs), else the original refusal stands; its bytes are written as they are.
        /// Answers the unit and character positions encoding resumes at.
        /// </summary>
        private (int Unit, int Character) CallEncodeHandler(List<byte> bytes, EncodeContext ctx, bool bigEndian,
            int cpIndex, int unitWidth, bool surrogateRefusal, int cp)
        {
            int runEnd = cpIndex + 1;
            int at = UnitIndexOfCharacter(cpIndex) + unitWidth;
            while (at < Value.Length)
            {
                int next = Value[at];
                int nextWidth = 1;
                if (char.IsHighSurrogate(Value[at]) && at + 1 < Value.Length && char.IsLowSurrogate(Value[at + 1]))
                {
                    next = char.ConvertToUtf32(Value[at], Value[at + 1]);
                    nextWidth = 2;
                }
                bool lone = nextWidth == 1 && char.IsSurrogate(Value[at]);
                bool bad = next > ctx.Limit || lone && ctx.IsUtf;
                if (!bad) break;
                runEnd++;
                at += nextWidth;
            }
            string reason = surrogateRefusal ? "surrogates not allowed" : $"ordinal not in range({ctx.Limit + 1})";
            var replacement = PyCodecErrors.CallEncode(ctx.Errors, ctx.Codec, reason, Value, cpIndex, runEnd,
                CodePointLength(), out int newPosition);
            if (replacement is PyBytes raw)
                bytes.AddRange(raw.Value);
            else
            {
                var text = ((PyString)replacement).Value;
                foreach (var ch in text)
                {
                    bool encodable = ctx.IsUtf ? ch < 0x80 : ch <= ctx.Limit;
                    if (!encodable)
                        throw surrogateRefusal ? SurrogateError(ctx, cpIndex, Value[UnitIndexOfCharacter(cpIndex)])
                                               : RangeError(ctx, cpIndex, cp, unitWidth);
                    Emit(bytes, ch, ctx, bigEndian);
                }
            }
            return (UnitIndexOfCharacter(newPosition), newPosition);
        }

        /// <summary>
        /// PyUnicode_GET_LENGTH: the text's length in code points, which a subclass's __len__ does
        /// not change (that is PyObject_Size, Length()).
        /// </summary>
        internal int CodePointLength() => (int)Len();

        private static void Emit(List<byte> bytes, int cp, EncodeContext ctx, bool bigEndian)
        {
            if (ctx.Width == 0)
            {
                EmitUtf8(bytes, cp);
                return;
            }
            if (ctx.Width == 2 && cp > 0xFFFF)
            {
                int v = cp - 0x10000;
                EmitUnit(bytes, 0xD800 + (v >> 10), bigEndian);
                EmitUnit(bytes, 0xDC00 + (v & 0x3FF), bigEndian);
                return;
            }
            for (int k = 0; k < ctx.Width; k++)
            {
                int shift = bigEndian ? 8 * (ctx.Width - 1 - k) : 8 * k;
                bytes.Add((byte)((cp >> shift) & 0xFF));
            }
        }

        private static void EmitUtf8(List<byte> bytes, int cp)
        {
            if (cp < 0x80)
            {
                bytes.Add((byte)cp);
            }
            else if (cp < 0x800)
            {
                bytes.Add((byte)(0xC0 | (cp >> 6)));
                bytes.Add((byte)(0x80 | (cp & 0x3F)));
            }
            else if (cp < 0x10000)
            {
                bytes.Add((byte)(0xE0 | (cp >> 12)));
                bytes.Add((byte)(0x80 | ((cp >> 6) & 0x3F)));
                bytes.Add((byte)(0x80 | (cp & 0x3F)));
            }
            else
            {
                bytes.Add((byte)(0xF0 | (cp >> 18)));
                bytes.Add((byte)(0x80 | ((cp >> 12) & 0x3F)));
                bytes.Add((byte)(0x80 | ((cp >> 6) & 0x3F)));
                bytes.Add((byte)(0x80 | (cp & 0x3F)));
            }
        }

        /// <summary>
        /// CPython's backslashreplace escape: \xNN up to 0xFF, \uXXXX up to 0xFFFF, \UXXXXXXXX
        /// beyond, always lower-case hex, always ASCII bytes.
        /// </summary>
        private static void AppendEscape(List<byte> bytes, int cp)
        {
            if (cp <= 0xFF) AppendAscii(bytes, $"\\x{cp:x2}");
            else if (cp <= 0xFFFF) AppendAscii(bytes, $"\\u{cp:x4}");
            else AppendAscii(bytes, $"\\U{cp:x8}");
        }

        private static void AppendAscii(List<byte> bytes, string text)
        {
            for (int k = 0; k < text.Length; k++) bytes.Add((byte)text[k]);
        }

        private static void EmitUnit(List<byte> bytes, int unit, bool bigEndian)
        {
            if (bigEndian)
            {
                bytes.Add((byte)((unit >> 8) & 0xFF));
                bytes.Add((byte)(unit & 0xFF));
            }
            else
            {
                bytes.Add((byte)(unit & 0xFF));
                bytes.Add((byte)((unit >> 8) & 0xFF));
            }
        }

        /// <summary>
        /// CPython reports a run of unencodable characters: one is "character '\xe9' in position 1",
        /// several are "characters in position 0-1". The escape follows the value - \xNN, \uXXXX,
        /// \UXXXXXXXX - and the old code wrote '\u00e9' for a character CPython writes as '\xe9'.
        /// </summary>
        private PythonException RangeError(EncodeContext ctx, int cpIndex, int cp, int unitWidth)
        {
            int start = cpIndex;
            int end = cpIndex;
            // The scan extends the run AFTER the failing character; starting on it counted that
            // character twice, so chr(0xE9).encode('ascii') claimed "characters in position 0-1"
            // for a one-character string.
            int at = UnitIndexOfCharacter(start) + unitWidth;
            while (at < Value.Length)
            {
                int next = Value[at];
                int nextWidth = 1;
                if (char.IsHighSurrogate(Value[at]) && at + 1 < Value.Length &&
                    char.IsLowSurrogate(Value[at + 1]))
                {
                    next = char.ConvertToUtf32(Value[at], Value[at + 1]);
                    nextWidth = 2;
                }
                bool bad = nextWidth == 1 && char.IsSurrogate(Value[at]) ? ctx.IsUtf : next > ctx.Limit;
                if (!bad) break;
                end++;
                at += nextWidth;
            }

            return PyUnicodeEncodeError.Failure(ctx.Codec, Value, start, end + 1,
                $"ordinal not in range({ctx.Limit + 1})");
        }

        private PythonException SurrogateError(EncodeContext ctx, int cpIndex, char unit) =>
            PyUnicodeEncodeError.Failure(ctx.Codec, Value, cpIndex, cpIndex + 1, "surrogates not allowed");

        /// <summary>Unit offset of a character index: the one place encode() converts the two.</summary>
        private int UnitIndexOfCharacter(int cpIndex)
        {
            int units = 0;
            for (int seen = 0; seen < cpIndex && units < Value.Length; seen++)
            {
                if (char.IsHighSurrogate(Value[units]) && units + 1 < Value.Length &&
                    char.IsLowSurrogate(Value[units + 1])) units += 2;
                else units++;
            }
            return units;
        }

        private static byte[] WithPreamble(byte[] body, byte[] preamble)
        {
            var all = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, all, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, all, preamble.Length, body.Length);
            return all;
        }

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// An exact str from finished text, as CPython's constructors that end in unicode_result
        /// (_PyUnicode_FromUCS1, PyUnicode_Substring, the decoders, the writer): the empty str is
        /// unicode_get_empty, a single latin-1 character is get_latin1_char's one object.
        /// The public constructor stays for host code; the gate keeps it out of the interpreter.
        /// </summary>
        public static PyString From(string value)
        {
            if (string.IsNullOrEmpty(value)) return Empty;
            if (value.Length == 1 && value[0] < 256) return Latin1[value[0]];
            return new PyString(value);
        }

        /// <summary>
        /// An exact str written into a fresh PyUnicode_New and returned as it is (case conversion,
        /// a multi-item join, replace, int to decimal): only the empty str is shared.
        /// </summary>
        public static PyString Fresh(string value) =>
            string.IsNullOrEmpty(value) ? Empty : new PyString(value);

        // get_latin1_char's table: the one str of each character below U+0100.
        private static readonly PyString[] Latin1 = MakeLatin1();
        private static PyString[] MakeLatin1()
        {
            var table = new PyString[256];
            for (int c = 0; c < 256; c++) table[c] = new PyString(((char)c).ToString());
            return table;
        }

        #endregion

        #region Constants

        public static readonly PyString Empty = new PyString("");

        #endregion

        #region String Methods


        private PyObject CountOccurrences(string text, string sub)
        {
            if (string.IsNullOrEmpty(sub)) return PyInt.From(0);
            
            int count = 0;
            int index = 0;
            
            while ((index = text.IndexOf(sub, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += sub.Length;
            }
            
            return PyInt.From(count);
        }

        internal PyObject IsIdentifier(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"str.isidentifier() takes no arguments ({args.Length} given)");
            return ScanIdentifier(Value) ? PyBool.True : PyBool.False;
        }

        #endregion

        #region Iterator Support

        /// <summary>
        /// Iterator support for string iteration (for char in "string")
        /// </summary>
        protected internal override PyObject Iter()
        {
            return new PyStringIterator(this);
        }

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // String literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion

        #region Format Spec Helper

        // CPython 3.12: Objects/stringlib/unicode_format.h - format_spec parsing
        // PEP 3101: Format Specification Mini-Language
        // [[fill]align][sign][#][0][width][,][.precision][type]
        public static string FormatString(string value, string formatSpec)
        {
            if (string.IsNullOrEmpty(formatSpec))
                return value;

            var format = FormatSpec.Parse("str", formatSpec, 's', '<');
            if (format.Type != 's')
                throw FormatSpec.Unknown(format.Type, "str");
            // format_string_internal: what a string has no use for is refused by name.
            if (format.Sign == ' ')
                throw PyValueError.Create("Space not allowed in string format specifier");
            if (format.Sign != '\0')
                throw PyValueError.Create("Sign not allowed in string format specifier");
            if (format.NoNegativeZero)
                throw PyValueError.Create("Negative zero coercion (z) not allowed in string format specifier");
            if (format.Alternate)
                throw PyValueError.Create("Alternate form (#) not allowed in string format specifier");
            if (format.Align == '=')
                throw PyValueError.Create("'=' alignment not allowed in string format specifier");
            if (format.Width > int.MaxValue) throw PyMemoryError.Create();
            string fill = format.Fill;
            char align = format.Align;
            int width = (int)format.Width;
            long precision = format.Precision;

            // Apply precision (truncation) and width, both in characters (Objects/stringlib/
            // format.h reads a character at a time).
            string result = value;
            int resultChars = CodePointCount(result);
            if (precision >= 0 && resultChars > precision)
            {
                result = SubstringCodePoints(result, 0, (int)precision);
                resultChars = (int)precision;
            }

            if (width > resultChars)
            {
                int padLen = width - resultChars;
                switch (align)
                {
                    case '<':  // left-aligned
                        result = result + Repeat(fill, padLen);
                        break;
                    case '>':  // right-aligned
                        result = Repeat(fill, padLen) + result;
                        break;
                    case '^':  // centered
                        int leftPad = padLen / 2;
                        int rightPad = padLen - leftPad;
                        result = Repeat(fill, leftPad) + result + Repeat(fill, rightPad);
                        break;
                }
            }

            return result;
        }

        #endregion
    }
}
