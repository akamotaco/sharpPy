using System;
using System.Text;
using System.Globalization;

namespace SharpPy
{
    /// <summary>
    /// Python str 타입 구현 - C# string을 기반으로 한 문자열
    /// </summary>
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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'join' requires a 'str' object but received a '{self.GetTypeName()}'");

                    // Use private Join method implementation
                    var iterable = args[0];
                    var items = new System.Collections.Generic.List<string>();

                    if (iterable is PyList list)
                    {
                        foreach (var item in list.Items)
                        {
                            if (item is PyString itemStr)
                                items.Add(itemStr.Value);
                            else
                                throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                        }
                    }
                    else if (iterable is PyTuple tuple)
                    {
                        foreach (var item in tuple.Items)
                        {
                            if (item is PyString itemStr)
                                items.Add(itemStr.Value);
                            else
                                throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                        }
                    }
                    else
                    {
                        throw PyTypeError.Create("can only join an iterable");
                    }

                    return new PyString(string.Join(str.Value, items));
                },
                minArgs: 1, maxArgs: 1
            );

            // 기타 주요 메서드들도 등록
            strType.TypeDict["upper"] = new PyMethodDescriptor(
                "upper", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"upper() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'upper' requires a 'str' object but received a '{self.GetTypeName()}'");
                    return new PyString(str.Value.ToUpperInvariant());
                },
                minArgs: 0, maxArgs: 0
            );

            strType.TypeDict["lower"] = new PyMethodDescriptor(
                "lower", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"lower() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'lower' requires a 'str' object but received a '{self.GetTypeName()}'");
                    return new PyString(str.Value.ToLowerInvariant());
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
                        throw PyTypeError.Create($"capitalize() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'capitalize' requires a 'str' object but received a '{self.GetTypeName()}'");

                    // Empty string remains empty
                    if (str.Value.Length == 0)
                        return str;

                    // CPython 3.12: Objects/unicodeobject.c:9575-9596 - do_capitalize
                    // First character to title case (upper for most chars), rest to lower case
                    var textInfo = CultureInfo.InvariantCulture.TextInfo;
                    return new PyString(
                        char.ToUpperInvariant(str.Value[0]) +
                        (str.Value.Length > 1 ? str.Value.Substring(1).ToLowerInvariant() : "")
                    );
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: str.title() - titlecase the string
            strType.TypeDict["title"] = new PyMethodDescriptor(
                "title", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"title() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'title' requires a 'str' object but received a '{self.GetTypeName()}'");

                    // Python's title() converts to titlecase (first char of each word uppercase)
                    var textInfo = System.Globalization.CultureInfo.InvariantCulture.TextInfo;
                    return new PyString(textInfo.ToTitleCase(str.Value.ToLowerInvariant()));
                },
                minArgs: 0, maxArgs: 0
            );

            strType.TypeDict["split"] = new PyMethodDescriptor(
                "split", strType,
                (self, args, kwargs) => {
                    if (args.Length > 2)
                        throw PyTypeError.Create($"split() takes at most 2 arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'split' requires a 'str' object but received a '{self.GetTypeName()}'");

                    string sep = null;
                    int maxsplit = -1;
                    if (args.Length >= 1 && args[0] is PyString sepStr)
                        sep = sepStr.Value;
                    if (args.Length >= 2 && args[1] is PyInt maxsplitInt)
                        maxsplit = (int)maxsplitInt.Value;

                    return str.Split(sep, maxsplit);
                },
                minArgs: 0, maxArgs: 2
            );

            strType.TypeDict["strip"] = new PyMethodDescriptor(
                "strip", strType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create($"strip() takes at most 1 argument ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'strip' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'replace' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'zfill' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                        throw PyTypeError.Create($"isdigit() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'isdigit' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                        throw PyTypeError.Create($"isalpha() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'isalpha' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                        throw PyTypeError.Create($"isalnum() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'isalnum' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                        throw PyTypeError.Create($"isspace() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'isspace' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                        throw PyTypeError.Create($"isupper() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'isupper' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    bool hasCased = false;
                    foreach (char c in str.Value)
                    {
                        if (char.IsLower(c))
                            return PyBool.False;
                        if (char.IsUpper(c))
                            hasCased = true;
                    }
                    return PyBool.FromBool(hasCased);
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11910-11927 - unicode_islower_impl
            // Return True if the string is a lowercase string, False otherwise.
            strType.TypeDict["islower"] = new PyMethodDescriptor(
                "islower", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"islower() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'islower' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    bool hasCased = false;
                    foreach (char c in str.Value)
                    {
                        if (char.IsUpper(c))
                            return PyBool.False;
                        if (char.IsLower(c))
                            hasCased = true;
                    }
                    return PyBool.FromBool(hasCased);
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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'find' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    int start = 0;
                    int end = str.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);
                    if (end > str.Value.Length) end = str.Value.Length;
                    if (start > end) return new PyInt(-1);

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
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"index() takes from 1 to 3 positional arguments but {args.Length} were given");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'index' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    int start = 0;
                    int end = str.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);
                    if (end > str.Value.Length) end = str.Value.Length;
                    if (start > end)
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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'rfind' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    int start = 0;
                    int end = str.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);
                    if (end > str.Value.Length) end = str.Value.Length;
                    if (start > end) return new PyInt(-1);

                    int index = str.Value.LastIndexOf(sub.Value, start, end - start);
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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'rindex' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    int start = 0;
                    int end = str.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);
                    if (end > str.Value.Length) end = str.Value.Length;
                    if (start > end)
                        throw PyValueError.Create("substring not found");

                    int index = str.Value.LastIndexOf(sub.Value, start, end - start);
                    if (index == -1)
                        throw PyValueError.Create("substring not found");
                    return new PyInt(index);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/unicodeobject.c:9877-9911 - unicode_count
            // Return the number of non-overlapping occurrences of substring sub
            strType.TypeDict["count"] = new PyMethodDescriptor(
                "count", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"count() takes from 1 to 3 positional arguments but {args.Length} were given");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'count' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyString sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    if (sub.Value.Length == 0)
                        return new PyInt(str.Value.Length + 1);

                    int start = 0;
                    int end = str.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);
                    if (end > str.Value.Length) end = str.Value.Length;
                    if (start > end) return new PyInt(0);

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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'startswith' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyString prefix)
                        throw PyTypeError.Create("startswith first arg must be str");

                    int start = 0;
                    int end = str.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);
                    if (end > str.Value.Length) end = str.Value.Length;

                    if (start >= end || start + prefix.Value.Length > end)
                        return PyBool.False;

                    string substring = str.Value.Substring(start, Math.Min(prefix.Value.Length, end - start));
                    return PyBool.FromBool(substring == prefix.Value);
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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'endswith' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyString suffix)
                        throw PyTypeError.Create("endswith first arg must be str");

                    int start = 0;
                    int end = str.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start = Math.Max(0, str.Value.Length + start);
                    if (end < 0) end = Math.Max(0, str.Value.Length + end);
                    if (end > str.Value.Length) end = str.Value.Length;

                    if (start >= end || end - suffix.Value.Length < start)
                        return PyBool.False;

                    int suffixStart = end - suffix.Value.Length;
                    if (suffixStart < start)
                        return PyBool.False;

                    string substring = str.Value.Substring(suffixStart, suffix.Value.Length);
                    return PyBool.FromBool(substring == suffix.Value);
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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'lstrip' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'rstrip' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'rsplit' requires a 'str' object but received a '{self.GetTypeName()}'");

                    string sep = null;
                    int maxsplit = -1;

                    if (args.Length >= 1 && args[0] is not PyNone)
                    {
                        if (args[0] is not PyString sepStr)
                            throw PyTypeError.Create("sep must be str or None");
                        sep = sepStr.Value;
                    }

                    if (args.Length >= 2 && args[1] is PyInt maxInt)
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
            );

            // CPython 3.12: Objects/unicodeobject.c:10248-10289 - unicode_splitlines
            // Return a list of the lines in the string, breaking at line boundaries
            strType.TypeDict["splitlines"] = new PyMethodDescriptor(
                "splitlines", strType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create($"splitlines() takes at most 1 argument ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'splitlines' requires a 'str' object but received a '{self.GetTypeName()}'");

                    bool keepends = false;
                    if (args.Length >= 1 && args[0] is PyBool keependsB)
                        keepends = keependsB.Value;
                    else if (args.Length >= 1 && args[0] is PyInt keependsI)
                        keepends = keependsI.Value != 0;

                    var lines = str.Value.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
                    var items = new System.Collections.Generic.List<PyObject>();

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (keepends && i < lines.Length - 1)
                        {
                            // Add back the line separator
                            if (i < str.Value.Length)
                            {
                                int pos = str.Value.IndexOf(line, System.StringComparison.Ordinal);
                                if (pos >= 0 && pos + line.Length < str.Value.Length)
                                {
                                    char nextChar = str.Value[pos + line.Length];
                                    if (nextChar == '\r' && pos + line.Length + 1 < str.Value.Length && str.Value[pos + line.Length + 1] == '\n')
                                        line += "\r\n";
                                    else if (nextChar == '\r' || nextChar == '\n')
                                        line += nextChar;
                                }
                            }
                        }
                        if (i < lines.Length - 1 || line.Length > 0)
                            items.Add(new PyString(line));
                    }

                    return new PyList(items);
                },
                minArgs: 0, maxArgs: 1
            );

            // CPython 3.12: Objects/unicodeobject.c:10533-10550 - unicode_swapcase
            // Return a copy with uppercase characters converted to lowercase and vice versa
            strType.TypeDict["swapcase"] = new PyMethodDescriptor(
                "swapcase", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"swapcase() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'swapcase' requires a 'str' object but received a '{self.GetTypeName()}'");

                    var result = new System.Text.StringBuilder(str.Value.Length);
                    foreach (char c in str.Value)
                    {
                        if (char.IsUpper(c))
                            result.Append(char.ToLowerInvariant(c));
                        else if (char.IsLower(c))
                            result.Append(char.ToUpperInvariant(c));
                        else
                            result.Append(c);
                    }
                    return new PyString(result.ToString());
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/unicodeobject.c:11674-11691 - unicode_casefold
            // Return a casefolded copy (aggressive lowercase for caseless matching)
            strType.TypeDict["casefold"] = new PyMethodDescriptor(
                "casefold", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"casefold() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'casefold' requires a 'str' object but received a '{self.GetTypeName()}'");

                    return new PyString(str.Value.ToLowerInvariant());
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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'center' requires a 'str' object but received a '{self.GetTypeName()}'");

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

                    int totalPad = width - str.Value.Length;
                    int leftPad = totalPad / 2;
                    int rightPad = totalPad - leftPad;

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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'ljust' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'rjust' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                        throw PyTypeError.Create($"isascii() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'isascii' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                        throw PyTypeError.Create($"isdecimal() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'isdecimal' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                        throw PyTypeError.Create($"isnumeric() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'isnumeric' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                        throw PyTypeError.Create($"isidentifier() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'isidentifier' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                        throw PyTypeError.Create($"isprintable() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'isprintable' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                        throw PyTypeError.Create($"istitle() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'istitle' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (str.Value.Length == 0)
                        return PyBool.False;

                    bool inWord = false;
                    bool hasCased = false;

                    foreach (char c in str.Value)
                    {
                        if (char.IsUpper(c))
                        {
                            if (inWord)
                                return PyBool.False;
                            inWord = true;
                            hasCased = true;
                        }
                        else if (char.IsLower(c))
                        {
                            if (!inWord)
                                return PyBool.False;
                            hasCased = true;
                        }
                        else
                        {
                            inWord = false;
                        }
                    }

                    return PyBool.FromBool(hasCased);
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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'partition' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'rpartition' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'removeprefix' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'removesuffix' requires a 'str' object but received a '{self.GetTypeName()}'");

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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'expandtabs' requires a 'str' object but received a '{self.GetTypeName()}'");

                    int tabsize = 8;
                    if (args.Length >= 1 && args[0] is PyInt tabInt)
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
            );

            // CPython 3.12: Objects/unicodeobject.c:11246-11250 - unicode_encode_impl
            // Encode the string using the specified encoding
            strType.TypeDict["encode"] = new PyMethodDescriptor(
                "encode", strType,
                (self, args, kwargs) => {
                    if (args.Length > 2)
                        throw PyTypeError.Create($"encode() takes at most 2 arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'encode' requires a 'str' object but received a '{self.GetTypeName()}'");

                    string encoding = "utf-8";
                    string errors = "strict";

                    if (args.Length >= 1)
                    {
                        if (args[0] is PyString encodingStr)
                            encoding = encodingStr.Value.ToLowerInvariant();
                        else
                            throw PyTypeError.Create("encode() encoding must be str");
                    }

                    if (args.Length >= 2)
                    {
                        if (args[1] is PyString errorsStr)
                            errors = errorsStr.Value;
                        else
                            throw PyTypeError.Create("encode() errors must be str");
                    }

                    try
                    {
                        Encoding enc = encoding switch
                        {
                            "utf-8" or "utf8" => Encoding.UTF8,
                            "ascii" => Encoding.ASCII,
                            "utf-16" or "utf16" => Encoding.Unicode,
                            "utf-32" or "utf32" => Encoding.UTF32,
                            "latin-1" or "latin1" or "iso-8859-1" => Encoding.Latin1,
                            _ => throw PyLookupError.Create($"unknown encoding: {encoding}")
                        };

                        byte[] bytes = enc.GetBytes(str.Value);
                        return new PyBytes(bytes);
                    }
                    catch (EncoderFallbackException)
                    {
                        throw PyUnicodeEncodeError.Create($"'{encoding}' codec can't encode characters");
                    }
                },
                minArgs: 0, maxArgs: 2
            );

            // CPython 3.12: Objects/stringlib/unicode_format.h:940-958 - do_string_format
            // Format string using .format() method with {} placeholders
            strType.TypeDict["format"] = new PyMethodDescriptor(
                "format", strType,
                (self, args, kwargs) => {
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'format' requires a 'str' object but received a '{self.GetTypeName()}'");

                    // Simple implementation: use C# string.Format for basic cases
                    // Full PEP 3101 implementation would be complex
                    try
                    {
                        // Convert Python args to object array for C# formatting
                        object[] formatArgs = new object[args.Length];
                        for (int i = 0; i < args.Length; i++)
                        {
                            formatArgs[i] = args[i].ToString();
                        }

                        // Replace Python {} format with C# {0} {1} etc.
                        string format = str.Value;
                        int argIndex = 0;
                        var result = new System.Text.StringBuilder();
                        int i_pos = 0;

                        while (i_pos < format.Length)
                        {
                            int openBrace = format.IndexOf('{', i_pos);
                            int closeBrace = format.IndexOf('}', i_pos);

                            // Handle }} escape first
                            if ((closeBrace != -1) && (openBrace == -1 || closeBrace < openBrace))
                            {
                                // Check for escaped closing brace }}
                                if (closeBrace + 1 < format.Length && format[closeBrace + 1] == '}')
                                {
                                    result.Append(format.Substring(i_pos, closeBrace - i_pos));
                                    result.Append('}');
                                    i_pos = closeBrace + 2;
                                    continue;
                                }
                                else
                                {
                                    throw PyValueError.Create("Single '}' encountered in format string");
                                }
                            }

                            if (openBrace == -1)
                            {
                                result.Append(format.Substring(i_pos));
                                break;
                            }

                            // Check for escaped brace {{
                            if (openBrace + 1 < format.Length && format[openBrace + 1] == '{')
                            {
                                result.Append(format.Substring(i_pos, openBrace - i_pos));
                                result.Append('{');
                                i_pos = openBrace + 2;
                                continue;
                            }

                            result.Append(format.Substring(i_pos, openBrace - i_pos));

                            closeBrace = format.IndexOf('}', openBrace);
                            if (closeBrace == -1)
                                throw PyValueError.Create("Single '{' encountered in format string");

                            string field = format.Substring(openBrace + 1, closeBrace - openBrace - 1);

                            // Simple field reference: {}, {0}, {name}
                            if (string.IsNullOrEmpty(field))
                            {
                                if (argIndex >= args.Length)
                                    throw PyIndexError.Create("Replacement index out of range");
                                result.Append(args[argIndex].ToStr().Value);
                                argIndex++;
                            }
                            else if (int.TryParse(field, out int index))
                            {
                                if (index >= args.Length)
                                    throw PyIndexError.Create("Replacement index out of range");
                                result.Append(args[index].ToStr().Value);
                            }
                            else
                            {
                                // Named argument - requires kwargs
                                if (kwargs != null && kwargs.Contains(new PyString(field)).Value)
                                {
                                    var value = kwargs.GetItem(new PyString(field));
                                    result.Append(value.ToStr().Value);
                                }
                                else
                                {
                                    throw PyKeyError.Create(field);
                                }
                            }

                            i_pos = closeBrace + 1;
                        }

                        return new PyString(result.ToString());
                    }
                    catch (FormatException)
                    {
                        throw PyValueError.Create("Invalid format string");
                    }
                }
            );

            // CPython 3.12: Objects/stringlib/unicode_format.h:961-964 - do_string_format_map
            // Format string using .format_map() method with mapping object
            strType.TypeDict["format_map"] = new PyMethodDescriptor(
                "format_map", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"format_map() takes exactly one argument ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'format_map' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyDict mapping)
                        throw PyTypeError.Create("format_map() argument must be a dict");

                    // Use format() implementation with empty args and mapping as kwargs
                    var formatDescriptor = strType.TypeDict["format"] as PyMethodDescriptor;
                    return formatDescriptor!.Call(new PyObject[] { self }, mapping);
                },
                minArgs: 1, maxArgs: 1
            );

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
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'translate' requires a 'str' object but received a '{self.GetTypeName()}'");

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
        }

        #region Core Properties

        public string Value { get; }

        public PyString(string value) => Value = value ?? "";

        public override PyType GetPyType() => PyType.StrType;
        public override string GetTypeName() => "str";

        #endregion

        #region String Representation

        // Python str() - PyString은 이미 문자열이므로 자신을 반환
        public override PyString ToStr() => this;

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

            foreach (char c in Value)
            {
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
                    result.Append($"\\x{(int)c:x2}");
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

            var category = char.GetUnicodeCategory(c);

            // Control characters are not printable
            if (category == System.Globalization.UnicodeCategory.Control ||
                category == System.Globalization.UnicodeCategory.Format ||
                category == System.Globalization.UnicodeCategory.Surrogate ||
                category == System.Globalization.UnicodeCategory.PrivateUse ||
                category == System.Globalization.UnicodeCategory.OtherNotAssigned)
            {
                return false;
            }

            // Line and paragraph separators are not printable
            if (category == System.Globalization.UnicodeCategory.LineSeparator ||
                category == System.Globalization.UnicodeCategory.ParagraphSeparator)
            {
                return false;
            }

            // Special case: Non-breaking space (U+00A0) is not printable in CPython
            if (c == '\u00A0')
            {
                return false;
            }

            // Other space separators might have similar rules, but let's start with this specific case
            return true;
        }

        #endregion

        #region Hash and Equality

        public override int ToHash() => Value.GetHashCode();

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(Value == otherStr.Value),
                _ => PyBool.False
            };
        }

        #endregion

        #region Comparison Operations

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(string.Compare(Value, otherStr.Value, StringComparison.Ordinal) < 0),
                _ => throw PyTypeError.Create($"'<' not supported between instances of 'str' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(string.Compare(Value, otherStr.Value, StringComparison.Ordinal) <= 0),
                _ => throw PyTypeError.Create($"'<=' not supported between instances of 'str' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(string.Compare(Value, otherStr.Value, StringComparison.Ordinal) > 0),
                _ => throw PyTypeError.Create($"'>' not supported between instances of 'str' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(string.Compare(Value, otherStr.Value, StringComparison.Ordinal) >= 0),
                _ => throw PyTypeError.Create($"'>=' not supported between instances of 'str' and '{other.GetTypeName()}'")
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
                return PyNotImplemented.Instance;

            return new PyString(Value + otherStr.Value);
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
            // CPython 3.12: Support both single values and tuples
            PyObject[] values;

            if (other is PyTuple tuple)
            {
                // Tuple of values: "Hello %s %d" % ("world", 42)
                values = tuple.Items;
            }
            else
            {
                // Single value: "Hello %s" % "world"
                values = new PyObject[] { other };
            }

            try
            {
                // Simple % formatting - support %s, %d, %r
                var result = Value;
                int valueIndex = 0;

                for (int i = 0; i < result.Length; i++)
                {
                    if (result[i] == '%' && i + 1 < result.Length)
                    {
                        char formatChar = result[i + 1];

                        if (formatChar == '%')
                        {
                            // Escape: %% -> %
                            result = result.Remove(i, 1);
                            continue;
                        }

                        if (valueIndex >= values.Length)
                        {
                            throw PyTypeError.Create("not enough arguments for format string");
                        }

                        var value = values[valueIndex++];
                        string replacement;

                        switch (formatChar)
                        {
                            case 's': // String
                                replacement = value is PyString str ? str.Value : value.ToStr().Value;
                                break;
                            case 'r': // Repr
                                replacement = value.ToRepr().Value;
                                break;
                            case 'd': // Decimal integer
                            case 'i': // Integer
                                replacement = value is PyInt pyInt ? pyInt.Value.ToString() : value.ToStr().Value;
                                break;
                            case 'f': // Float
                                // CPython: Objects/unicodeobject.c:PyUnicode_FromFormat - %f uses 6 decimal places by default
                                if (value is PyFloat pyFloat)
                                    replacement = pyFloat.Value.ToString("F6", CultureInfo.InvariantCulture);
                                else if (value is PyInt pyIntForFloat)
                                    replacement = ((double)pyIntForFloat.Value).ToString("F6", CultureInfo.InvariantCulture);
                                else
                                    replacement = value.ToStr().Value;
                                break;
                            case 'g': // General format (lowercase) - Python uses 6 significant digits by default
                                if (value is PyFloat pyFloatG)
                                    replacement = pyFloatG.Value.ToString("g6", CultureInfo.InvariantCulture);
                                else if (value is PyInt pyIntG)
                                    replacement = ((double)pyIntG.Value).ToString("g6", CultureInfo.InvariantCulture);
                                else
                                    replacement = value.ToStr().Value;
                                break;
                            case 'G': // General format (uppercase) - Python uses 6 significant digits by default
                                if (value is PyFloat pyFloatGUpper)
                                    replacement = pyFloatGUpper.Value.ToString("G6", CultureInfo.InvariantCulture);
                                else if (value is PyInt pyIntGUpper)
                                    replacement = ((double)pyIntGUpper.Value).ToString("G6", CultureInfo.InvariantCulture);
                                else
                                    replacement = value.ToStr().Value;
                                break;
                            case 'e': // Exponent format (lowercase)
                                if (value is PyFloat pyFloatE)
                                    replacement = pyFloatE.Value.ToString("e", CultureInfo.InvariantCulture);
                                else if (value is PyInt pyIntE)
                                    replacement = ((double)pyIntE.Value).ToString("e", CultureInfo.InvariantCulture);
                                else
                                    replacement = value.ToStr().Value;
                                break;
                            case 'E': // Exponent format (uppercase)
                                if (value is PyFloat pyFloatEUpper)
                                    replacement = pyFloatEUpper.Value.ToString("E", CultureInfo.InvariantCulture);
                                else if (value is PyInt pyIntEUpper)
                                    replacement = ((double)pyIntEUpper.Value).ToString("E", CultureInfo.InvariantCulture);
                                else
                                    replacement = value.ToStr().Value;
                                break;
                            default:
                                throw PyValueError.Create($"unsupported format character '{formatChar}' (0x{(int)formatChar:x}) at index {i + 1}");
                        }

                        // Replace %X with the value
                        result = result.Substring(0, i) + replacement + result.Substring(i + 2);
                        i += replacement.Length - 1; // Adjust index after replacement
                    }
                }

                if (valueIndex < values.Length)
                {
                    throw PyTypeError.Create("not all arguments converted during string formatting");
                }

                return new PyString(result);
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw PyTypeError.Create($"unsupported operand type(s) for %: 'str' and '{other.GetTypeName()}': {ex.Message}");
            }
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
                var (start, stop, step) = slice.Indices(Value.Length);
                
                var chars = new List<char>();
                if (step > 0)
                {
                    for (int i = start; i < stop; i += step)
                    {
                        if (i >= 0 && i < Value.Length)
                            chars.Add(Value[i]);
                    }
                }
                else if (step < 0)
                {
                    for (int i = start; i > stop; i += step)
                    {
                        if (i >= 0 && i < Value.Length)
                            chars.Add(Value[i]);
                    }
                }
                
                return new PyString(new string(chars.ToArray()));
            }
            else
            {
                throw PyTypeError.Create($"string indices must be integers or slices, not {index.GetTypeName()}");
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

        public PyString Upper() => new PyString(Value.ToUpper());
        public PyString Lower() => new PyString(Value.ToLower());
        public PyString Capitalize() => new PyString(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Value.ToLower()));
        public PyString Title() => Capitalize(); // 간단한 구현
        
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
            if (iterable is PyList list)
            {
                // Performance: Eliminated LINQ (.Select) - manual string extraction + Cache
                var items = new string[list.Items.Length];
                for (int i = 0; i < list.Items.Length; i++)
                {
                    if (list.Items[i] is PyString str)
                        items[i] = str.Value;
                    else
                        throw PyTypeError.Create($"sequence item: expected str instance, {list.Items[i].GetTypeName()} found");
                }
                return StringCache.GetOrCreate(string.Join(Value, items));
            }

            throw PyTypeError.Create($"can only join an iterable");
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

        public PyBool IsUpper()
        {
            bool hasLetter = false;
            for (int i = 0; i < Value.Length; i++)
            {
                if (char.IsLetter(Value[i]))
                {
                    hasLetter = true;
                    if (!char.IsUpper(Value[i]))
                        return PyBool.False;
                }
            }
            return PyBool.FromBool(hasLetter);
        }

        public PyBool IsLower()
        {
            bool hasLetter = false;
            for (int i = 0; i < Value.Length; i++)
            {
                if (char.IsLetter(Value[i]))
                {
                    hasLetter = true;
                    if (!char.IsLower(Value[i]))
                        return PyBool.False;
                }
            }
            return PyBool.FromBool(hasLetter);
        }

        public PyBool IsTitle() => PyBool.FromBool(Value == Capitalize().Value);

        #endregion

        #region Length and Contains

        public override int Length() => Value.Length;
        
        public PyBool Contains(string substring) => PyBool.FromBool(Value.Contains(substring));
        
        /// <summary>
        /// CPython __contains__ 메소드 구현 - PyObject 버전
        /// </summary>
        public override PyBool Contains(PyObject item)
        {
            if (item is PyString pyStr)
            {
                return PyBool.FromBool(Value.Contains(pyStr.Value));
            }
            
            // 다른 타입은 문자열로 변환하여 검사
            var itemStr = item.ToStr();
            return PyBool.FromBool(Value.Contains(itemStr.Value));
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
        public override double ToFloat()
        {
            if (double.TryParse(Value.Trim(), out double result))
                return result;
            throw PyValueError.Create($"could not convert string to float: '{Value}'");
        }
        
        // === As* Methods: Type Conversion (PyString → PyObject types) ===
        
        /// <summary>
        /// C# 네이티브 타입 변환: PyString → C# string
        /// </summary>
        public override string AsString() => Value;
        
        /// <summary>
        /// CPython 호환: PyString을 PyInt로 변환
        /// </summary>
        public override PyInt AsInt()
        {
            return new PyInt(ToInt());
        }
        
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

        #region Encoding (Simplified)

        /// <summary>
        /// str.encode() - UTF-8로 인코딩 (간단한 구현)
        /// </summary>
        public PyBytes Encode(string encoding = "utf-8")
        {
            try
            {
                byte[] bytes;
                var lowerEncoding = encoding.ToLower();
                if (lowerEncoding == "utf-8" || lowerEncoding == "utf8")
                    bytes = Encoding.UTF8.GetBytes(Value);
                else if (lowerEncoding == "ascii")
                    bytes = Encoding.ASCII.GetBytes(Value);
                else if (lowerEncoding == "unicode")
                    bytes = Encoding.Unicode.GetBytes(Value);
                else
                    throw PyLookupError.Create($"unknown encoding: {encoding}");
                return new PyBytes(bytes);
            }
            catch (Exception)
            {
                throw PyValueError.Create($"'{encoding}' codec can't encode characters");
            }
        }

        #endregion

        #region Static Factory Methods

        public static PyString FromBytes(byte[] bytes, string encoding = "utf-8")
        {
            try
            {
                string result;
                var lowerEncoding = encoding.ToLower();
                if (lowerEncoding == "utf-8" || lowerEncoding == "utf8")
                    result = Encoding.UTF8.GetString(bytes);
                else if (lowerEncoding == "ascii")
                    result = Encoding.ASCII.GetString(bytes);
                else if (lowerEncoding == "unicode")
                    result = Encoding.Unicode.GetString(bytes);
                else
                    throw PyLookupError.Create($"unknown encoding: {encoding}");
                return new PyString(result);
            }
            catch (Exception)
            {
                throw PyValueError.Create($"'{encoding}' codec can't decode bytes");
            }
        }

        #endregion

        #region Constants

        public static readonly PyString Empty = new PyString("");

        #endregion

        #region String Methods

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "upper" => new PyStringMethod(this, "upper", Upper),
                "lower" => new PyStringMethod(this, "lower", Lower),
                "title" => new PyStringMethod(this, "title", TitleMethod),
                "strip" => new PyStringMethod(this, "strip", Strip),
                "lstrip" => new PyStringMethod(this, "lstrip", LStrip),
                "rstrip" => new PyStringMethod(this, "rstrip", RStrip),
                "replace" => new PyStringMethod(this, "replace", Replace),
                "split" => new PyStringMethod(this, "split", Split),
                "join" => new PyStringMethod(this, "join", Join),
                "startswith" => new PyStringMethod(this, "startswith", StartsWith),
                "endswith" => new PyStringMethod(this, "endswith", EndsWith),
                "find" => new PyStringMethod(this, "find", Find),
                "count" => new PyStringMethod(this, "count", Count),
                "encode" => new PyStringMethod(this, "encode", EncodeMethod),
                "isidentifier" => new PyStringMethod(this, "isidentifier", IsIdentifier),
                _ => base.GetAttribute(name)
            };
        }

        private PyObject Upper(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"upper() takes no arguments ({args.Length} given)");
            return new PyString(Value.ToUpperInvariant());
        }

        private PyObject Lower(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"lower() takes no arguments ({args.Length} given)");
            return new PyString(Value.ToLowerInvariant());
        }

        private PyObject TitleMethod(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"title() takes no arguments ({args.Length} given)");
            return Title();
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
            if (args.Length != 1)
                throw PyTypeError.Create($"join() takes exactly one argument ({args.Length} given)");

            var iterable = args[0];
            var items = new System.Collections.Generic.List<string>();

            // CPython 3.12: Support all iterables (list, tuple, generator, etc.)
            if (iterable is PyList list)
            {
                foreach (var item in list.Items)
                {
                    if (item is PyString str)
                        items.Add(str.Value);
                    else
                        throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                }
            }
            else if (iterable is PyTuple tuple)
            {
                foreach (var item in tuple.Items)
                {
                    if (item is PyString str)
                        items.Add(str.Value);
                    else
                        throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                }
            }
            else if (iterable is PyGenerator generator)
            {
                // CPython 3.12: Consume generator expression
                while (true)
                {
                    try
                    {
                        var item = generator.Next();
                        if (item is PyString str)
                            items.Add(str.Value);
                        else
                            throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        break;
                    }
                }
            }
            else
            {
                // CPython 3.12: Try to iterate using __iter__
                try
                {
                    var iterMethod = iterable.GetAttribute("__iter__");
                    if (iterMethod != null && iterMethod != PyNone.Instance)
                    {
                        var iterator = iterMethod.Call(new PyObject[0], null);
                        var nextMethod = iterator.GetAttribute("__next__");

                        while (true)
                        {
                            try
                            {
                                var item = nextMethod.Call(new PyObject[0], null);
                                if (item is PyString str)
                                    items.Add(str.Value);
                                else
                                    throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                            }
                            catch (PythonException ex) when (ex.PyException is PyStopIteration)
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        throw PyTypeError.Create("can only join an iterable");
                    }
                }
                catch (PythonException ex) when (ex.PyException is not PyStopIteration)
                {
                    throw PyTypeError.Create("can only join an iterable");
                }
                catch (Exception)
                {
                    throw PyTypeError.Create("can only join an iterable");
                }
            }

            return new PyString(string.Join(Value, items));
        }

        private PyObject StartsWith(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"startswith() takes exactly one argument ({args.Length} given)");
            
            var prefix = args[0] switch
            {
                PyString str => str.Value,
                _ => throw PyTypeError.Create("startswith first arg must be str")
            };
            
            return PyBool.FromBool(Value.StartsWith(prefix));
        }

        private PyObject EndsWith(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"endswith() takes exactly one argument ({args.Length} given)");
            
            var suffix = args[0] switch
            {
                PyString str => str.Value,
                _ => throw PyTypeError.Create("endswith first arg must be str")
            };
            
            return PyBool.FromBool(Value.EndsWith(suffix));
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
            if (args.Length >= 2 && args[1] is PyInt startInt)
            {
                start = (int)startInt.Value;
                if (start < 0) start += Value.Length;
                start = Math.Max(0, Math.Min(Value.Length, start));
            }

            if (args.Length >= 3 && args[2] is PyInt endInt)
            {
                end = (int)endInt.Value;
                if (end < 0) end += Value.Length;
                end = Math.Max(0, Math.Min(Value.Length, end));
            }

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
            if (args.Length >= 2 && args[1] is PyInt startInt)
            {
                start = (int)startInt.Value;
                if (start < 0) start += Value.Length;
                start = Math.Max(0, Math.Min(Value.Length, start));
            }

            if (args.Length >= 3 && args[2] is PyInt endInt)
            {
                end = (int)endInt.Value;
                if (end < 0) end += Value.Length;
                end = Math.Max(0, Math.Min(Value.Length, end));
            }

            if (start >= end || sub.Length == 0) return new PyInt(0);
            
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
                throw PyTypeError.Create($"isidentifier() takes no arguments ({args.Length} given)");

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
    }
}