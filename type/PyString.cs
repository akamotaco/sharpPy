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

        internal static string Build(PyObject objectArg, PyObject encodingArg, PyObject errorsArg)
        {
            bool decodingRequested = encodingArg != null || errorsArg != null;

            if (!decodingRequested)
            {
                if (objectArg == null)
                    return "";
                // Measured: str(65) is '65', not 'A' - unicode_new runs PyObject_Str, and chr() is a
                // different function. An earlier draft here converted ints with chr() and was wrong.
                return objectArg.ToStr().Value;
            }

            // With encoding or errors present, CPython never accepts a str and never calls str().
            if (objectArg is PyString || objectArg is PyStrSubclass)
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
            return DecodeBytes(data, encodingName, errorName);
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
            System.Text.Encoding encoding = ResolveCodec(encodingName)
                ?? throw PyLookupError.Create($"unknown encoding: {encodingName}");

            string normalized = NormalizeCodecName(encodingName);

            // CPython's utf-16/utf-32 consume the byte-order mark instead of putting it in the
            // result; .NET's GetString left it as a leading U+FEFF in every round trip. A
            // big-endian mark also flips the byte order, and no mark means native little-endian.
            // Measured: "".encode("utf-16") == b'\xff\xfe'.
            if (normalized == "utf-16" || normalized == "utf-32")
            {
                int unit = normalized == "utf-16" ? 2 : 4;
                bool bigEndian = false;
                int skip = 0;
                bool littleMark = data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE &&
                                  (unit == 2 || (data.Length >= 4 && data[2] == 0x00 && data[3] == 0x00));
                bool bigMark = unit == 2
                    ? data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF
                    : data.Length >= 4 && data[0] == 0x00 && data[1] == 0x00 &&
                      data[2] == 0xFE && data[3] == 0xFF;
                if (littleMark) skip = unit;
                else if (bigMark) { skip = unit; bigEndian = true; }

                encoding = normalized == "utf-16"
                    ? (System.Text.Encoding)(bigEndian
                        ? System.Text.UnicodeEncoding.BigEndianUnicode
                        : System.Text.UnicodeEncoding.Unicode)
                    : (System.Text.Encoding)(bigEndian
                        ? new System.Text.UTF32Encoding(true, false)
                        : new System.Text.UTF32Encoding(false, false));

                if (skip != 0 && data.Length >= skip)
                {
                    var rest = new byte[data.Length - skip];
                    Buffer.BlockCopy(data, skip, rest, 0, rest.Length);
                    data = rest;
                }
            }

            bool strict = errorName == null || errorName == "strict";
            if (!strict && errorName != "ignore" && errorName != "replace" &&
                errorName != "surrogateescape" && errorName != "backslashreplace")
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
                    throw PyUnicodeDecodeError.Create(
                        $"'utf-8' codec can't decode byte 0x{bad:x2} in position {position}: invalid start byte");
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
                    return new PyString(PyUnicodeCase.Upper(str.Value));
                },
                minArgs: 0, maxArgs: 0
            );

            strType.TypeDict["lower"] = new PyMethodDescriptor(
                "lower", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"str.lower() takes no arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "lower");
                    return new PyString(PyUnicodeCase.Lower(str.Value));
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
                    return new PyString(PyUnicodeCase.Capitalize(str.Value));
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
                    return new PyString(PyUnicodeCase.Title(str.Value));
                },
                minArgs: 0, maxArgs: 0
            );

            strType.TypeDict["split"] = new PyMethodDescriptor(
                "split", strType,
                (self, args, kwargs) => {
                    if (args.Length > 2)
                        throw PyTypeError.Create($"split() takes at most 2 arguments ({args.Length} given)");
                    PyString str = AsStrReceiver(self, "split");

                    string sep = null;
                    int maxsplit = -1;
                    PyObject[] pa = ProjectKeywordSlots(args, kwargs, "sep", "maxsplit");
                    if (pa.Length >= 1 && pa[0] is PyString sepStr)
                        sep = sepStr.Value;
                    if (pa.Length >= 2 && pa[1] is PyInt maxsplitInt)
                        maxsplit = (int)maxsplitInt.Value;

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

                    if (args.Length == 0)
                        return new PyString(str.Value.Trim());

                    var chars = args[0] is PyString charsStr ? charsStr.Value.ToCharArray() : throw PyTypeError.Create("strip arg must be None or str");
                    return new PyString(str.Value.Trim(chars));
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

                    return args.Length == 3
                        ? str.Replace(old, newStr, (int)((PyInt)args[2]).Value)
                        : str.Replace(old, newStr);
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

                    int width = (int)widthInt.Value;
                    string value = str.Value;

                    // If already long enough, return unchanged
                    if (value.Length >= width)
                        return str;

                    int fillCount = width - value.Length;

                    // Check if string starts with '+' or '-'
                    if (value.Length > 0 && (value[0] == '+' || value[0] == '-'))
                    {
                        // Move sign to beginning: sign + zeros + rest
                        return new PyString(value[0] + new string('0', fillCount) + value.Substring(1));
                    }
                    else
                    {
                        // Just prepend zeros
                        return new PyString(new string('0', fillCount) + value);
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

                    foreach (char c in str.Value)
                    {
                        if (!char.IsDigit(c))
                            return PyBool.False;
                    }
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

                    foreach (char c in str.Value)
                    {
                        if (!char.IsLetter(c))
                            return PyBool.False;
                    }
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

                    foreach (char c in str.Value)
                    {
                        if (!char.IsLetterOrDigit(c))
                            return PyBool.False;
                    }
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

                    foreach (char c in str.Value)
                    {
                        if (!char.IsWhiteSpace(c))
                            return PyBool.False;
                    }
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
                        throw PyTypeError.Create($"find() takes from 1 to 3 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "find");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    ParseStrStartStop(args, str.Value.Length, out int start, out int end);

                    // CPython 3.12: Objects/unicodeobject.c:13500-13550 - unicode_find
                    // Normalize negative indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);

                    // Clamp to valid range
                    start = Math.Max(0, Math.Min(start, str.Value.Length));
                    end = Math.Max(0, Math.Min(end, str.Value.Length));

                    // Empty range check
                    if (start >= end) return new PyInt(-1);

                    int index = str.Value.IndexOf(sub.Value, start, end - start);
                    return new PyInt(index);
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
                        throw PyTypeError.Create($"index() takes from 1 to 3 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "index");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    ParseStrStartStop(args, str.Value.Length, out int start, out int end);

                    // CPython 3.12: Objects/unicodeobject.c:13500-13550 - unicode_find
                    // Normalize negative indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);

                    // Clamp to valid range
                    start = Math.Max(0, Math.Min(start, str.Value.Length));
                    end = Math.Max(0, Math.Min(end, str.Value.Length));

                    if (start >= end)
                        throw PyValueError.Create("substring not found");

                    int index = str.Value.IndexOf(sub.Value, start, end - start);
                    if (index == -1)
                        throw PyValueError.Create("substring not found");
                    return new PyInt(index);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:9724-9758 - unicode_rfind
            // Return the highest index where substring sub is found
            strType.TypeDict["rfind"] = new PyMethodDescriptor(
                "rfind", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"rfind() takes from 1 to 3 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "rfind");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    ParseStrStartStop(args, str.Value.Length, out int start, out int end);

                    // CPython 3.12: Objects/unicodeobject.c:13500-13550 - unicode_rfind
                    // Normalize negative indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);

                    // Clamp to valid range
                    start = Math.Max(0, Math.Min(start, str.Value.Length));
                    end = Math.Max(0, Math.Min(end, str.Value.Length));

                    if (start >= end) return new PyInt(-1);

                    // C# LastIndexOf: searches backwards from startIndex for count characters
                    // Python rfind: searches in range [start, end)
                    // So we search backwards from (end - 1) for (end - start) characters
                    if (end == 0) return new PyInt(-1);
                    int index = str.Value.LastIndexOf(sub.Value, end - 1, end - start);
                    return new PyInt(index);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:9760-9794 - unicode_rindex
            // Like rfind() but raise ValueError when substring is not found
            strType.TypeDict["rindex"] = new PyMethodDescriptor(
                "rindex", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"rindex() takes from 1 to 3 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "rindex");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    ParseStrStartStop(args, str.Value.Length, out int start, out int end);

                    // CPython 3.12: Objects/unicodeobject.c:13500-13550 - unicode_rindex
                    // Normalize negative indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);

                    // Clamp to valid range
                    start = Math.Max(0, Math.Min(start, str.Value.Length));
                    end = Math.Max(0, Math.Min(end, str.Value.Length));

                    if (start >= end)
                        throw PyValueError.Create("substring not found");

                    // C# LastIndexOf: searches backwards from startIndex for count characters
                    // Python rindex: searches in range [start, end)
                    if (end == 0)
                        throw PyValueError.Create("substring not found");
                    int index = str.Value.LastIndexOf(sub.Value, end - 1, end - start);
                    if (index == -1)
                        throw PyValueError.Create("substring not found");
                    return new PyInt(index);
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

                    return new PyInt(selfStr.Value.Length);
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
                    if (self is PyString)
                        return self;
                    // Measured CPython 3.12.15: str.__str__(CS("w")) == "w" (raw content,
                    // exact str), NOT the user __str__ result; str(CS("w")) dispatches
                    // type(x).__str__ separately.
                    if (self is PyStrSubclass sub)
                        return sub.GetStrValue();
                    var content = (self as PyClassInstance)?.GetStringStorage();
                    if (content == null)
                        throw PyTypeError.Create($"descriptor '__str__' requires a 'str' object but received a '{self.GetTypeName()}'");
                    return content;
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
                    return new PyString("'" + reprSelf.Value + "'");
                });

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
                    return recv.RichCompare(args[0], op);
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

                    return selfStr.GetItem(args[0]);
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

                    return selfStr.Contains(args[0]);
                });

            strType.TypeDict["count"] = new PyMethodDescriptor(
                "count", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"count() takes from 1 to 3 positional arguments but {args.Length} were given");
                    PyString str = AsStrReceiver(self, "count");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    // Empty subsection: count boundary positions over the *range*,
                    // so "abc".count("", 1, 2) == 2, not len+1.
                    if (sub.Value.Length == 0)
                        return new PyInt(StrEmptySpan(args, str.Value.Length));

                    ParseStrStartStop(args, str.Value.Length, out int start, out int end);

                    // CPython 3.12: Objects/unicodeobject.c:13500-13550 - unicode_count
                    // Normalize negative indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);

                    // Clamp to valid range
                    start = Math.Max(0, Math.Min(start, str.Value.Length));
                    end = Math.Max(0, Math.Min(end, str.Value.Length));

                    if (start >= end) return new PyInt(0);

                    int count = 0;
                    int pos = start;
                    while (pos <= end - sub.Value.Length)
                    {
                        int foundIndex = str.Value.IndexOf(sub.Value, pos, end - pos);
                        if (foundIndex == -1)
                            break;
                        count++;
                        pos = foundIndex + sub.Value.Length;
                    }
                    return new PyInt(count);
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

                    ParseStrStartStop(args, str.Value.Length, out int start, out int end);

                    // Normalize indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);
                    if (end > str.Value.Length) end = str.Value.Length;

                    foreach (var prefix in prefixes)
                    {
                        if (start < end && start + prefix.Length <= end &&
                            str.Value.Substring(start, prefix.Length) == prefix)
                            return PyBool.True;
                    }
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

                    ParseStrStartStop(args, str.Value.Length, out int start, out int end);

                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);
                    if (end > str.Value.Length) end = str.Value.Length;

                    foreach (var suffix in suffixes)
                    {
                        if (start < end && suffix.Length <= end - start &&
                            str.Value.Substring(end - suffix.Length, suffix.Length) == suffix)
                            return PyBool.True;
                    }
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

                    if (args.Length == 0)
                        return new PyString(str.Value.TrimStart());

                    if (args[0] is not PyString chars)
                        throw PyTypeError.Create("lstrip arg must be None or str");

                    return new PyString(str.Value.TrimStart(chars.Value.ToCharArray()));
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

                    if (args.Length == 0)
                        return new PyString(str.Value.TrimEnd());

                    if (args[0] is not PyString chars)
                        throw PyTypeError.Create("rstrip arg must be None or str");

                    return new PyString(str.Value.TrimEnd(chars.Value.ToCharArray()));
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

                    string sep = null;
                    int maxsplit = -1;
                    PyObject[] pa = ProjectKeywordSlots(args, kwargs, "sep", "maxsplit");

                    if (pa.Length >= 1 && pa[0] is not PyNone)
                    {
                        if (pa[0] is not PyString sepStr)
                            throw PyTypeError.Create("sep must be str or None");
                        sep = sepStr.Value;
                    }

                    if (pa.Length >= 2 && pa[1] is PyInt maxInt)
                        maxsplit = (int)maxInt.Value;

                    // CPython: rsplit splits from the right but returns results in left-to-right order
                    string[] parts;
                    if (sep == null)
                    {
                        // Split on whitespace - rsplit from right means keeping leftmost parts together when limited
                        parts = str.Value.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                        if (maxsplit >= 0 && parts.Length > maxsplit + 1)
                        {
                            // Keep the leftmost parts together
                            int keepCount = parts.Length - maxsplit;
                            var limited = new string[maxsplit + 1];
                            limited[0] = string.Join(" ", parts, 0, keepCount);
                            Array.Copy(parts, keepCount, limited, 1, maxsplit);
                            parts = limited;
                        }
                    }
                    else
                    {
                        // Split with separator from right
                        var allParts = str.Value.Split(new[] { sep }, System.StringSplitOptions.None);
                        if (maxsplit >= 0 && allParts.Length > maxsplit + 1)
                        {
                            // Keep the leftmost parts together
                            int keepCount = allParts.Length - maxsplit;
                            parts = new string[maxsplit + 1];
                            parts[0] = string.Join(sep, allParts, 0, keepCount);
                            Array.Copy(allParts, keepCount, parts, 1, maxsplit);
                        }
                        else
                        {
                            parts = allParts;
                        }
                    }

                    var items = new PyObject[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        items[i] = new PyString(parts[i]);

                    return new PyList(new System.Collections.Generic.List<PyObject>(items));
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
                    return new PyString(PyUnicodeCase.SwapCase(str.Value));
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

                    return new PyString(PyUnicodeCase.CaseFold(str.Value));
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

                    int width = (int)widthInt.Value;
                    char fillchar = ' ';

                    if (args.Length >= 2)
                    {
                        if (args[1] is not PyString fillStr || fillStr.Value.Length != 1)
                            throw PyTypeError.Create("center() fillchar must be a single character");
                        fillchar = fillStr.Value[0];
                    }

                    if (str.Value.Length >= width)
                        return str;

                    // CPython 3.12 Objects/stringlib/transmogrify.h:203-206:
                    // left = marg/2 + (marg & width & 1); right = marg - left.
                    int marg = width - str.Value.Length;
                    int leftPad = marg / 2 + (marg & width & 1);
                    int rightPad = marg - leftPad;

                    return new PyString(new string(fillchar, leftPad) + str.Value + new string(fillchar, rightPad));
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

                    int width = (int)widthInt.Value;
                    char fillchar = ' ';

                    if (args.Length >= 2)
                    {
                        if (args[1] is not PyString fillStr || fillStr.Value.Length != 1)
                            throw PyTypeError.Create("ljust() fillchar must be a single character");
                        fillchar = fillStr.Value[0];
                    }

                    if (str.Value.Length >= width)
                        return str;

                    return new PyString(str.Value + new string(fillchar, width - str.Value.Length));
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

                    int width = (int)widthInt.Value;
                    char fillchar = ' ';

                    if (args.Length >= 2)
                    {
                        if (args[1] is not PyString fillStr || fillStr.Value.Length != 1)
                            throw PyTypeError.Create("rjust() fillchar must be a single character");
                        fillchar = fillStr.Value[0];
                    }

                    if (str.Value.Length >= width)
                        return str;

                    return new PyString(new string(fillchar, width - str.Value.Length) + str.Value);
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

                    foreach (char c in str.Value)
                    {
                        if (!char.IsDigit(c) || c > 127)  // ASCII decimal digits only
                            return PyBool.False;
                    }
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

                    foreach (char c in str.Value)
                    {
                        if (!char.IsNumber(c))
                            return PyBool.False;
                    }
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

                    // First character must be letter or underscore
                    char first = str.Value[0];
                    if (!char.IsLetter(first) && first != '_')
                        return PyBool.False;

                    // Remaining characters must be letter, digit, or underscore
                    for (int i = 1; i < str.Value.Length; i++)
                    {
                        char c = str.Value[i];
                        if (!char.IsLetterOrDigit(c) && c != '_')
                            return PyBool.False;
                    }

                    return PyBool.True;
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

                    foreach (char c in str.Value)
                    {
                        if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                            return PyBool.False;
                    }
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

                    int index = str.Value.IndexOf(sep.Value);
                    if (index == -1)
                        return new PyTuple(new PyObject[] { str, new PyString(""), new PyString("") });

                    return new PyTuple(new PyObject[] {
                        new PyString(str.Value.Substring(0, index)),
                        sep,
                        new PyString(str.Value.Substring(index + sep.Value.Length))
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

                    int index = str.Value.LastIndexOf(sep.Value);
                    if (index == -1)
                        return new PyTuple(new PyObject[] { new PyString(""), new PyString(""), str });

                    return new PyTuple(new PyObject[] {
                        new PyString(str.Value.Substring(0, index)),
                        sep,
                        new PyString(str.Value.Substring(index + sep.Value.Length))
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

                    if (str.Value.StartsWith(prefix.Value))
                        return new PyString(str.Value.Substring(prefix.Value.Length));

                    return str;
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

                    if (str.Value.EndsWith(suffix.Value))
                        return new PyString(str.Value.Substring(0, str.Value.Length - suffix.Value.Length));

                    return str;
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

                    int tabsize = 8;
                    PyObject[] paTab = ProjectKeywordSlots(args, kwargs, "tabsize");
                    if (paTab.Length >= 1 && paTab[0] is PyInt tabInt)
                        tabsize = (int)tabInt.Value;

                    if (tabsize <= 0)
                        return new PyString(str.Value.Replace("\t", ""));

                    var result = new System.Text.StringBuilder();
                    int column = 0;

                    foreach (char c in str.Value)
                    {
                        if (c == '\t')
                        {
                            int spaces = tabsize - (column % tabsize);
                            result.Append(' ', spaces);
                            column += spaces;
                        }
                        else if (c == '\n' || c == '\r')
                        {
                            result.Append(c);
                            column = 0;
                        }
                        else
                        {
                            result.Append(c);
                            column++;
                        }
                    }

                    return new PyString(result.ToString());
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

                    return PyStringFormatter.Format(str.Value, args, kwargs);
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

                    return PyStringFormatter.Format(str.Value, null, args[0]);
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
                                table.SetItem(new PyInt(codepoint), value);
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
                            table.SetItem(new PyInt(fromChar), new PyInt(toChar));
                        }

                        // If z is provided, map those characters to None (delete)
                        if (args.Length >= 3)
                        {
                            if (args[2] is not PyString z)
                                throw PyTypeError.Create("third maketrans argument must be a string");

                            foreach (char c in z.Value)
                            {
                                table.SetItem(new PyInt(c), PyNone.Instance);
                            }
                        }
                    }

                    return table;
                },
                minArgs: 1, maxArgs: 3
            );
            // Wrap in classmethod descriptor
            strType.TypeDict["maketrans"] = new PyClassMethodDescriptor("maketrans", strType, maketransMethod);

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
                        var key = new PyInt(c);
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

                    return new PyString(result.ToString());
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

                    return new PyString(FormatString(strObj.Value, specStr.Value));
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:14711-14730 (unicode_new_impl)
            // str.__new__(cls, value='', encoding=None, errors=None)
            strType.TypeDict["__new__"] = new PyStaticBuiltinMethod(
                "__new__",
                (args, kwargs) =>
                {
                    // args[0] is cls
                    if (args.Length < 1)
                        throw PyTypeError.Create("str.__new__(): not enough arguments");

                    PyType cls = args[0] as PyType;
                    if (cls == null && args[0] is PyClass pyClass)
                        cls = pyClass.GetPyType();
                    if (cls == null)
                        throw PyTypeError.Create("str.__new__(X): X is not a type object");

                    // CPython unicode_new takes (object='', encoding='utf-8', errors='strict'), and
                    // the encoding/errors pair changes the meaning of the call completely: bytes get
                    // decoded, a str is refused ("decoding str is not supported"), and a non
                    // bytes-like object is refused ("decoding to str: need a bytes-like object, int
                    // found"). None of that was here - the arguments were dropped and everything went
                    // through str(), so H(b"ab", "utf8") produced "b'ab'" instead of 'ab'.
                    PyStringNewArguments.Project(args, kwargs, true,
                        out PyObject objectArg, out PyObject encodingArg, out PyObject errorsArg);
                    string value = PyStringNewArguments.Build(objectArg, encodingArg, errorsArg);

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
                        // Regular str type, return PyString
                        return new PyString(value);
                    }
                }
            );
        }

        #region Core Properties

        public string Value { get; }

        public PyString(string value) => Value = value ?? "";

        public override PyType GetPyType() => PyType.StrType;
        public override string GetTypeName() => "str";

        // CPython 3.12: Objects/unicodeobject.c - unicode_bool
        // Optimized: Direct length check, no MRO traversal
        public override bool IsTrue() => Value.Length > 0;

        #endregion

        #region String Representation

        // Python str() - PyString은 이미 문자열이므로 자신을 반환
        public override PyString AsString() => this;

        public override PyString ToRepr()
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
            return new PyString(result.ToString());
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

        public override int ToHash() => Value.GetHashCode();

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(Value == otherStr.Value),
                // CPython unicode_richcompare: content comparison also for str subclasses.
                PyStrSubclass otherSub => PyBool.FromBool(Value == otherSub.GetStringValue()),
                _ => PyNotImplemented.Instance
            };
        }

        #endregion

        #region Comparison Operations

        /// <summary>
        /// The single place Python text ordering is decided. Everything that compares str values
        /// (str, str subclasses, and anything that sorts them) must go through here.
        ///
        /// It is UTF-16 ordinal today, which is what SharpPy has always done. That is NOT
        /// CPython's ordering: code-unit order puts astral characters between U+D7FF and U+E000,
        /// so sorted([chr(0x10FFFF), chr(0xE000), chr(0xFFFF), chr(0x41)]) disagrees with
        /// CPython 3.12 and min()/max() pick the wrong element. Registered as P0-34; flipping this
        /// one function to code-point lexicographic order is the fix, and it needs the
        /// Value consumers moved off raw unit arithmetic first (see
        /// docs/reports/2026-10-30/p034_value_consumer_inventory.md).
        /// </summary>
        internal static int CompareText(string left, string right) =>
            string.CompareOrdinal(left, right);

        /// <summary>
        /// The value of the character at a Python index, for ord(). Returns a UTF-16 code unit
        /// today; PEP 393 style code-point indexing (P0-34) changes this without moving its
        /// callers. ord() on astral text is wrong until then, which is P0-34, not a hidden bug.
        /// </summary>
        internal int CodePointAt(int index) => Value[index];

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
                otherStr = (other as PyStrSubclass)?.GetStrValue()
                    ?? (other as PyClassInstance)?.GetStringStorage();
                if (otherStr == null)
                    return PyNotImplemented.Instance;
            }

            return new PyString(Value + otherStr.Value);
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
                items.Add(new PyString(keepends ? s.Substring(start, i + brkLen - start) : s.Substring(start, i - start)));
                i += brkLen - 1;
                start = i + 1;
            }
            if (start < s.Length)
                items.Add(new PyString(s.Substring(start)));
            return items;
        }

        // Receiver resolution for str descriptors: exact str or the content storage of a
        // str subclass instance (CPython looks through the subclass via the unicode type).
        /// <summary>
        /// CPython str find/index/rfind/rindex/count/startswith/endswith parse start/end
        /// via PyNumber_Index: bool and __index__ are accepted, None keeps the default,
        /// and any other type raises with this exact wording.
        /// </summary>
        // CPython: count("") / count("", start, end) counts boundaries -> end-start+1.
        private static int StrEmptySpan(PyObject[] args, int length)
        {
            ParseStrStartStop(args, length, out int start, out int end);
            if (start < 0) start += length;
            if (end < 0) end += length;
            start = Math.Max(0, Math.Min(start, length));
            end = Math.Max(0, Math.Min(end, length));
            // CPython counts zero-width match positions inside the clamped [start, end] range, so a
            // reversed range yields 0: measured "abc".count("", 3, 1) == 0.
            return Math.Max(0, end - start + 1);
        }

        private static void ParseStrStartStop(PyObject[] args, int length, out int start, out int end)
        {
            start = 0;
            end = length;
            if (args.Length >= 2 && args[1] != PyNone.Instance)
            {
                var si = PyObject.TryGetIndex(args[1]);
                if (si == null)
                    throw PyTypeError.Create("slice indices must be integers or None or have an __index__ method");
                start = (int)si.Value;
            }
            if (args.Length >= 3 && args[2] != PyNone.Instance)
            {
                var si = PyObject.TryGetIndex(args[2]);
                if (si == null)
                    throw PyTypeError.Create("slice indices must be integers or None or have an __index__ method");
                end = (int)si.Value;
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
            return self as PyString
                ?? (self as PyStrSubclass)?.GetStrValue()
                ?? (self as PyClassInstance)?.GetStringStorage();
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
        /// Note: __rmul__ is handled by VM's reverse operation dispatch (TryReverseBinaryOp)
        /// </summary>
        public override PyObject Multiply(PyObject other)
        {
            if (other is not PyInt count)
                return PyNotImplemented.Instance;

            if (count.Value <= 0)
                return new PyString("");

            // Performance: Eliminated LINQ (Enumerable.Repeat) - manual string repetition
            var sb = new StringBuilder(Value.Length * (int)count.Value);
            for (int i = 0; i < count.Value; i++)
            {
                sb.Append(Value);
            }
            return new PyString(sb.ToString());
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
            return new PyString(FormatPercent(Value, other));
        }

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

            int NextIntStar()
            {
                var v = NextPositional();
                if (v is PyInt pi)
                    return (int)pi.Value;
                if (v is PyBool pb)
                    return pb.Value ? 1 : 0;
                throw PyTypeError.Create("* wants integer");
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
                        value = arg.GetItem(new PyString(key));
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

                int width = -1;
                if (i < fmt.Length && fmt[i] == '*')
                {
                    i++;
                    width = NextIntStar();
                }
                else
                {
                    int ws = i;
                    while (i < fmt.Length && char.IsDigit(fmt[i]))
                        i++;
                    if (i > ws)
                        width = int.Parse(fmt.Substring(ws, i - ws), CultureInfo.InvariantCulture);
                }

                int precision = 6;
                bool hasPrecision = false;
                if (i < fmt.Length && fmt[i] == '.')
                {
                    hasPrecision = true;
                    i++;
                    if (i < fmt.Length && fmt[i] == '*')
                    {
                        i++;
                        precision = NextIntStar();
                    }
                    else
                    {
                        int ps = i;
                        while (i < fmt.Length && char.IsDigit(fmt[i]))
                            i++;
                        precision = i > ps ? int.Parse(fmt.Substring(ps, i - ps), CultureInfo.InvariantCulture) : 0;
                    }
                }

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
                        body = value is PyString pstr ? pstr.Value : value.ToStr().Value;
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
                    {
                        numeric = true;
                        BigInteger ival;
                        // CPython tests PyLong_Check / PyFloat_Check here, and both accept
                        // subclasses, so the exact CLR type tests are replaced by the central
                        // PyInt.TryLongSelf / PyFloat.TryFloatSelf helpers. Measured: "%d" % IO(5)
                        // is '5' where IO is a user int subclass; SharpPy raised instead.
                        if (value is PyBool bb) ival = bb.Value ? BigInteger.One : BigInteger.Zero;
                        else if (PyInt.TryLongSelf(value, out var intSelf)) ival = intSelf.Value;
                        else if (PyFloat.TryFloatSelf(value, out var floatSelf)) ival = (BigInteger)Math.Truncate(floatSelf.Value);
                        else throw PyTypeError.Create($"%{conv} format: a real number is required, not {value.GetTypeName()}");
                        if (ival.Sign < 0) { sign = "-"; ival = -ival; }
                        else if (signPlus) sign = "+";
                        else if (signSpace) sign = " ";
                        body = ival.ToString(CultureInfo.InvariantCulture);
                        break;
                    }
                    case 'o':
                    case 'x':
                    case 'X':
                    {
                        numeric = true;
                        BigInteger ival;
                        // Same rule as %d: a subclass of int is still an int (CPython
                        // PyLong_Check), a float is not.
                        if (value is PyBool bb) ival = bb.Value ? BigInteger.One : BigInteger.Zero;
                        else if (PyInt.TryLongSelf(value, out var intSelf)) ival = intSelf.Value;
                        else throw PyTypeError.Create($"%{conv} format: an integer is required, not {value.GetTypeName()}");
                        if (ival.Sign < 0) { sign = "-"; ival = -ival; }
                        else if (signPlus) sign = "+";
                        else if (signSpace) sign = " ";
                        string digits = conv == 'o' ? ToBase(ival, 8, false)
                            : ToBase(ival, 16, conv == 'X');
                        string prefix = alt && !ival.IsZero
                            ? (conv == 'o' ? "0o" : conv == 'X' ? "0X" : "0x")
                            : "";
                        body = prefix + digits;
                        break;
                    }
                    case 'c':
                    {
                        if (value is PyInt ci && ci.Value >= 0 && ci.Value <= 0x10FFFF)
                            body = char.ConvertFromUtf32((int)ci.Value);
                        else if (value is PyString cs && cs.Value.Length == 1)
                            body = cs.Value;
                        else
                            throw PyTypeError.Create("%c requires int or char");
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
                        double dv;
                        // Same rule as %d: PyFloat_Check / PyLong_Check accept subclasses, so
                        // "%.3f" % FSub(2.5) formats the payload as '2.500' (measured).
                        if (value is PyBool bv) dv = bv.Value ? 1.0 : 0.0;
                        else if (PyFloat.TryFloatSelf(value, out var floatSelf)) dv = floatSelf.Value;
                        else if (PyInt.TryLongSelf(value, out var intSelf)) dv = (double)intSelf.Value;
                        else throw PyTypeError.Create($"%{conv} format: a real number is required, not {value.GetTypeName()}");
                        body = conv switch
                        {
                            'f' or 'F' => dv.ToString((conv == 'F' ? "F" : "f") + precision, CultureInfo.InvariantCulture),
                            'e' => FormatExponent(dv, precision, false),
                            'E' => FormatExponent(dv, precision, true),
                            'g' => dv.ToString("g" + precision, CultureInfo.InvariantCulture),
                            _ => dv.ToString("G" + precision, CultureInfo.InvariantCulture),
                        };
                        // CPython float() text for nan/inf
                        if (double.IsNaN(dv)) body = "nan";
                        else if (double.IsPositiveInfinity(dv)) body = "inf";
                        else if (double.IsNegativeInfinity(dv)) { dv = dv; body = "-inf"; }
                        if (body.StartsWith('-')) { sign = "-"; body = body.Substring(1); }
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
                    // 0-padding after sign, before digits (digits already contain alt prefix)
                    if (sign.Length > 0 && (core.StartsWith("0x") || core.StartsWith("0X") || core.StartsWith("0")))
                    {
                        int p = core.StartsWith("0") ? 1 : 0;
                        sb.Append(sign).Append(core.Substring(0, p)).Append('0', pad).Append(core.Substring(p));
                    }
                    else
                        sb.Append(head).Append(' ', Math.Max(0, width - head.Length));
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

            if (positional && ai < args.Length)
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

        private static string AsciiEscape(string s)
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
        /// Format exponent notation to match Python format (e.g., "1.23e+04" not "1.23e+004")
        /// CPython: Objects/floatobject.c:format_float_short (uses at least 2 digits for exponent, not 3)
        /// </summary>
        private static string FormatExponent(double value, int precision, bool uppercase)
        {
            // C# ToString("e") uses 3 digits for exponent (e.g., "e+004")
            // Python uses minimum 2 digits (e.g., "e+04")
            string formatted = value.ToString(uppercase ? $"E{precision}" : $"e{precision}", CultureInfo.InvariantCulture);

            // Fix exponent format: replace e+004 with e+04, e-004 with e-04, etc.
            // Pattern: e+/-004 → e+/-04
            var regex = new System.Text.RegularExpressions.Regex(uppercase ? @"E([+-])0(\d{2})" : @"e([+-])0(\d{2})");
            return regex.Replace(formatted, uppercase ? "E$1$2" : "e$1$2");
        }

        /// <summary>
        /// 인덱스 접근 str[i]
        /// </summary>
        public PyString GetItem(int index)
        {
            // Python식 음수 인덱스 지원
            if (index < 0) index += Value.Length;
            
            if (index < 0 || index >= Value.Length)
                throw PyIndexError.Create("string index out of range");
            
            return new PyString(Value[index].ToString());
        }
        
        /// <summary>
        /// PyObject.GetItem 오버라이드 - 인덱싱 및 슬라이싱 지원
        /// </summary>
        public override PyObject GetItem(PyObject index)
        {
            if (index is PyInt pyInt)
            {
                return GetItem((int)pyInt.Value);
            }
            else if (index is PySlice slice)
            {
                // 슬라이싱 처리
                var (start, stop, step) = slice.StorageIndices(Value.Length);
                
                int count = PySlice.GetLength(start, stop, step);
                var chars = new char[count];
                for (int n = 0, position = start; n < count; n++, position = unchecked(position + step))
                    chars[n] = Value[position];
                return new PyString(new string(chars));
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
            
            var len = Value.Length;
            var actualStart = start ?? (step > 0 ? 0 : len - 1);
            var actualEnd = end ?? (step > 0 ? len : -1);
            
            // 음수 인덱스 정규화
            if (actualStart < 0) actualStart += len;
            if (actualEnd < 0) actualEnd += len;
            
            var result = new StringBuilder();
            if (step > 0)
            {
                for (int i = Math.Max(0, actualStart); i < Math.Min(len, actualEnd); i += step)
                {
                    result.Append(Value[i]);
                }
            }
            else
            {
                for (int i = Math.Min(len - 1, actualStart); i > Math.Max(-1, actualEnd); i += step)
                {
                    result.Append(Value[i]);
                }
            }
            
            return new PyString(result.ToString());
        }

        #endregion

        #region String Methods

        public PyString Strip(string chars = null)
        {
            if (chars == null)
                return new PyString(Value.Trim());
            return new PyString(Value.Trim(chars.ToCharArray()));
        }
        
        public PyString LStrip(string chars = null)
        {
            if (chars == null)
                return new PyString(Value.TrimStart());
            return new PyString(Value.TrimStart(chars.ToCharArray()));
        }
        
        public PyString RStrip(string chars = null)
        {
            if (chars == null)
                return new PyString(Value.TrimEnd());
            return new PyString(Value.TrimEnd(chars.ToCharArray()));
        }

        public PyString Replace(string old, string newStr, int count = -1)
        {
            if (count == -1)
                return new PyString(Value.Replace(old, newStr));
            
            // 제한된 횟수만 바꾸기
            var result = Value;
            for (int i = 0; i < count; i++)
            {
                var index = result.IndexOf(old);
                if (index == -1) break;
                result = result.Substring(0, index) + newStr + result.Substring(index + old.Length);
            }
            return new PyString(result);
        }

        public PyInt Find(string sub, int start = 0, int? end = null)
        {
            var actualEnd = end ?? Value.Length;
            if (start < 0) start = 0;
            if (actualEnd > Value.Length) actualEnd = Value.Length;

            // CPython 3.12: Objects/unicodeobject.c:13500-13550 - unicode_find
            // If start >= end, return -1 (empty string search)
            if (start >= actualEnd) return new PyInt(-1);

            var searchIn = start == 0 && actualEnd == Value.Length
                ? Value
                : Value.Substring(start, actualEnd - start);

            var index = searchIn.IndexOf(sub);
            return new PyInt(index == -1 ? -1 : index + start);
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
            if (string.IsNullOrEmpty(sub)) return new PyInt(Value.Length + 1);
            
            int count = 0;
            int index = 0;
            while ((index = Value.IndexOf(sub, index)) != -1)
            {
                count++;
                index += sub.Length;
            }
            return new PyInt(count);
        }

        public PyBool StartsWith(string prefix) => PyBool.FromBool(Value.StartsWith(prefix));
        public PyBool EndsWith(string suffix) => PyBool.FromBool(Value.EndsWith(suffix));

        public PyList Split(string sep = null, int maxsplit = -1)
        {
            string[] parts;

            if (sep == null)
            {
                // 공백문자로 분리
                var options = StringSplitOptions.RemoveEmptyEntries;
                parts = maxsplit == -1
                    ? Value.Split(new char[0], options)
                    : Value.Split(new char[0], maxsplit + 1, options);
            }
            else
            {
                parts = maxsplit == -1
                    ? Value.Split(new[] { sep }, StringSplitOptions.None)
                    : Value.Split(new[] { sep }, maxsplit + 1, StringSplitOptions.None);
            }

            // Performance: Eliminated LINQ (.Select + .Cast + .ToArray) - direct array creation + Cache
            var pyStrings = new PyObject[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                pyStrings[i] = StringCache.GetOrCreate(parts[i]);
            }
            return ListCache.Create(pyStrings);
        }

        public PyString Join(PyObject iterable)
        {
            PyObject[] values;
            if (iterable.GetType() == typeof(PyList)) values = ((PyList)iterable).Items;
            else if (iterable.GetType() == typeof(PyTuple)) values = ((PyTuple)iterable).Items;
            else
            {
                PyObject iterator;
                try { iterator = iterable.GetIterator(); }
                catch (PythonException error) when (error.PyException is PyTypeError)
                { throw PyTypeError.Create("can only join an iterable"); }
                // PySequence_Fast consumes the iterable before checking element
                // types. A later iterator exception wins over an earlier non-str.
                var cursor = iterator.GetIterator();
                long hint = iterator.LengthHint(8);
                if (hint > Array.MaxLength) throw PyMemoryError.Create();
                var collected = new List<PyObject>((int)hint);
                while (true)
                {
                    try { collected.Add(cursor.Next()); }
                    catch (PythonException error) when (error.PyException is PyStopIteration) { break; }
                }
                values = collected.ToArray();
            }
            if (values.Length == 0) return StringCache.GetOrCreate("");
            if (values.Length == 1 && values[0].GetType() == typeof(PyString)) return (PyString)values[0];
            var strings = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is not PyString item)
                    throw PyTypeError.Create($"sequence item {i}: expected str instance, {values[i].GetTypeName()} found");
                strings[i] = item.Value;
            }
            return new PyString(string.Join(Value, strings));
        }

        #endregion

        #region Type Testing Methods

        // Performance: Eliminated LINQ (.All, .Any) - manual character checks
        public PyBool IsDigit()
        {
            if (Value.Length == 0) return PyBool.False;
            for (int i = 0; i < Value.Length; i++)
            {
                if (!char.IsDigit(Value[i]))
                    return PyBool.False;
            }
            return PyBool.True;
        }

        public PyBool IsAlpha()
        {
            if (Value.Length == 0) return PyBool.False;
            for (int i = 0; i < Value.Length; i++)
            {
                if (!char.IsLetter(Value[i]))
                    return PyBool.False;
            }
            return PyBool.True;
        }

        public PyBool IsAlnum()
        {
            if (Value.Length == 0) return PyBool.False;
            for (int i = 0; i < Value.Length; i++)
            {
                if (!char.IsLetterOrDigit(Value[i]))
                    return PyBool.False;
            }
            return PyBool.True;
        }

        public PyBool IsSpace()
        {
            if (Value.Length == 0) return PyBool.False;
            for (int i = 0; i < Value.Length; i++)
            {
                if (!char.IsWhiteSpace(Value[i]))
                    return PyBool.False;
            }
            return PyBool.True;
        }

        #endregion

        #region Length and Contains

        public override int Length() => Value.Length;
        
        public PyBool Contains(string substring) => PyBool.FromBool(Value.Contains(substring));
        
        /// <summary>
        /// CPython __contains__ 메소드 구현 - PyObject 버전
        /// </summary>
        // CPython 3.12: Objects/unicodeobject.c unicode_contains (sq_contains): substring
        // test where a non-str key raises TypeError. Converting the key via ToStr() was
        // wrong: `9 in "abc"` answered False where CPython raises
        // "'in <string>' requires string as left operand, not int".
        public override PyBool Contains(PyObject item)
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
        public override bool PyBoolValue() => Value.Length > 0;
        
        /// <summary>
        /// CPython PyUnicode_AsLong 호환: PyString에서 C# int 값 추출
        /// </summary>
        public override int ToInt()
        {
            if (int.TryParse(Value.Trim(), out int result))
                return result;
            throw PyValueError.Create($"invalid literal for int() with base 10: '{Value}'");
        }
        
        /// <summary>
        /// CPython PyUnicode_AsDouble 호환: PyString에서 C# double 값 추출
        /// </summary>
        // CPython 3.12: Objects/floatobject.c:165-202 (float_from_string_inner)
        // CPython 3.12: Python/pystrtod.c:27-57 (_Py_parse_inf_or_nan)
        public override double ToFloat()
        {
            var trimmed = Value.Trim();

            // CPython 3.12: Python/pystrtod.c:27-57 - _Py_parse_inf_or_nan
            // Parse special values: inf, -inf, infinity, -infinity, nan, -nan
            if (trimmed.Length > 0)
            {
                bool negate = false;
                string s = trimmed;

                // Handle leading sign
                if (s[0] == '-')
                {
                    negate = true;
                    s = s.Substring(1).TrimStart();
                }
                else if (s[0] == '+')
                {
                    s = s.Substring(1).TrimStart();
                }

                // Check for "inf" or "infinity" (case insensitive)
                if (s.Length >= 3 && s.Substring(0, 3).ToLowerInvariant() == "inf")
                {
                    // Check if it's "infinity"
                    if (s.Length >= 8 && s.Substring(0, 8).ToLowerInvariant() == "infinity")
                    {
                        return negate ? double.NegativeInfinity : double.PositiveInfinity;
                    }
                    // Just "inf"
                    else if (s.Length == 3 || !char.IsLetterOrDigit(s[3]))
                    {
                        return negate ? double.NegativeInfinity : double.PositiveInfinity;
                    }
                }
                // Check for "nan" (case insensitive)
                else if (s.Length >= 3 && s.Substring(0, 3).ToLowerInvariant() == "nan")
                {
                    if (s.Length == 3 || !char.IsLetterOrDigit(s[3]))
                    {
                        return double.NaN;
                    }
                }
            }

            // Standard numeric parsing (shared with the buffer forms: PEP 515
            // underscores and culture-independent digits follow CPython, not CLR).
            if (PyFloat.TryParsePythonText(trimmed, out double result))
                return result;

            throw PyValueError.Create($"could not convert string to float: '{Value}'");
        }
        
        // === As* Methods: Type Conversion (PyString → PyObject types) ===
        
        /// <summary>
        /// C# 네이티브 타입 변환: PyString → C# string
        /// </summary>
        public override string ToClrString() => Value;
        
        /// <summary>
        /// CPython 호환: PyString을 PyInt로 변환
        /// </summary>
        public override PyInt AsInt() => PyInt.FromString(Value);
        
        /// <summary>
        /// CPython 호환: PyString을 PyFloat로 변환
        /// </summary>
        public override PyFloat AsFloat()
        {
            return new PyFloat(ToFloat());
        }
        
        /// <summary>
        /// CPython 호환: PyString을 PyBool로 변환
        /// </summary>
        public override PyBool AsBool()
        {
            return PyBool.FromBool(Value.Length > 0);
        }
        
        /// <summary>
        /// CPython 호환: PyString을 PyList로 변환 (각 문자를 PyString 요소로)
        /// </summary>
        public override PyList AsList()
        {
            // Performance: Eliminated LINQ (.Select + .ToList) - manual character conversion + Cache
            var items = new PyObject[Value.Length];
            for (int i = 0; i < Value.Length; i++)
            {
                items[i] = StringCache.GetOrCreate(Value[i].ToString());
            }
            return ListCache.Create(items);
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
                    return new PyBytes(EncodeCodePoints(ctx, bigEndian: false));
                case "utf-16":
                    return new PyBytes(WithPreamble(
                        EncodeCodePoints(ctx, bigEndian: false), new byte[] { 0xFF, 0xFE }));
                case "utf-16-le":
                    return new PyBytes(EncodeCodePoints(ctx, bigEndian: false));
                case "utf-16-be":
                    return new PyBytes(EncodeCodePoints(ctx, bigEndian: true));
                case "utf-32":
                    return new PyBytes(WithPreamble(
                        EncodeCodePoints(ctx, bigEndian: false), new byte[] { 0xFF, 0xFE, 0x00, 0x00 }));
                case "utf-32-le":
                    return new PyBytes(EncodeCodePoints(ctx, bigEndian: false));
                case "utf-32-be":
                    return new PyBytes(EncodeCodePoints(ctx, bigEndian: true));
                case "ascii":
                case "latin-1":
                    return new PyBytes(EncodeCodePoints(ctx, bigEndian: false));
                default:
                    // CPython's LookupError names the encoding the caller asked for. Codecs beyond
                    // these still have no encoder here; that limit stays in docs/known_gaps.md.
                    throw PyLookupError.Create($"unknown encoding: {encoding}");
            }
        }

        /// <summary>Everything the byte walk needs to know about the requested codec.</summary>
        private sealed class EncodeContext
        {
            internal EncodeContext(string codec, string errors)
            {
                Codec = codec;
                // codecs.lookup_error() resolves handler names case-insensitively.
                Errors = (errors ?? "strict").ToLowerInvariant();
                IsUtf = codec == "utf-8" || codec == "utf-16" || codec == "utf-16-le" ||
                        codec == "utf-16-be" || codec == "utf-32" || codec == "utf-32-le" ||
                        codec == "utf-32-be";
                Limit = codec == "ascii" ? 0x7F : codec == "latin-1" ? 0xFF : int.MaxValue;
                Width = codec == "utf-8" ? 0 : codec.StartsWith("utf-16") ? 2
                      : codec.StartsWith("utf-32") ? 4 : 1;
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
                        throw PyLookupError.Create($"unknown error handler name '{ctx.Errors}'");
                }
            }
            return bytes.ToArray();
        }

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

            string chDesc = cp <= 0xFF ? $"'\\x{cp:x2}'"
                            : cp <= 0xFFFF ? $"'\\u{cp:x4}'"
                            : $"'\\U{cp:x8}'";
            string position = start == end
                ? $"character {chDesc} in position {start}"
                : $"characters in position {start}-{end}";
            return PyUnicodeEncodeError.Create(
                $"'{ctx.Codec}' codec can't encode {position}: ordinal not in range({ctx.Limit + 1})");
        }

        private PythonException SurrogateError(EncodeContext ctx, int cpIndex, char unit) =>
            PyUnicodeEncodeError.Create(
                $"'{ctx.Codec}' codec can't encode character '\\u{(int)unit:x4}' " +
                $"in position {cpIndex}: surrogates not allowed");

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

        #endregion

        #region Constants

        public static readonly PyString Empty = new PyString("");

        #endregion

        #region String Methods

        public override PyObject GetAttribute(string name)
        {
            // CPython resolves str methods through the type: str.__dict__ holds the real
            // method_descriptor objects. Every name that used to be answered by an instance-level
            // closure here (upper, lower, title, strip, lstrip, rstrip, replace, split, join, find,
            // count, encode, isidentifier, startswith, endswith) is registered as a descriptor in
            // strType.TypeDict, so the closures only shadowed them: keyword acceptance
            // (METH_KEYWORDS), arity messages and descriptor identity were decided by a duplicate path
            // instead of by the descriptor. Deleting the shadow makes the descriptor authoritative.
            return base.GetAttribute(name);
        }

        private PyObject Strip(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"strip() takes at most 1 argument ({args.Length} given)");
            
            if (args.Length == 0)
                return new PyString(Value.Trim());
            
            var chars = args[0] switch
            {
                PyString str => str.Value.ToCharArray(),
                _ => throw PyTypeError.Create("strip arg must be None or str")
            };
            
            return new PyString(Value.Trim(chars));
        }

        private PyObject LStrip(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"lstrip() takes at most 1 argument ({args.Length} given)");
            
            if (args.Length == 0)
                return new PyString(Value.TrimStart());
            
            var chars = args[0] switch
            {
                PyString str => str.Value.ToCharArray(),
                _ => throw PyTypeError.Create("lstrip arg must be None or str")
            };
            
            return new PyString(Value.TrimStart(chars));
        }

        private PyObject RStrip(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"rstrip() takes at most 1 argument ({args.Length} given)");
            
            if (args.Length == 0)
                return new PyString(Value.TrimEnd());
            
            var chars = args[0] switch
            {
                PyString str => str.Value.ToCharArray(),
                _ => throw PyTypeError.Create("rstrip arg must be None or str")
            };
            
            return new PyString(Value.TrimEnd(chars));
        }

        private PyObject Replace(PyObject[] args)
        {
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"replace() takes 2 or 3 arguments ({args.Length} given)");
            
            var old = args[0] switch
            {
                PyString str => str.Value,
                _ => throw PyTypeError.Create("replace() old must be str")
            };
            
            var newStr = args[1] switch
            {
                PyString str => str.Value,
                _ => throw PyTypeError.Create("replace() new must be str")
            };
            
            if (args.Length == 3)
            {
                var count = args[2] switch
                {
                    PyInt i => i.Value,
                    _ => throw PyTypeError.Create("replace() count must be int")
                };
                
                var result = Value;
                for (int i = 0; i < count && result.Contains(old); i++)
                {
                    var index = result.IndexOf(old);
                    if (index == -1) break;
                    result = result.Substring(0, index) + newStr + result.Substring(index + old.Length);
                }
                return new PyString(result);
            }
            
            return new PyString(Value.Replace(old, newStr));
        }

        private PyObject Split(PyObject[] args)
        {
            if (args.Length > 2)
                throw PyTypeError.Create($"split() takes at most 2 arguments ({args.Length} given)");
            
            string sep = null;
            int maxsplit = -1;
            
            if (args.Length >= 1 && args[0] is PyString sepStr)
                sep = sepStr.Value;
            
            if (args.Length >= 2 && args[1] is PyInt maxsplitInt)
                maxsplit = (int)maxsplitInt.Value;

            return Split(sep, maxsplit);
        }

        private PyObject Join(PyObject[] args)
        {
            if (args.Length != 1) throw PyTypeError.Create($"join() takes exactly one argument ({args.Length} given)");
            return Join(args[0]);
        }

        private PyObject StartsWith(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"startswith() takes exactly one argument ({args.Length} given)");
            
            foreach (var prefix in NormalizePrefixTuple(args[0], "startswith"))
                if (Value.StartsWith(prefix))
                    return PyBool.True;
            return PyBool.False;
        }

        private PyObject EndsWith(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"endswith() takes exactly one argument ({args.Length} given)");
            
            foreach (var suffix in NormalizePrefixTuple(args[0], "endswith"))
                if (Value.EndsWith(suffix))
                    return PyBool.True;
            return PyBool.False;
        }

        private PyObject Find(PyObject[] args)
        {
            if (args.Length < 1 || args.Length > 3)
                throw PyTypeError.Create($"find() takes 1 to 3 arguments ({args.Length} given)");

            var sub = args[0] switch
            {
                PyString str => str.Value,
                _ => throw PyTypeError.Create("find() sub must be str")
            };

            int start = 0;
            int end = Value.Length;

            // Handle negative indices (Python convention: -1 means last character)
            ParseStrStartStop(args, Value.Length, out start, out end);
            if (start < 0) start += Value.Length;
            start = Math.Max(0, Math.Min(Value.Length, start));
            if (end < 0) end += Value.Length;
            end = Math.Max(0, Math.Min(Value.Length, end));

            if (start >= end) return new PyInt(-1);
            
            var substring = start == 0 && end == Value.Length 
                ? Value 
                : Value.Substring(start, end - start);
                
            var index = substring.IndexOf(sub);
            return new PyInt(index == -1 ? -1 : index + start);
        }

        private PyObject Count(PyObject[] args)
        {
            if (args.Length < 1 || args.Length > 3)
                throw PyTypeError.Create($"count() takes 1 to 3 arguments ({args.Length} given)");

            var sub = args[0] switch
            {
                PyString str => str.Value,
                _ => throw PyTypeError.Create("count() sub must be str")
            };

            int start = 0;
            int end = Value.Length;

            // Handle negative indices (Python convention: -1 means last character)
            ParseStrStartStop(args, Value.Length, out start, out end);
            if (start < 0) start += Value.Length;
            start = Math.Max(0, Math.Min(Value.Length, start));
            if (end < 0) end += Value.Length;
            end = Math.Max(0, Math.Min(Value.Length, end));

            // CPython unicode_count: the empty subsection counts *boundary positions*,
            // i.e. end - start + 1, not 0.
            if (sub.Length == 0) return new PyInt(Math.Max(0, end - start + 1));
            if (start >= end) return new PyInt(0);
            
            var substring = start == 0 && end == Value.Length 
                ? Value 
                : Value.Substring(start, end - start);
                
            return CountOccurrences(substring, sub);
        }

        private PyObject CountOccurrences(string text, string sub)
        {
            if (string.IsNullOrEmpty(sub)) return new PyInt(0);
            
            int count = 0;
            int index = 0;
            
            while ((index = text.IndexOf(sub, index)) != -1)
            {
                count++;
                index += sub.Length;
            }
            
            return new PyInt(count);
        }

        private PyObject EncodeMethod(PyObject[] args)
        {
            if (args.Length > 2)
                throw PyTypeError.Create($"encode() takes at most 2 arguments ({args.Length} given)");

            string encoding = "utf-8";
            if (args.Length >= 1)
            {
                if (args[0] is PyString encodingStr)
                    encoding = encodingStr.Value;
                else
                    throw PyTypeError.Create("encode() encoding must be str");
            }

            // Second argument (errors) is ignored for now
            return Encode(encoding);
        }

        private PyObject IsIdentifier(PyObject[] args)
        {
            // CPython 3.12: str.isidentifier()
            // Returns True if the string is a valid identifier according to Python language definition
            // Note: This returns True for keywords too (e.g., "if", "class")
            if (args.Length != 0)
                throw PyTypeError.Create($"str.isidentifier() takes no arguments ({args.Length} given)");

            if (string.IsNullOrEmpty(Value))
                return PyBool.False;

            // Python identifier rules:
            // 1. First character must be letter (a-z, A-Z) or underscore (_)
            // 2. Subsequent characters can be letters, digits, or underscores
            // 3. Cannot be empty

            char first = Value[0];
            if (!char.IsLetter(first) && first != '_')
                return PyBool.False;

            for (int i = 1; i < Value.Length; i++)
            {
                char c = Value[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return PyBool.False;
            }

            return PyBool.True;
        }

        #endregion

        #region Iterator Support

        /// <summary>
        /// Iterator support for string iteration (for char in "string")
        /// </summary>
        public override PyObject GetIterator()
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

            // Parse format spec
            char fill = ' ';
            char align = '<';  // Default alignment for strings is left
            int width = 0;
            int precision = -1;  // -1 means no limit
            char type = 's';    // default type for strings

            int i = 0;
            int len = formatSpec.Length;

            // Check for fill + align (fill is any char, align is one of <>^)
            if (len >= 2 && "<>^".Contains(formatSpec[1]))
            {
                fill = formatSpec[0];
                align = formatSpec[1];
                i = 2;
            }
            else if (len >= 1 && "<>^".Contains(formatSpec[0]))
            {
                align = formatSpec[0];
                i = 1;
            }

            // Width
            while (i < len && char.IsDigit(formatSpec[i]))
            {
                width = width * 10 + (formatSpec[i] - '0');
                i++;
            }

            // Precision
            if (i < len && formatSpec[i] == '.')
            {
                i++;
                precision = 0;
                while (i < len && char.IsDigit(formatSpec[i]))
                {
                    precision = precision * 10 + (formatSpec[i] - '0');
                    i++;
                }
            }

            // Type
            if (i < len)
            {
                type = formatSpec[i];
                i++;
            }

            // Only 's' or empty type is valid for strings
            if (type != 's' && type != '\0')
                throw PyValueError.Create($"Unknown format code '{type}' for object of type 'str'");

            // Apply precision (truncation)
            string result = value;
            if (precision >= 0 && result.Length > precision)
                result = result.Substring(0, precision);

            // Apply width and alignment
            if (width > result.Length)
            {
                int padLen = width - result.Length;
                switch (align)
                {
                    case '<':  // left-aligned
                        result = result + new string(fill, padLen);
                        break;
                    case '>':  // right-aligned
                        result = new string(fill, padLen) + result;
                        break;
                    case '^':  // centered
                        int leftPad = padLen / 2;
                        int rightPad = padLen - leftPad;
                        result = new string(fill, leftPad) + result + new string(fill, rightPad);
                        break;
                }
            }

            return result;
        }

        #endregion
    }
}
