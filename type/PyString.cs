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
            // unicode_new's clinic: str(object=<unrepresentable>, encoding=<unrepresentable>, errors=<unrepresentable>).
            var given = hasReceiver ? args[System.Math.Min(1, args.Length)..] : args;
            var parsed = PyArgs.Unpack("str", given, kwargs, new[] { "object", "encoding", "errors" }, 0, 0, 3);
            objectArg = parsed[0];
            encodingArg = parsed[1];
            errorsArg = parsed[2];
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

            // unicode_new's clinic converts encoding and errors first; then, with either present,
            // PyUnicode_FromEncodedObject never accepts a str and never calls str().
            string encodingName = PyArgs.ClinicStr("str", "argument 'encoding'", "str", encodingArg) ?? "utf-8";
            string errorName = PyArgs.ClinicStr("str", "argument 'errors'", "str", errorsArg);
            if (objectArg is PyString)
                throw PyTypeError.Create("decoding str is not supported");

            byte[] data = RawBytesOrNull(objectArg);
            if (data == null)
                throw PyTypeError.Create(
                    $"decoding to str: need a bytes-like object, {objectArg.GetTypeName()} found");

            return DecodeBytes(data, encodingName, errorName);
        }

        /// <summary>The codecs PyUnicode_Decode and PyUnicode_AsEncodedString take without the registry.</summary>
        internal enum Shortcut { None, Utf8, Utf16, Utf32, Ascii, Mbcs, Latin1 }

        /// <summary>
        /// The "Shortcuts for common default encodings" of PyUnicode_Decode and
        /// PyUnicode_AsEncodedString: _Py_normalize_encoding into char buflower[11] (a longer name
        /// fails and takes the registry), then utf8/utf_8, utf16, utf32, ascii/us_ascii, mbcs on
        /// Windows, and latin1/latin_1/iso_8859_1/iso8859_1.
        /// </summary>
        internal static Shortcut ShortcutOf(string encoding)
        {
            const int lowerLen = 11;
            var lower = new System.Text.StringBuilder(lowerLen);
            bool punct = false;
            foreach (char c in encoding)
            {
                if ((c < 0x80 && char.IsAsciiLetterOrDigit(c)) || c == '.')
                {
                    if (punct && lower.Length != 0)
                    {
                        if (lower.Length == lowerLen - 1)
                            return Shortcut.None;
                        lower.Append('_');
                    }
                    punct = false;
                    if (lower.Length == lowerLen - 1)
                        return Shortcut.None;
                    lower.Append(char.ToLowerInvariant(c));
                }
                else
                    punct = true;
            }
            string name = lower.ToString();
            if (name.StartsWith("utf", System.StringComparison.Ordinal))
            {
                string rest = name.Substring(3);
                // Match "utf8" and "utf_8"
                if (rest.StartsWith("_", System.StringComparison.Ordinal))
                    rest = rest.Substring(1);
                return rest switch { "8" => Shortcut.Utf8, "16" => Shortcut.Utf16, "32" => Shortcut.Utf32, _ => Shortcut.None };
            }
            return name switch
            {
                "ascii" or "us_ascii" => Shortcut.Ascii,
                "mbcs" when System.OperatingSystem.IsWindows() => Shortcut.Mbcs,
                "latin1" or "latin_1" or "iso_8859_1" or "iso8859_1" => Shortcut.Latin1,
                _ => Shortcut.None,
            };
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

        /// <summary>
        /// PyUnicode_Decode: nothing to decode is the empty string before any lookup, a null
        /// encoding is UTF-8, then the shortcuts, then _PyCodec_DecodeText over a memoryview.
        /// </summary>
        internal static PyString DecodeBytes(byte[] data, string encodingName, string errorName)
        {
            PyCodecRegistry.CheckEncodingErrors(encodingName, errorName);
            if (data.Length == 0)
                return PyString.Empty;
            if (encodingName == null)
                return UnicodeDecoders.DecodeUtf8(data, errorName, null);
            int byteorder = 0;
            switch (ShortcutOf(encodingName))
            {
                case Shortcut.Utf8: return UnicodeDecoders.DecodeUtf8(data, errorName, null);
                case Shortcut.Utf16: return UnicodeDecoders.DecodeUtf16(data, errorName, ref byteorder, null);
                case Shortcut.Utf32: return UnicodeDecoders.DecodeUtf32(data, errorName, ref byteorder, null);
                case Shortcut.Ascii: return UnicodeDecoders.DecodeAscii(data, errorName);
                case Shortcut.Mbcs: return PyCodePage.Decode(PyCodePage.CP_ACP, data, errorName, true, out _);
                case Shortcut.Latin1: return UnicodeDecoders.DecodeLatin1(data);
            }
            // Decode via the codec registry (PyMemoryView_FromBuffer)
            var unicode = PyCodecRegistry.DecodeText(new PyMemoryView(data, readOnly: true), encodingName, errorName);
            return unicode is PyString text ? text
                : throw PyTypeError.Create($"'{encodingName}' decoder returned '{unicode.GetPyType().FullName}' instead of 'str'; use codecs.decode() to decode to arbitrary types");
        }
    }

    public partial class PyString : PyObject
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
                    var str = (PyString)self;

                    return str.Join(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            // 기타 주요 메서드들도 등록
            strType.TypeDict["upper"] = new PyMethodDescriptor(
                "upper", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;
                    return PyUnicodeCase.Upper(str);
                },
                minArgs: 0, maxArgs: 0
            );

            strType.TypeDict["lower"] = new PyMethodDescriptor(
                "lower", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;
                    return PyUnicodeCase.Lower(str);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:10607-10614 - unicode_capitalize_impl
            // Return a capitalized version of the string.
            // More specifically, make the first character have upper case and the rest lower case.
            strType.TypeDict["capitalize"] = new PyMethodDescriptor(
                "capitalize", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    // unicode_capitalize_impl: the empty text is unchanged
                    if (str.CodePointLength() == 0)
                        return str.ResultUnchanged();

                    // CPython 3.12: Objects/unicodeobject.c do_capitalize - the first character goes
                    // through the TITLE mapping (not upper) and the rest through the full lower
                    // mapping, which is why "ǅ".capitalize() keeps ǅ and "ß".capitalize() is "Ss".
                    return PyUnicodeCase.Capitalize(str);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: str.title() - titlecase the string
            strType.TypeDict["title"] = new PyMethodDescriptor(
                "title", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    // CPython str.title() uses Unicode cased/uncased word boundaries, so an apostrophe
                    // keeps the next letter uppercase: "it's a test-case 2nd".title() ->
                    // "It'S A Test-Case 2Nd". System.Globalization.TextInfo.ToTitleCase follows .NET
                    // word-breaking, so the word rule lives in PyUnicodeCase.Title (do_title).
                    return PyUnicodeCase.Title(str);
                },
                minArgs: 0, maxArgs: 0
            );

            strType.TypeDict["split"] = new PyMethodDescriptor(
                "split", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    var (sep, maxsplit) = SplitArguments(ProjectKeywordSlots(args, kwargs, "sep", "maxsplit"));

                    return str.Split(sep, maxsplit);
                },
                minArgs: 0, maxArgs: 2
, acceptsKwargs: true);

            strType.TypeDict["strip"] = new PyMethodDescriptor(
                "strip", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;
                    // do_argstrip: None or nothing is whitespace, else the characters of a str.
                    return ArgStrip(str, args, BothStrip, "strip");
                },
                minArgs: 0, maxArgs: 1
            );

            strType.TypeDict["replace"] = new PyMethodDescriptor(
                "replace", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    var old = args[0] as PyString ?? throw PyArgs.BadArgument("replace", "argument 1", "str", args[0]);
                    var newStr = args[1] as PyString ?? throw PyArgs.BadArgument("replace", "argument 2", "str", args[1]);

                    // count is "n": PyLong_AsSsize_t after _PyNumber_Index.
                    long count = args.Length == 3 ? PyArgs.Ssize(args[2]) : -1;
                    return str.Replace(old, newStr, count);
                },
                minArgs: 2, maxArgs: 3
            );

            // CPython 3.12: str.zfill(width) - pad string with zeros on the left
            strType.TypeDict["zfill"] = new PyMethodDescriptor(
                "zfill", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    return str.ZFill(PyArgs.Width(args[0]));
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:11800-11817 - unicode_isdigit_impl
            // Return True if the string is a digit string, False otherwise.
            // A string is a digit string if all characters are digits.
            strType.TypeDict["isdigit"] = new PyMethodDescriptor(
                "isdigit", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    if (str.CodePointLength() == 0)
                        return PyBool.False;

                    // unicode_isdigit_impl: Py_UNICODE_ISDIGIT of each character
                    foreach (int cp in str.CodePoints())
                        if (!UnicodeType.IsDigit(cp))
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
                    var str = (PyString)self;

                    if (str.CodePointLength() == 0)
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
                    var str = (PyString)self;

                    if (str.CodePointLength() == 0)
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
                    var str = (PyString)self;

                    if (str.CodePointLength() == 0)
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
                    var str = (PyString)self;

                    if (str.CodePointLength() == 0)
                        return PyBool.False;

                    // unicode_isupper_impl: no lowercase/titlecase character, at least one uppercase.
                    return PyBool.FromBool(PyUnicodeCase.IsUpper(str));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11910-11927 - unicode_islower_impl
            // Return True if the string is a lowercase string, False otherwise.
            strType.TypeDict["islower"] = new PyMethodDescriptor(
                "islower", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    if (str.CodePointLength() == 0)
                        return PyBool.False;

                    // unicode_islower_impl: no uppercase/titlecase character, at least one lowercase.
                    return PyBool.FromBool(PyUnicodeCase.IsLower(str));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:9652-9686 - unicode_find
            // Return the lowest index where substring sub is found
            strType.TypeDict["find"] = new PyMethodDescriptor(
                "find", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;


                    return FindIndexOf(str, args[0], args, backward: false, raiseWhenMissing: false);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:9688-9722 - unicode_index
            // Like find() but raise ValueError when the substring is not found
            strType.TypeDict["index"] = new PyMethodDescriptor(
                "index", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;


                    return FindIndexOf(str, args[0], args, backward: false, raiseWhenMissing: true);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:9724-9758 - unicode_rfind
            // Return the highest index where substring sub is found
            strType.TypeDict["rfind"] = new PyMethodDescriptor(
                "rfind", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;


                    return FindIndexOf(str, args[0], args, backward: true, raiseWhenMissing: false);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:9760-9794 - unicode_rindex
            // Like rfind() but raise ValueError when substring is not found
            strType.TypeDict["rindex"] = new PyMethodDescriptor(
                "rindex", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;


                    return FindIndexOf(str, args[0], args, backward: true, raiseWhenMissing: true);
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
                    var selfStr = (PyString)self;

                    return PyInt.From(selfStr.CodePointLength());
                });

            // CPython 3.12: str.__dict__['__str__'] is a slot wrapper; unicode_tp_str
            // returns self, so subclass instances pass through unchanged (str()/print
            // then re-dispatch type(x).__str__).
            strType.TypeDict["__str__"] = new PyWrapperDescriptor(
                "__str__", strType,
                (self, args, kwargs) =>
                {
                    
                    // Measured CPython 3.12.15: str.__str__(CS("w")) == "w" (raw content,
                    // exact str), NOT the user __str__ result; str(CS("w")) dispatches
                    // type(x).__str__ separately.
                    return ((PyString)self).Str();
                });
            strType.TypeDict["__repr__"] = new PyWrapperDescriptor(
                "__repr__", strType,
                (self, args, kwargs) =>
                {
                    // the slot itself: unicode_repr of the str's characters, whatever a subclass defines
                    return ((PyString)self).UnicodeRepr();
                });

            // Objects/unicodeobject.c: unicode_getnewargs - pickle and copy reconstruct a str (or a
            // str subclass) from this tuple, so a subclass needs the value, not the wrapper.
            strType.TypeDict["__getnewargs__"] = new PyMethodDescriptor(
                "__getnewargs__", strType,
                (self, args, kwargs) => PyTuple.From(new PyObject[]
                {
                    // unicode_getnewargs: _PyUnicode_Copy(self)
                    ((PyString)self).Copy()
                }),
                minArgs: 0, maxArgs: 0);

            // CPython 3.12: str % (nb_remainder via PyUnicode_Format) slot wrapper.
            strType.TypeDict["__mod__"] = new PyWrapperDescriptor(
                "__mod__", strType,
                (self, args, kwargs) =>
                {
                    var recv = (PyString)self;
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
                    var recv = (PyString)self;
                    return recv.TpRichCompare(args[0], op);
                });
            }

            strType.TypeDict["__add__"] = new PyWrapperDescriptor(
                "__add__", strType,
                (self, args, kwargs) =>
                {
                    var selfStr = (PyString)self;

                    var concat = selfStr.Add(args[0]);
                    if (ReferenceEquals(concat, PyNotImplemented.Instance))
                        throw PyTypeError.Create($"can only concatenate str (not \"{args[0].GetTypeName()}\") to str");
                    return concat;
                });

            strType.TypeDict["__getitem__"] = new PyWrapperDescriptor(
                "__getitem__", strType,
                (self, args, kwargs) =>
                {
                    var selfStr = (PyString)self;

                    return selfStr.MpSubscript(args[0]);
                });

            strType.TypeDict["__contains__"] = new PyWrapperDescriptor(
                "__contains__", strType,
                (self, args, kwargs) =>
                {
                    var selfStr = (PyString)self;

                    // unicode_contains: the slot itself, not PyObject_Contains (a subclass would re-enter it).
                    return selfStr.ContainsItem(args[0]);
                });

            strType.TypeDict["count"] = new PyMethodDescriptor(
                "count", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    return CountOf(str, args[0], args);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:10037-10060 - unicode_startswith
            // Return True if string starts with the specified prefix
            strType.TypeDict["startswith"] = new PyMethodDescriptor(
                "startswith", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    return StartsOrEndsWith(str, args[0], args, "startswith", fromEnd: false);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:10062-10085 - unicode_endswith
            // Return True if string ends with the specified suffix
            strType.TypeDict["endswith"] = new PyMethodDescriptor(
                "endswith", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    return StartsOrEndsWith(str, args[0], args, "endswith", fromEnd: true);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:9442-9471 - unicode_lstrip
            // Return a copy with leading whitespace removed
            strType.TypeDict["lstrip"] = new PyMethodDescriptor(
                "lstrip", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;
                    // do_argstrip: None or nothing is whitespace, else the characters of a str.
                    return ArgStrip(str, args, LeftStrip, "lstrip");
                },
                minArgs: 0, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:9473-9502 - unicode_rstrip
            // Return a copy with trailing whitespace removed
            strType.TypeDict["rstrip"] = new PyMethodDescriptor(
                "rstrip", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;
                    // do_argstrip: None or nothing is whitespace, else the characters of a str.
                    return ArgStrip(str, args, RightStrip, "rstrip");
                },
                minArgs: 0, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:10200-10246 - unicode_rsplit
            // Return a list of the words in the string, using sep as delimiter, from right
            strType.TypeDict["rsplit"] = new PyMethodDescriptor(
                "rsplit", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

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
                    var str = (PyString)self;

                    // CPython unicode_splitlines is METH_FASTCALL|METH_KEYWORDS with keepends=False,
                    // so 'a\nb'.splitlines(keepends=True) -> ['a\n', 'b']. The delegate read only the
                    // positional array, so the keyword is projected into its slot and resolved with
                    // CPython truthiness instead of matching PyBool/PyInt only.
                    PyObject keependsArg = args.Length >= 1
                        ? args[0]
                        : KwValue(kwargs, "keepends");
                    bool keepends = keependsArg != null && keependsArg.ToBool();

                    // CPython unicode_splitlines: \n \r \r\n \v \f \x1c \x1d \x1e \x85 \u2028 \u2029
                    return new PyList(SplitLinesCore(str, keepends));
                },
                minArgs: 0, maxArgs: 1
, acceptsKwargs: true);

            // CPython 3.12: Objects/unicodeobject.c:10533-10550 - unicode_swapcase
            // Return a copy with uppercase characters converted to lowercase and vice versa
            strType.TypeDict["swapcase"] = new PyMethodDescriptor(
                "swapcase", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    // do_swapcase decides by the uppercase/lowercase PROPERTIES, and the lowercase
                    // branch needs the sigma context, so it is not a per-char ToLower/ToUpper loop.
                    return PyUnicodeCase.SwapCase(str);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11674-11691 - unicode_casefold
            // Return a casefolded copy (aggressive lowercase for caseless matching)
            strType.TypeDict["casefold"] = new PyMethodDescriptor(
                "casefold", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    return PyUnicodeCase.CaseFold(str);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:10402-10425 - unicode_center
            // Return a centered string of length width
            strType.TypeDict["center"] = new PyMethodDescriptor(
                "center", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    int width = PyArgs.Width(args[0]);
                    int fillchar = args.Length >= 2 ? FillCharacter(args[1]) : ' ';
                    return str.Center(width, fillchar);
                },
                minArgs: 1, maxArgs: 2
            );

            // CPython 3.12: Objects/unicodeobject.c:10427-10450 - unicode_ljust
            // Return a left-justified string of length width
            strType.TypeDict["ljust"] = new PyMethodDescriptor(
                "ljust", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    int width = PyArgs.Width(args[0]);
                    int fillchar = args.Length >= 2 ? FillCharacter(args[1]) : ' ';
                    int chars = str.CodePointLength();
                    return chars >= width ? str.ResultUnchanged() : str.Pad(0, width - chars, fillchar);
                },
                minArgs: 1, maxArgs: 2
            );

            // CPython 3.12: Objects/unicodeobject.c:10452-10475 - unicode_rjust
            // Return a right-justified string of length width
            strType.TypeDict["rjust"] = new PyMethodDescriptor(
                "rjust", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    int width = PyArgs.Width(args[0]);
                    int fillchar = args.Length >= 2 ? FillCharacter(args[1]) : ' ';
                    int chars = str.CodePointLength();
                    return chars >= width ? str.ResultUnchanged() : str.Pad(width - chars, 0, fillchar);
                },
                minArgs: 1, maxArgs: 2
            );

            // CPython 3.12: Objects/unicodeobject.c:11693-11710 - unicode_isascii
            // Return True if all characters are ASCII
            strType.TypeDict["isascii"] = new PyMethodDescriptor(
                "isascii", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    // PyUnicode_IS_ASCII
                    return PyBool.FromBool(str.MaxChar() < 128);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11819-11836 - unicode_isdecimal
            // Return True if all characters are decimal characters
            strType.TypeDict["isdecimal"] = new PyMethodDescriptor(
                "isdecimal", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    if (str.CodePointLength() == 0)
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
                    var str = (PyString)self;

                    if (str.CodePointLength() == 0)
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
                    var str = (PyString)self;

                    if (str.CodePointLength() == 0)
                        return PyBool.False;

                    return ScanIdentifier(str) ? PyBool.True : PyBool.False;
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11952-11969 - unicode_isprintable
            // Return True if all characters are printable
            strType.TypeDict["isprintable"] = new PyMethodDescriptor(
                "isprintable", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

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
                    var str = (PyString)self;

                    if (str.CodePointLength() == 0)
                        return PyBool.False;

                    return PyBool.FromBool(PyUnicodeCase.IsTitle(str));
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:10291-10314 - unicode_partition
            // Partition string at first occurrence of sep, return 3-tuple
            strType.TypeDict["partition"] = new PyMethodDescriptor(
                "partition", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    return Partition(str, args[0], fromRight: false);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:10316-10339 - unicode_rpartition
            // Partition string at last occurrence of sep, return 3-tuple
            strType.TypeDict["rpartition"] = new PyMethodDescriptor(
                "rpartition", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    return Partition(str, args[0], fromRight: true);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:12096-12117 - unicode_removeprefix
            // Remove prefix from string (Python 3.9+)
            strType.TypeDict["removeprefix"] = new PyMethodDescriptor(
                "removeprefix", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    if (args[0] is not PyString prefix)
                        throw PyArgs.BadArgument("removeprefix", "argument", "str", args[0]);

                    return str.RemoveAffix(prefix, suffix: false);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:12119-12140 - unicode_removesuffix
            // Remove suffix from string (Python 3.9+)
            strType.TypeDict["removesuffix"] = new PyMethodDescriptor(
                "removesuffix", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    if (args[0] is not PyString suffix)
                        throw PyArgs.BadArgument("removesuffix", "argument", "str", args[0]);

                    return str.RemoveAffix(suffix, suffix: true);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:10477-10500 - unicode_expandtabs
            // Return a copy where tab characters are expanded
            strType.TypeDict["expandtabs"] = new PyMethodDescriptor(
                "expandtabs", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    // unicode_expandtabs_impl: tabsize is a C int; columns count code points, a
                    // newline or carriage return starts a new one, and no tab is the text unchanged.
                    int tabsize = 8;
                    PyObject[] paTab = ProjectKeywordSlots(args, kwargs, "tabsize");
                    if (paTab.Length >= 1 && paTab[0] != null)
                        tabsize = PyArgs.Int(paTab[0]);
                    return str.ExpandTabs(tabsize);
                },
                minArgs: 0, maxArgs: 1
, acceptsKwargs: true);

            // CPython 3.12: Objects/unicodeobject.c:11246-11250 - unicode_encode_impl
            // Encode the string using the specified encoding
            strType.TypeDict["encode"] = new PyMethodDescriptor(
                "encode", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    string encoding = "utf-8";
                    string errors = "strict";

                    PyObject[] paEnc = ProjectKeywordSlots(args, kwargs, "encoding", "errors");
                    // unicode_encode's clinic: both by the str converter.
                    if (paEnc.Length >= 1 && paEnc[0] != null)
                        encoding = PyArgs.ClinicStr("encode", "argument 'encoding'", "str", paEnc[0]);
                    if (paEnc.Length >= 2 && paEnc[1] != null)
                        errors = PyArgs.ClinicStr("encode", "argument 'errors'", "str", paEnc[1]);

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
                    var str = (PyString)self;
                    var formatted = PyStringFormatter.Format(str, args, kwargs);
                    return SoleLiteral(str, str.Value.IndexOf('{') < 0 && str.Value.IndexOf('}') < 0) ?? formatted;
                }
, acceptsKwargs: true);

            // CPython 3.12: Objects/stringlib/unicode_format.h:961-964 - do_string_format_map
            // Format string using .format_map() method with mapping object
            strType.TypeDict["format_map"] = new PyMethodDescriptor(
                "format_map", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;
                    var formatted = PyStringFormatter.Format(str, null, args[0]);
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

                    return MakeTrans(args);
                },
                minArgs: 1, maxArgs: 3
            );
            // unicode_maketrans: METH_VARARGS|METH_STATIC.
            strType.TypeDict["maketrans"] = PyStaticmethod.ForBuiltin(strType, "maketrans", (args, kwargs) =>
            {
                return maketransMethod._implementation(strType, args, kwargs);
            });

            // CPython 3.12: Objects/unicodeobject.c:13010-13014 - unicode_translate
            // Translate string using translation table
            strType.TypeDict["translate"] = new PyMethodDescriptor(
                "translate", strType,
                (self, args, kwargs) => {
                    var str = (PyString)self;

                    return str.Translate(args[0]);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:14632-14670 - unicode__format___impl
            // str.__format__(format_spec) - Format the string according to format_spec
            strType.TypeDict["__format__"] = new PyMethodDescriptor(
                "__format__", strType,
                (self, args, kwargs) => {
                    var strObj = (PyString)self;
                    if (args[0] is not PyString specStr)
                        throw PyArgs.BadArgument("__format__", "argument", "str", args[0]);

                    // unicode__format__: an empty spec is str(self).
                    return specStr.CodePointLength() == 0 ? self.ToStr() : FormatString(strObj, specStr, self.GetPyType().FullName);
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
                            return new PyStrSubclass(classObj, text);
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

        public override PyType GetPyType() => _customType ?? PyType.StrType;

        // PyX_CheckExact
        internal bool IsExact => IsExactly(PyType.StrType);

        // CPython 3.12: Objects/unicodeobject.c - unicode_bool
        // Optimized: Direct length check, no MRO traversal
        protected internal override bool Bool() => CodePointLength() > 0;

        #endregion

        #region String Representation

        // Python str() - PyString은 이미 문자열이므로 자신을 반환
        protected internal override PyString Str() => ResultUnchanged();

        // unicode_repr: the quote is ' unless the text has ' and no ", and each character is itself,
        // an escape (\\ \t \n \r and the quote), or \xhh, \uhhhh, \Uhhhhhhhh when it is not printable.
        protected internal override PyString Repr() => UnicodeRepr();

        // unicode_repr
        internal PyString UnicodeRepr()
        {
            bool hasSingleQuote = false, hasDoubleQuote = false;
            foreach (int cp in CodePoints())
            {
                if (cp == '\'') hasSingleQuote = true;
                else if (cp == '"') hasDoubleQuote = true;
            }
            char quote = hasSingleQuote && !hasDoubleQuote ? '"' : '\'';

            var writer = new PyUnicodeWriter();
            writer.WriteChar(quote);
            foreach (int cp in CodePoints())
            {
                if (cp == quote || cp == '\\')
                {
                    writer.WriteChar('\\');
                    writer.WriteChar(cp);
                }
                else if (cp == '\t') writer.WriteUnits("\\t");
                else if (cp == '\n') writer.WriteUnits("\\n");
                else if (cp == '\r') writer.WriteUnits("\\r");
                else if (cp < ' ' || cp == 0x7F) writer.WriteUnits($"\\x{cp:x2}");
                else if (cp < 0x7F) writer.WriteChar(cp);
                else if (UnicodeType.IsPrintable(cp)) writer.WriteChar(cp);
                else if (cp <= 0xFF) writer.WriteUnits($"\\x{cp:x2}");
                else if (cp <= 0xFFFF) writer.WriteUnits($"\\u{cp:x4}");
                else writer.WriteUnits($"\\U{cp:x8}");
            }
            writer.WriteChar(quote);
            return writer.Finish();
        }

        #endregion

        #region Hash and Equality

        // unicode_hash
        protected internal override long Hash() => HashCodePoints();

        public override int ToHash() => Value.GetHashCode();

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(ContentEquals(otherStr)),
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
        private PyString Slice(int start, int stop, int step)
        {
            int count = PySlice.GetLength(start, stop, step);
            if (count <= 0)
                return Empty;
            if (step == 1)
                return Substring(start, start + count);
            if (IsUcs4)
            {
                var points = new int[count];
                for (int n = 0, i = start; n < count; n++, i += step)
                    points[n] = Ucs4[i];
                return FromCodePoints(points);
            }
            var units = new char[count];
            for (int n = 0, i = start; n < count; n++, i += step)
                units[n] = Ucs2[i];
            return FromUnits(new string(units));
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
        // Py_UNICODE_ISALPHA, Py_UNICODE_ISDECIMAL, Py_UNICODE_ISNUMERIC, Py_UNICODE_ISALNUM
        // (Objects/unicodectype.c over unicodetype_db.h, core/UnicodeType.cs).
        internal static bool IsLetterCp(int cp) => UnicodeType.IsAlpha(cp);
        internal static bool IsDecimalDigitCp(int cp) => UnicodeType.IsDecimalDigit(cp);
        internal static bool IsNumericCp(int cp) => UnicodeType.IsNumeric(cp);
        internal static bool IsLetterOrDigitCp(int cp) => UnicodeType.IsAlnum(cp);

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

        // _PyUnicode_IsXidStart and _PyUnicode_IsXidContinue
        internal static bool IsXidStart(int cp) => UnicodeType.IsXidStart(cp);
        internal static bool IsXidContinue(int cp) => UnicodeType.IsXidContinue(cp);

        // _PyUnicode_ScanIdentifier, whether the whole string is scanned
        // _PyUnicode_ScanIdentifier over the characters: XID_Start or '_' first, then XID_Continue.
        internal static bool ScanIdentifier(PyString s)
        {
            int length = s.CodePointLength();
            if (length == 0)
                return false;
            int first = s.ReadChar(0);
            if (!(IsXidStart(first) || first == '_'))
                return false;
            for (int i = 1; i < length; i++)
                if (!IsXidContinue(s.ReadChar(i)))
                    return false;
            return true;
        }

        // Py_UNICODE_ISSPACE and Py_UNICODE_ISPRINTABLE
        internal static bool IsWhiteSpaceCp(int cp) => UnicodeType.IsSpace(cp);
        internal static bool IsPrintableCp(int cp) => UnicodeType.IsPrintable(cp);


        /// <summary>
        /// The characters of text as one-character str objects. Four places needed this and each had
        /// its own unit loop (PyString.AsList behind list(), PySet's string conversion, the starred
        /// unpacking fast path in the VM, and the exception-group member list). Iterating a str
        /// yields characters in CPython (Objects/unicodeobject.c unicode_iterator), so list(),
        /// set(), (*s) and ExceptionGroup('m', s) all disagreed for astral text.
        /// </summary>
        internal PyObject[] Characters()
        {
            var items = new PyObject[CodePointLength()];
            for (int i = 0; i < items.Length; i++)
                items[i] = FromCodePoint(ReadChar(i));
            return items;
        }

        /// <summary>
        /// _PyUnicode_TransformDecimalAndSpaceToASCII: what int() and float() parse. Below 127 a
        /// character stays, whitespace is ' ', a decimal digit its ASCII digit, and anything else
        /// is '?' that ends the result.
        /// </summary>
        internal static string TransformDecimalAndSpaceToAscii(string unicode)
        {
            bool ascii = true;
            foreach (char c in unicode)
                if (c >= 128) { ascii = false; break; }
            // If the string is already ASCII, just return the same string
            if (ascii)
                return unicode;
            var output = new StringBuilder(unicode.Length);
            for (int i = 0; i < unicode.Length;)
            {
                int ch = CodePointAt(unicode, i, out int width);
                i += width;
                if (ch < 127)
                    output.Append((char)ch);
                else if (UnicodeType.IsSpace(ch))
                    output.Append(' ');
                else
                {
                    int decimalValue = UnicodeType.ToDecimalDigit(ch);
                    if (decimalValue < 0)
                    {
                        output.Append('?');
                        break;
                    }
                    output.Append((char)('0' + decimalValue));
                }
            }
            return output.ToString();
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

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(CompareCodePoints(otherStr) < 0),
                _ => PyNotImplemented.Instance
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(CompareCodePoints(otherStr) <= 0),
                _ => PyNotImplemented.Instance
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(CompareCodePoints(otherStr) > 0),
                _ => PyNotImplemented.Instance
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(CompareCodePoints(otherStr) >= 0),
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
            return Concat(this, otherStr);
        }

        // --- Shared primitives for CPython-exact string operations ---

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

        // A code point as the UTF-16 text holds it: a lone surrogate stays one unit.
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
            PyAllocation.StringLength(PyAllocation.Product(CodePointLength(), times, "repeated string is too long"));
            if (CodePointLength() == 0)
                return Empty;
            if (!IsUcs4)
            {
                var sb = new StringBuilder(Ucs2.Length * (int)times);
                for (long i = 0; i < times; i++)
                    sb.Append(Ucs2);
                return FromUnits(sb.ToString());
            }
            var points = new int[CodePointLength() * (int)times];
            for (int i = 0; i < (int)times; i++)
                Array.Copy(Ucs4, 0, points, i * Ucs4.Length, Ucs4.Length);
            return FromCodePoints(points);
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
            var formatted = FormatPercent(this, other);
            return SoleLiteral(this, FindCodePoints(this, PercentSign, 0, CodePointLength()) < 0) ?? formatted;
        }

        /// <summary>
        /// _PyUnicodeWriter_Finish when the writer holds nothing but the whole format string, written
        /// once (WriteSubstring of the full range is WriteStr, which keeps the object itself): the
        /// format string comes back as it is, a str subclass instance included. An empty one is "".
        /// </summary>
        private static PyString SoleLiteral(PyString format, bool noFields) =>
            noFields && format.CodePointLength() > 0 ? format : null;

        private static PyString PercentSign => FromUnits("%");

        // PyUnicode_Format: the format is read a character at a time and the result written by one
        // _PyUnicodeWriter, so the precision and width of %s, %r and %a count characters.
        private static PyString FormatPercent(PyString fmt, PyObject arg)
        {
            // CPython unicode_format: a non-tuple right operand is one argument
            // (or a mapping); a tuple is a positional argument list.
            bool positional = arg is PyTuple;
            PyObject[] args = positional ? ((PyTuple)arg).Items : null;
            int ai = 0;
            bool singleUsed = false;
            var sb = new PyUnicodeWriter();
            PyString whole = null;
            int i = 0;
            int n = fmt.CodePointLength();

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

            while (i < n)
            {
                int c = fmt.ReadChar(i);
                if (c != '%')
                {
                    sb.WriteChar(c);
                    i++;
                    continue;
                }

                i++;
                if (i >= n)
                    throw PyValueError.Create("incomplete format");
                if (fmt.ReadChar(i) == '%')
                {
                    sb.WriteChar('%');
                    i++;
                    continue;
                }

                PyObject value = null;
                if (fmt.ReadChar(i) == '(')
                {
                    if (!positional)
                    {
                        // unicode_format: the argument is the dict when its type has mp_subscript and it
                        // is no tuple or str (PyMapping_Check); anything else has no (key).
                        if (arg is PyString || !PyObject.MappingCheck(arg))
                            throw PyTypeError.Create("format requires a mapping");
                        i++;
                        int keyStart = i;
                        int depth = 1;
                        while (i < n)
                        {
                            if (fmt.ReadChar(i) == '(')
                                depth++;
                            else if (fmt.ReadChar(i) == ')')
                            {
                                depth--;
                                if (depth == 0)
                                    break;
                            }
                            i++;
                        }
                        if (i >= n)
                            throw PyValueError.Create("incomplete format; also it should contain a right parenthesis");
                        var key = fmt.Substring(keyStart, i);
                        i++; // ')'
                        value = arg.GetItem(key);
                    }
                    else
                    {
                        throw PyTypeError.Create("format requires a mapping");
                    }
                }
                else
                {
                    // A spec with no (key) takes the next argument, which for a mapping is the
                    // mapping itself (unicode_format's args, arglen -1). Values are consumed after the spec is parsed (CPython order),
                    // so a '*' width claims its argument before the conversion value.
                }

                bool alt = false, leftAdjust = false, signPlus = false, signSpace = false, zeroPad = false;
                while (i < n)
                {
                    int f = fmt.ReadChar(i);
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
                if (i < n && fmt.ReadChar(i) == '*')
                {
                    i++;
                    widthValue = PyArgs.Ssize(NextStar());
                    if (widthValue < 0)
                    {
                        leftAdjust = true;
                        widthValue = widthValue == long.MinValue ? long.MaxValue : -widthValue;
                    }
                }
                else if (i < n && IsAsciiDigit(fmt.ReadChar(i)))
                {
                    widthValue = 0;
                    while (i < n && IsAsciiDigit(fmt.ReadChar(i)))
                    {
                        int d = fmt.ReadChar(i) - '0';
                        if (widthValue > (long.MaxValue - d) / 10)
                            throw PyValueError.Create("width too big");
                        widthValue = widthValue * 10 + d;
                        i++;
                    }
                }
                // A result no CLR string can hold is the MemoryError of its allocation.
                int width = PyAllocation.StringLength(widthValue);

                int precision = 6;
                bool hasPrecision = false;
                if (i < n && fmt.ReadChar(i) == '.')
                {
                    hasPrecision = true;
                    i++;
                    if (i < n && fmt.ReadChar(i) == '*')
                    {
                        i++;
                        precision = PyArgs.Int(NextStar());
                        if (precision < 0)
                            precision = 0;
                    }
                    else
                    {
                        precision = 0;
                        while (i < n && IsAsciiDigit(fmt.ReadChar(i)))
                        {
                            int d = fmt.ReadChar(i) - '0';
                            if (precision > (int.MaxValue - d) / 10)
                                throw PyValueError.Create("precision too big");
                            precision = precision * 10 + d;
                            i++;
                        }
                    }
                }

                // unicode_format_arg_parse: one "h", "l" or "L" length prefix is ignored ("%ld").
                if (i < n && fmt.ReadChar(i) is 'h' or 'l' or 'L')
                    i++;
                if (i >= n)
                    throw PyValueError.Create("incomplete format");
                int conv = fmt.ReadChar(i);
                int convIndex = i;
                i++;
                value ??= NextPositional();

                // the field's text: a str for %s, %r, %a and %c, ASCII digits for the numbers
                PyString text = null;
                string body = null;
                string sign = "";
                bool numeric = false;
                switch (conv)
                {
                    case 's':
                    case 'r':
                    case 'a':
                        // unicode_format_arg_format: PyObject_Str, PyObject_Repr or PyObject_ASCII,
                        // then the precision cuts characters
                        text = conv == 's' ? value.ToStr() : conv == 'r' ? value.ToRepr() : AsciiOf(value);
                        if (hasPrecision && text.CodePointLength() > precision)
                            text = text.Substring(0, precision);
                        break;
                    case 'd':
                    case 'i':
                    case 'u':
                    {
                        numeric = true;
                        BigInteger ival = PyPercentFormat.Integer(value, (char)conv);
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
                        BigInteger ival = PyPercentFormat.Integer(value, (char)conv);
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
                            text = FromCodePoint(character.ReadChar(0));
                            break;
                        }
                        var code = PyPercentFormat.Character(value) ??
                            throw PyTypeError.Create("%c requires int or char");
                        if (code < 0 || code > 0x10FFFF)
                            throw PyOverflowError.Create("%c arg not in range(0x110000)");
                        text = FromCodePoint((int)code);
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
                        bool upper = conv is 'F' or 'E' or 'G';
                        if (double.IsNaN(dv)) body = upper ? "NAN" : "nan";
                        else if (double.IsInfinity(dv)) body = upper ? "INF" : "inf";
                        else body = PyFloatFormatter.DoubleToString(Math.Abs(dv), (char)conv, precision, alt);
                        if (!double.IsNaN(dv) && double.IsNegative(dv)) sign = "-";
                        else if (signPlus) sign = "+";
                        else if (signSpace) sign = " ";
                        break;
                    }
                    default:
                        // '%c' of the character when it is printable ASCII, else '?'
                        throw PyValueError.Create($"unsupported format character '{(conv >= 31 && conv <= 126 ? (char)conv : '?')}' (0x{conv:x}) at index {convIndex}");
                }

                if (text != null)
                {
                    // a text field: spaces to the width, on the left unless "-"
                    int chars = text.CodePointLength();
                    // unicode_format_arg: the last spec is written without overallocation, so a
                    // field that needs no padding and is the whole result is that str itself.
                    if (i == n && sb.Length == 0 && width <= chars && chars > 0)
                    {
                        whole = text;
                        continue;
                    }
                    if (!leftAdjust && width > chars)
                        for (int k = chars; k < width; k++) sb.WriteChar(' ');
                    sb.WriteStr(text);
                    if (leftAdjust && width > chars)
                        for (int k = chars; k < width; k++) sb.WriteChar(' ');
                    continue;
                }
                var field = new StringBuilder();
                string core = numeric ? body : body;
                string head = sign + core;
                if (numeric && alt && (conv == 'o' || conv == 'x' || conv == 'X') && zeroPad && !leftAdjust && width > head.Length)
                {
                    int pad = width - head.Length;
                    // The zeros go between the sign and prefix and the digits.
                    int prefix = core.Length > 1 && core[0] == '0' && !char.IsDigit(core[1]) ? 2 : 0;
                    field.Append(sign).Append(core, 0, prefix).Append('0', Math.Max(0, pad)).Append(core, prefix, core.Length - prefix);
                }
                else if (leftAdjust)
                {
                    field.Append(head);
                    if (width > head.Length)
                        field.Append(' ', width - head.Length);
                }
                else if (zeroPad && numeric && !signPlus && !signSpace)
                {
                    if (width > head.Length)
                    {
                        field.Append(sign);
                        string digitsPart = core;
                        if (alt && (conv == 'x' || conv == 'X' || conv == 'o') && digitsPart.Length > 0 && digitsPart[0] == '0')
                        {
                            field.Append(digitsPart.Substring(0, 1));
                            field.Append('0', width - head.Length);
                            field.Append(digitsPart.Substring(1));
                        }
                        else
                        {
                            field.Append('0', width - head.Length).Append(digitsPart);
                        }
                    }
                    else
                        field.Append(head);
                }
                else if (zeroPad && numeric && (signPlus || signSpace))
                {
                    // sign occupies the first char; zeros fill after the sign
                    field.Append(sign);
                    if (width > head.Length)
                        field.Append('0', width - head.Length);
                    field.Append(core);
                }
                else
                {
                    if (width > head.Length)
                        field.Append(' ', width - head.Length);
                    field.Append(head);
                }
                sb.WriteUnits(field.ToString());
            }

            // PyUnicode_Format: ctx.dict is a mapping that is no tuple or str.
            if ((positional && ai < args.Length) || (!positional && !singleUsed &&
                (arg is PyString || !PyObject.MappingCheck(arg))))
                throw PyTypeError.Create("not all arguments converted during string formatting");
            return whole ?? sb.Finish();
        }

        private static bool IsAsciiDigit(int c) => c >= '0' && c <= '9';


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

            return FromCodePoint(ReadChar(index));
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
                return Slice(start, stop, step);
            }
            else
            {
                // CPython 3.12: Objects/unicodeobject.c unicode_subscript uses
                // PyNumber_AsSsize_t for non-slice keys (accepts __index__) and reports
                // "string indices must be integers, not 'T'" - different wording from list.
                if (TrySequenceIndex(index, out int position))
                    return GetItem(position);
                throw PyTypeError.Create($"string indices must be integers, not '{index.GetTypeName()}'");
            }
        }

        #endregion

        #region String Methods

        // unicode_split's clinic: sep is any object and maxsplit a Py_ssize_t, both converted
        // before the body asks sep to be str or None.
        /// <summary>
        /// unicode_result_unchanged (Objects/unicodeobject.c): an exact str is its own result, a
        /// subclass instance gives an exact copy.
        /// </summary>
        // unicode_get_empty: every exact empty str is the one singleton in CPython.
        private bool IsEmptySingleton => CodePointLength() == 0 && IsExact;

        internal PyString ResultUnchanged() => IsExact ? this : Copy();

        public PyString Join(PyObject iterable)
        {
            // stringlib join: PySequence_Fast consumes the iterable before checking element types.
            PyObject[] values = PyList.SequenceFast(iterable, "can only join an iterable");
            return JoinArray(this, values);
        }

        #endregion

        #region Length and Contains

        // CPython: PyUnicode_GET_LENGTH counts characters. Everything above this line that needs a
        // unit offset converts through CodePointToUnit.
        protected internal override long Len() => CodePointLength();
        
        
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
                return PyBool.FromBool(FindCodePoints(this, pyStr, 0, CodePointLength()) >= 0);

            throw PyTypeError.Create($"'in <string>' requires string as left operand, not {item.GetTypeName()}");
        }

        #endregion

        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyString → C# basic types) ===
        
        /// <summary>
        /// CPython PyObject_IsTrue 호환: PyString에서 C# bool 값 추출
        /// </summary>
        
        

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
            return PyBool.FromBool(CodePointLength() > 0);
        }
        
        #endregion

        #region Encoding

        /// <summary>
        /// PyUnicode_AsEncodedString: the fast paths of the UTF codecs, ascii and latin-1 (the
        /// encoders of Objects/unicodeobject.c, UnicodeEncoders), else _PyCodec_EncodeText.
        /// </summary>
        public PyBytes Encode(string encoding = "utf-8", string errors = null)
        {
            // PyUnicode_AsEncodedString
            PyCodecRegistry.CheckEncodingErrors(encoding, errors);
            if (encoding == null)
                return PyBytes.From(UnicodeEncoders.EncodeUtf8(this, errors));
            switch (PyStringNewArguments.ShortcutOf(encoding))
            {
                case PyStringNewArguments.Shortcut.Utf8:
                    return PyBytes.From(UnicodeEncoders.EncodeUtf8(this, errors));
                case PyStringNewArguments.Shortcut.Utf16:
                    return PyBytes.From(UnicodeEncoders.EncodeUtf16Or32(this, errors, 0, 2));
                case PyStringNewArguments.Shortcut.Utf32:
                    return PyBytes.From(UnicodeEncoders.EncodeUtf16Or32(this, errors, 0, 4));
                case PyStringNewArguments.Shortcut.Ascii:
                    return PyBytes.From(UnicodeEncoders.EncodeUcs1(this, errors, 128));
                case PyStringNewArguments.Shortcut.Mbcs:
                    return PyBytes.From(PyCodePage.Encode(PyCodePage.CP_ACP, this, errors));
                case PyStringNewArguments.Shortcut.Latin1:
                    return PyBytes.From(UnicodeEncoders.EncodeUcs1(this, errors, 256));
                default:
                    // Encode via the codec registry
                    PyObject v = PyCodecRegistry.EncodeText(this, encoding, errors);
                    // The normal path
                    if (v is PyBytes encoded)
                        return encoded;
                    // If the codec returns a buffer, raise a warning and convert to bytes
                    if (v is PyByteArray array)
                    {
                        PyWarnings.Warn(PyType.RuntimeWarningType,
                            $"encoder {encoding} returned bytearray instead of bytes; use codecs.encode() to encode to arbitrary types", 1);
                        return PyBytes.From(array.ToByteArrayCopy());
                    }
                    throw PyTypeError.Create(
                        $"'{encoding}' encoder returned '{v.GetPyType().FullName}' instead of 'bytes'; use codecs.encode() to encode to arbitrary types");
            }
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


        internal PyObject IsIdentifier(PyObject[] args)
        {
            return ScanIdentifier(this) ? PyBool.True : PyBool.False;
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
        /// <summary>
        /// format_string_internal (Python/formatter_unicode.c): the spec is read as characters, what
        /// a string has no use for is refused by name, the precision cuts characters and the fill
        /// pads to the width. A value that needs neither is written whole, which is the value.
        /// </summary>
        internal static PyString FormatString(PyString value, PyString formatSpec, string typeName = "str")
        {
            if (formatSpec.CodePointLength() == 0)
                return value;

            var format = FormatSpec.Parse(typeName, formatSpec, 's', '<');
            if (format.Type != 's')
                throw FormatSpec.Unknown(format.Type, typeName);
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
            PyAllocation.StringLength(format.Width);

            int len = value.CodePointLength();
            if ((format.Width == -1 || format.Width <= len) && (format.Precision == -1 || format.Precision >= len))
                return value;
            if (format.Precision >= 0 && len >= format.Precision)
                len = (int)format.Precision;
            // calc_padding
            int total = format.Width >= 0 && format.Width > len ? (int)format.Width : len;
            int lpad = format.Align == '>' ? total - len : format.Align == '^' ? (total - len) / 2 : 0;
            int rpad = total - len - lpad;
            var writer = new PyUnicodeWriter();
            for (int k = 0; k < lpad; k++)
                writer.WriteChar(format.FillChar);
            writer.WriteSubstring(value, 0, len);
            for (int k = 0; k < rpad; k++)
                writer.WriteChar(format.FillChar);
            return writer.Finish();
        }

        #endregion
    }
}
