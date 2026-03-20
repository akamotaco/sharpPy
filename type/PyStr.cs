using System;
using System.Text;
using System.Globalization;

namespace SharpPy
{
    /// <summary>
    /// Python str 타입 구현 - C# string을 기반으로 한 문자열
    /// </summary>
    public class PyStr : PyObject
    {
        // static constructor moved to Core Properties region (initializes _smallIntStrings + descriptors)

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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'join' requires a 'str' object but received a '{self.GetTypeName()}'");

                    // Use private Join method implementation
                    var iterable = args[0];
                    var items = new System.Collections.Generic.List<string>();

                    if (iterable is PyList list)
                    {
                        foreach (var item in list.Items)
                        {
                            if (item is PyStr itemStr)
                                items.Add(itemStr.Value);
                            else
                                throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                        }
                    }
                    else if (iterable is PyTuple tuple)
                    {
                        foreach (var item in tuple.Items)
                        {
                            if (item is PyStr itemStr)
                                items.Add(itemStr.Value);
                            else
                                throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                        }
                    }
                    else
                    {
                        throw PyTypeError.Create("can only join an iterable");
                    }

                    return new PyStr(string.Join(str.Value, items));
                },
                minArgs: 1, maxArgs: 1
            );

            // 기타 주요 메서드들도 등록
            var upperDesc = new PyMethodDescriptor(
                "upper", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"upper() takes no arguments ({args.Length} given)");
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'upper' requires a 'str' object but received a '{self.GetTypeName()}'");
                    return new PyStr(str.Value.ToUpperInvariant());
                },
                minArgs: 0, maxArgs: 0
            );
            upperDesc._fastCall0 = self => ((PyStr)self).Upper();
            strType.TypeDict["upper"] = upperDesc;

            var lowerDesc = new PyMethodDescriptor(
                "lower", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"lower() takes no arguments ({args.Length} given)");
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'lower' requires a 'str' object but received a '{self.GetTypeName()}'");
                    return new PyStr(str.Value.ToLowerInvariant());
                },
                minArgs: 0, maxArgs: 0
            );
            lowerDesc._fastCall0 = self => ((PyStr)self).Lower();
            strType.TypeDict["lower"] = lowerDesc;

            // CPython 3.12: Objects/unicodeobject.c:10607-10614 - unicode_capitalize_impl
            // Return a capitalized version of the string.
            // More specifically, make the first character have upper case and the rest lower case.
            strType.TypeDict["capitalize"] = new PyMethodDescriptor(
                "capitalize", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"capitalize() takes no arguments ({args.Length} given)");
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'capitalize' requires a 'str' object but received a '{self.GetTypeName()}'");

                    // Empty string remains empty
                    if (str.Value.Length == 0)
                        return str;

                    // CPython 3.12: Objects/unicodeobject.c:9575-9596 - do_capitalize
                    // First character to title case (upper for most chars), rest to lower case
                    var textInfo = CultureInfo.InvariantCulture.TextInfo;
                    return new PyStr(
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'title' requires a 'str' object but received a '{self.GetTypeName()}'");

                    // Python's title() converts to titlecase (first char of each word uppercase)
                    var textInfo = System.Globalization.CultureInfo.InvariantCulture.TextInfo;
                    return new PyStr(textInfo.ToTitleCase(str.Value.ToLowerInvariant()));
                },
                minArgs: 0, maxArgs: 0
            );

            strType.TypeDict["split"] = new PyMethodDescriptor(
                "split", strType,
                (self, args, kwargs) => {
                    if (args.Length > 2)
                        throw PyTypeError.Create($"split() takes at most 2 arguments ({args.Length} given)");
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'split' requires a 'str' object but received a '{self.GetTypeName()}'");

                    string sep = null;
                    int maxsplit = -1;
                    if (args.Length >= 1 && args[0] is PyStr sepStr)
                        sep = sepStr.Value;
                    if (args.Length >= 2 && args[1] is PyInt maxsplitInt)
                        maxsplit = (int)maxsplitInt.Value;

                    return str.Split(sep, maxsplit);
                },
                minArgs: 0, maxArgs: 2
            );

            var stripDesc = new PyMethodDescriptor(
                "strip", strType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create($"strip() takes at most 1 argument ({args.Length} given)");
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'strip' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args.Length == 0)
                        return new PyStr(str.Value.Trim());

                    var chars = args[0] is PyStr charsStr ? charsStr.Value.ToCharArray() : throw PyTypeError.Create("strip arg must be None or str");
                    return new PyStr(str.Value.Trim(chars));
                },
                minArgs: 0, maxArgs: 1
            );
            stripDesc._fastCall0 = self => ((PyStr)self).StripCached();
            strType.TypeDict["strip"] = stripDesc;

            strType.TypeDict["replace"] = new PyMethodDescriptor(
                "replace", strType,
                (self, args, kwargs) => {
                    if (args.Length < 2 || args.Length > 3)
                        throw PyTypeError.Create($"replace() takes 2 or 3 arguments ({args.Length} given)");
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'replace' requires a 'str' object but received a '{self.GetTypeName()}'");

                    var old = args[0] is PyStr oldStr ? oldStr.Value : throw PyTypeError.Create("replace() old must be str");
                    var newStr = args[1] is PyStr newPyStr ? newPyStr.Value : throw PyTypeError.Create("replace() new must be str");

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
                    if (self is not PyStr str)
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
                        return new PyStr(value[0] + new string('0', fillCount) + value.Substring(1));
                    }
                    else
                    {
                        // Just prepend zeros
                        return new PyStr(new string('0', fillCount) + value);
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
                    if (self is not PyStr str)
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
                    if (self is not PyStr str)
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
                    if (self is not PyStr str)
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
                    if (self is not PyStr str)
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
                    if (self is not PyStr str)
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
                    if (self is not PyStr str)
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'find' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyStr sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    int start = 0;
                    int end = str.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

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
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"index() takes from 1 to 3 positional arguments but {args.Length} were given");
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'index' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyStr sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    int start = 0;
                    int end = str.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'rfind' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyStr sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    int start = 0;
                    int end = str.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'rindex' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyStr sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    int start = 0;
                    int end = str.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

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
            strType.TypeDict["count"] = new PyMethodDescriptor(
                "count", strType,
                (self, args, kwargs) => {
                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"count() takes from 1 to 3 positional arguments but {args.Length} were given");
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'count' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyStr sub)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    if (sub.Value.Length == 0)
                        return new PyInt(str.Value.Length + 1);

                    int start = 0;
                    int end = str.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'startswith' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyStr prefix)
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'endswith' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyStr suffix)
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'lstrip' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args.Length == 0)
                        return new PyStr(str.Value.TrimStart());

                    if (args[0] is not PyStr chars)
                        throw PyTypeError.Create("lstrip arg must be None or str");

                    return new PyStr(str.Value.TrimStart(chars.Value.ToCharArray()));
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'rstrip' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args.Length == 0)
                        return new PyStr(str.Value.TrimEnd());

                    if (args[0] is not PyStr chars)
                        throw PyTypeError.Create("rstrip arg must be None or str");

                    return new PyStr(str.Value.TrimEnd(chars.Value.ToCharArray()));
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'rsplit' requires a 'str' object but received a '{self.GetTypeName()}'");

                    string sep = null;
                    int maxsplit = -1;

                    if (args.Length >= 1 && args[0] is not PyNone)
                    {
                        if (args[0] is not PyStr sepStr)
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
                        items[i] = new PyStr(parts[i]);

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
                    if (self is not PyStr str)
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
                            items.Add(new PyStr(line));
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
                    if (self is not PyStr str)
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
                    return new PyStr(result.ToString());
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'casefold' requires a 'str' object but received a '{self.GetTypeName()}'");

                    return new PyStr(str.Value.ToLowerInvariant());
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'center' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyInt widthInt)
                        throw PyTypeError.Create("center() width must be an integer");

                    int width = (int)widthInt.Value;
                    char fillchar = ' ';

                    if (args.Length >= 2)
                    {
                        if (args[1] is not PyStr fillStr || fillStr.Value.Length != 1)
                            throw PyTypeError.Create("center() fillchar must be a single character");
                        fillchar = fillStr.Value[0];
                    }

                    if (str.Value.Length >= width)
                        return str;

                    int totalPad = width - str.Value.Length;
                    int leftPad = totalPad / 2;
                    int rightPad = totalPad - leftPad;

                    return new PyStr(new string(fillchar, leftPad) + str.Value + new string(fillchar, rightPad));
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'ljust' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyInt widthInt)
                        throw PyTypeError.Create("ljust() width must be an integer");

                    int width = (int)widthInt.Value;
                    char fillchar = ' ';

                    if (args.Length >= 2)
                    {
                        if (args[1] is not PyStr fillStr || fillStr.Value.Length != 1)
                            throw PyTypeError.Create("ljust() fillchar must be a single character");
                        fillchar = fillStr.Value[0];
                    }

                    if (str.Value.Length >= width)
                        return str;

                    return new PyStr(str.Value + new string(fillchar, width - str.Value.Length));
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'rjust' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyInt widthInt)
                        throw PyTypeError.Create("rjust() width must be an integer");

                    int width = (int)widthInt.Value;
                    char fillchar = ' ';

                    if (args.Length >= 2)
                    {
                        if (args[1] is not PyStr fillStr || fillStr.Value.Length != 1)
                            throw PyTypeError.Create("rjust() fillchar must be a single character");
                        fillchar = fillStr.Value[0];
                    }

                    if (str.Value.Length >= width)
                        return str;

                    return new PyStr(new string(fillchar, width - str.Value.Length) + str.Value);
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
                    if (self is not PyStr str)
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
                    if (self is not PyStr str)
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
                    if (self is not PyStr str)
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
                    if (self is not PyStr str)
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
                    if (self is not PyStr str)
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
                    if (self is not PyStr str)
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'partition' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyStr sep)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    if (sep.Value.Length == 0)
                        throw PyValueError.Create("empty separator");

                    int index = str.Value.IndexOf(sep.Value);
                    if (index == -1)
                        return new PyTuple(new PyObject[] { str, new PyStr(""), new PyStr("") });

                    return new PyTuple(new PyObject[] {
                        new PyStr(str.Value.Substring(0, index)),
                        sep,
                        new PyStr(str.Value.Substring(index + sep.Value.Length))
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'rpartition' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyStr sep)
                        throw PyTypeError.Create("must be str, not " + args[0].GetTypeName());

                    if (sep.Value.Length == 0)
                        throw PyValueError.Create("empty separator");

                    int index = str.Value.LastIndexOf(sep.Value);
                    if (index == -1)
                        return new PyTuple(new PyObject[] { new PyStr(""), new PyStr(""), str });

                    return new PyTuple(new PyObject[] {
                        new PyStr(str.Value.Substring(0, index)),
                        sep,
                        new PyStr(str.Value.Substring(index + sep.Value.Length))
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'removeprefix' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyStr prefix)
                        throw PyTypeError.Create("prefix must be str, not " + args[0].GetTypeName());

                    if (str.Value.StartsWith(prefix.Value))
                        return new PyStr(str.Value.Substring(prefix.Value.Length));

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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'removesuffix' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyStr suffix)
                        throw PyTypeError.Create("suffix must be str, not " + args[0].GetTypeName());

                    if (str.Value.EndsWith(suffix.Value))
                        return new PyStr(str.Value.Substring(0, str.Value.Length - suffix.Value.Length));

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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'expandtabs' requires a 'str' object but received a '{self.GetTypeName()}'");

                    int tabsize = 8;
                    if (args.Length >= 1 && args[0] is PyInt tabInt)
                        tabsize = (int)tabInt.Value;

                    if (tabsize <= 0)
                        return new PyStr(str.Value.Replace("\t", ""));

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

                    return new PyStr(result.ToString());
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
                    if (self is not PyStr str)
                        throw PyTypeError.Create($"descriptor 'encode' requires a 'str' object but received a '{self.GetTypeName()}'");

                    string encoding = "utf-8";
                    string errors = "strict";

                    if (args.Length >= 1)
                    {
                        if (args[0] is PyStr encodingStr)
                            encoding = encodingStr.Value.ToLowerInvariant();
                        else
                            throw PyTypeError.Create("encode() encoding must be str");
                    }

                    if (args.Length >= 2)
                    {
                        if (args[1] is PyStr errorsStr)
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
                    if (self is not PyStr str)
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

                            // CPython 3.12: PEP 3101 - parse field as "field_name!conversion:format_spec"
                            // For now, we support: {0:04x}, {name:format}, etc.
                            string fieldName = field;
                            string formatSpec = null;
                            string conversion = null;

                            // Parse conversion (!r, !s, !a)
                            int conversionIdx = field.IndexOf('!');
                            if (conversionIdx != -1)
                            {
                                fieldName = field.Substring(0, conversionIdx);
                                int formatSpecIdx = field.IndexOf(':', conversionIdx);
                                if (formatSpecIdx != -1)
                                {
                                    conversion = field.Substring(conversionIdx + 1, formatSpecIdx - conversionIdx - 1);
                                    formatSpec = field.Substring(formatSpecIdx + 1);
                                }
                                else
                                {
                                    conversion = field.Substring(conversionIdx + 1);
                                }
                            }
                            else
                            {
                                // Parse format spec
                                int formatSpecIdx = field.IndexOf(':');
                                if (formatSpecIdx != -1)
                                {
                                    fieldName = field.Substring(0, formatSpecIdx);
                                    formatSpec = field.Substring(formatSpecIdx + 1);
                                }
                            }

                            // Get the value to format
                            PyObject value = null;
                            if (string.IsNullOrEmpty(fieldName))
                            {
                                if (argIndex >= args.Length)
                                    throw PyIndexError.Create("Replacement index out of range");
                                value = args[argIndex];
                                argIndex++;
                            }
                            else if (int.TryParse(fieldName, out int index))
                            {
                                if (index >= args.Length)
                                    throw PyIndexError.Create("Replacement index out of range");
                                value = args[index];
                            }
                            else
                            {
                                // Named argument - requires kwargs
                                if (kwargs != null && kwargs.Contains(new PyStr(fieldName)).Value)
                                {
                                    value = kwargs.GetItem(new PyStr(fieldName));
                                }
                                else
                                {
                                    throw PyKeyError.Create(fieldName);
                                }
                            }

                            // Apply conversion
                            if (conversion != null)
                            {
                                value = conversion switch
                                {
                                    "s" => value.ToStr(),
                                    "r" => value.ToRepr(),
                                    "a" => value.ToRepr(), // ASCII version (simplified)
                                    _ => throw PyValueError.Create($"Unknown conversion specifier {conversion}")
                                };
                            }

                            // Apply format spec
                            string formattedValue;
                            if (formatSpec != null)
                            {
                                formattedValue = FormatValue(value, formatSpec);
                            }
                            else
                            {
                                formattedValue = value.ToStr().Value;
                            }

                            result.Append(formattedValue);

                            i_pos = closeBrace + 1;
                        }

                        return new PyStr(result.ToString());
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
                    if (self is not PyStr str)
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

                            if (key is PyStr keyStr)
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
                        if (args[0] is not PyStr x || args[1] is not PyStr y)
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
                            if (args[2] is not PyStr z)
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
                    if (self is not PyStr str)
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
                            else if (value is PyStr strValue)
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

                    return new PyStr(result.ToString());
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
                    if (self is not PyStr strObj)
                        throw PyTypeError.Create($"descriptor '__format__' requires a 'str' object but received a '{self.GetTypeName()}'");
                    if (args[0] is not PyStr specStr)
                        throw PyTypeError.Create($"__format__() argument 1 must be str, not {args[0].GetTypeName()}");

                    return new PyStr(FormatString(strObj.Value, specStr.Value));
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

                    // Get the value argument (args[1] if present)
                    string value = "";
                    if (args.Length >= 2 && args[1] != PyNone.Instance)
                    {
                        // CPython: Line 14720 - unicode = PyObject_Str(x)
                        var strObj = args[1].ToStr();
                        value = strObj.Value;
                    }

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
                        // Regular str type, return PyStr
                        return new PyStr(value);
                    }
                }
            );

            // === CPython 3.12 dunder method descriptors ===
            // isinstance(str_instance, Sequence) 등 ABC 판정에 필요

            strType.TypeDict["__getitem__"] = new PyMethodDescriptor(
                "__getitem__", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1) throw PyTypeError.Create("__getitem__() takes exactly 1 argument");
                    return ((PyStr)self).GetItem(args[0]);
                }, minArgs: 1, maxArgs: 1);

            strType.TypeDict["__len__"] = new PyMethodDescriptor(
                "__len__", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0) throw PyTypeError.Create("__len__() takes no arguments");
                    return new PyInt(((PyStr)self).Length());
                }, minArgs: 0, maxArgs: 0);

            strType.TypeDict["__contains__"] = new PyMethodDescriptor(
                "__contains__", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1) throw PyTypeError.Create("__contains__() takes exactly 1 argument");
                    return ((PyStr)self).Contains(args[0]);
                }, minArgs: 1, maxArgs: 1);

            strType.TypeDict["__iter__"] = new PyMethodDescriptor(
                "__iter__", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0) throw PyTypeError.Create("__iter__() takes no arguments");
                    return ((PyStr)self).GetIterator();
                }, minArgs: 0, maxArgs: 0);
            // str has no __reversed__ in CPython
        }

        #region Core Properties

        public string Value { get; }

        public PyStr(string value) => Value = value ?? "";

        // Per-instance result cache for upper/lower/strip — avoids new PyStr on repeated calls.
        // CPython 3.12: strings are immutable, so cached results are always valid.
        private PyStr _cachedUpper;
        private PyStr _cachedLower;
        private PyStr _cachedStrip;

        internal PyStr Upper()
        {
            if (_cachedUpper != null) return _cachedUpper;
            var u = Value.ToUpperInvariant();
            _cachedUpper = u == Value ? this : new PyStr(u);
            return _cachedUpper;
        }

        internal PyStr Lower()
        {
            if (_cachedLower != null) return _cachedLower;
            var l = Value.ToLowerInvariant();
            _cachedLower = l == Value ? this : new PyStr(l);
            return _cachedLower;
        }

        internal PyStr StripCached()
        {
            if (_cachedStrip != null) return _cachedStrip;
            var s = Value.Trim();
            _cachedStrip = s == Value ? this : new PyStr(s);
            return _cachedStrip;
        }

        // Static cache for str(0) through str(255) — SmallIntCache pattern.
        // CPython 3.12: _PyUnicode_FromId caches commonly used strings.
        private static readonly PyStr[] _smallIntStrings;
        static PyStr()
        {
            _smallIntStrings = new PyStr[256];
            for (int i = 0; i < 256; i++)
                _smallIntStrings[i] = new PyStr(i.ToString());
            InitializeStringDescriptors();
        }

        /// <summary>
        /// Get cached PyStr for small int values (0-255), or create new one.
        /// </summary>
        internal static PyStr FromInt(long value)
        {
            if ((ulong)value < 256)
                return _smallIntStrings[value];
            return new PyStr(value.ToString());
        }

        public override PyType GetPyType() => PyType.StrType;
        public override string GetTypeName() => "str";

        // CPython 3.12: Objects/unicodeobject.c - unicode_bool
        // Optimized: Direct length check, no MRO traversal
        public override bool IsTrue() => Value.Length > 0;

        #endregion

        #region String Representation

        // Python str() - PyStr은 이미 문자열이므로 자신을 반환
        public override PyStr ToStr() => this;

        public override PyStr ToRepr()
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
            return new PyStr(result.ToString());
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
                PyStr otherStr => PyBool.FromBool(Value == otherStr.Value),
                _ => PyBool.False
            };
        }

        #endregion

        #region Comparison Operations

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                PyStr otherStr => PyBool.FromBool(string.Compare(Value, otherStr.Value, StringComparison.Ordinal) < 0),
                _ => throw PyTypeError.Create($"'<' not supported between instances of 'str' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                PyStr otherStr => PyBool.FromBool(string.Compare(Value, otherStr.Value, StringComparison.Ordinal) <= 0),
                _ => throw PyTypeError.Create($"'<=' not supported between instances of 'str' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                PyStr otherStr => PyBool.FromBool(string.Compare(Value, otherStr.Value, StringComparison.Ordinal) > 0),
                _ => throw PyTypeError.Create($"'>' not supported between instances of 'str' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                PyStr otherStr => PyBool.FromBool(string.Compare(Value, otherStr.Value, StringComparison.Ordinal) >= 0),
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
            if (other is not PyStr otherStr)
                return PyNotImplemented.Instance;

            return new PyStr(Value + otherStr.Value);
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
                return new PyStr("");

            // Performance: Eliminated LINQ (Enumerable.Repeat) - manual string repetition
            var sb = new StringBuilder(Value.Length * (int)count.Value);
            for (int i = 0; i < count.Value; i++)
            {
                sb.Append(Value);
            }
            return new PyStr(sb.ToString());
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
                // % formatting with precision support - CPython 3.12 compatible
                // CPython: Objects/unicodeobject.c:unicode_format_arg (lines 495-1046)
                var result = Value;
                int valueIndex = 0;

                for (int i = 0; i < result.Length; i++)
                {
                    if (result[i] == '%' && i + 1 < result.Length)
                    {
                        // Parse format specifier: %[flags][width][.precision]type
                        // CPython: Objects/unicodeobject.c:unicode_format_arg (lines 548-663)
                        int pos = i + 1;

                        // Skip flags (+, -, 0, space, #)
                        while (pos < result.Length && "+-0 #".Contains(result[pos]))
                            pos++;

                        // Skip width (digits or *)
                        while (pos < result.Length && char.IsDigit(result[pos]))
                            pos++;
                        if (pos < result.Length && result[pos] == '*')
                            pos++;

                        // Parse precision (.digits or .*)
                        int? precision = null;
                        if (pos < result.Length && result[pos] == '.')
                        {
                            pos++; // Skip '.'
                            if (pos < result.Length && result[pos] == '*')
                            {
                                pos++; // Skip '*' (dynamic precision - not implemented yet)
                            }
                            else
                            {
                                int precStart = pos;
                                while (pos < result.Length && char.IsDigit(result[pos]))
                                    pos++;
                                if (pos > precStart)
                                    precision = int.Parse(result.Substring(precStart, pos - precStart));
                            }
                        }

                        if (pos >= result.Length)
                            throw PyValueError.Create("incomplete format");

                        char formatChar = result[pos];
                        int formatLen = pos - i + 1; // Length of entire format specifier

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

                        // Get precision value
                        int precValue = precision ?? 6;

                        switch (formatChar)
                        {
                            case 's': // String
                                replacement = value is PyStr str ? str.Value : value.ToStr().Value;
                                break;
                            case 'r': // Repr
                                replacement = value.ToRepr().Value;
                                break;
                            case 'd': // Decimal integer
                            case 'i': // Integer
                                replacement = value is PyInt pyInt ? pyInt.Value.ToString() : value.ToStr().Value;
                                break;
                            case 'f': // Float
                                // CPython: Objects/unicodeobject.c:formatfloat (lines 278-365)
                                // Default precision is 6 for %f
                                if (value is PyFloat pyFloat)
                                    replacement = pyFloat.Value.ToString($"F{precValue}", CultureInfo.InvariantCulture);
                                else if (value is PyInt pyIntForFloat)
                                    replacement = ((double)pyIntForFloat.Value).ToString($"F{precValue}", CultureInfo.InvariantCulture);
                                else
                                    replacement = value.ToStr().Value;
                                break;
                            case 'g': // General format (lowercase) - Default 6 significant digits
                                if (value is PyFloat pyFloatG)
                                    replacement = pyFloatG.Value.ToString($"g{precValue}", CultureInfo.InvariantCulture);
                                else if (value is PyInt pyIntG)
                                    replacement = ((double)pyIntG.Value).ToString($"g{precValue}", CultureInfo.InvariantCulture);
                                else
                                    replacement = value.ToStr().Value;
                                break;
                            case 'G': // General format (uppercase) - Default 6 significant digits
                                if (value is PyFloat pyFloatGUpper)
                                    replacement = pyFloatGUpper.Value.ToString($"G{precValue}", CultureInfo.InvariantCulture);
                                else if (value is PyInt pyIntGUpper)
                                    replacement = ((double)pyIntGUpper.Value).ToString($"G{precValue}", CultureInfo.InvariantCulture);
                                else
                                    replacement = value.ToStr().Value;
                                break;
                            case 'e': // Exponent format (lowercase) - Default 6 decimal places
                                if (value is PyFloat pyFloatE)
                                    replacement = FormatExponent(pyFloatE.Value, precValue, false);
                                else if (value is PyInt pyIntE)
                                    replacement = FormatExponent((double)pyIntE.Value, precValue, false);
                                else
                                    replacement = value.ToStr().Value;
                                break;
                            case 'E': // Exponent format (uppercase) - Default 6 decimal places
                                if (value is PyFloat pyFloatEUpper)
                                    replacement = FormatExponent(pyFloatEUpper.Value, precValue, true);
                                else if (value is PyInt pyIntEUpper)
                                    replacement = FormatExponent((double)pyIntEUpper.Value, precValue, true);
                                else
                                    replacement = value.ToStr().Value;
                                break;
                            default:
                                throw PyValueError.Create($"unsupported format character '{formatChar}' (0x{(int)formatChar:x}) at index {pos}");
                        }

                        // Replace format specifier with the value
                        result = result.Substring(0, i) + replacement + result.Substring(i + formatLen);
                        i += replacement.Length - 1; // Adjust index after replacement
                    }
                }

                if (valueIndex < values.Length)
                {
                    throw PyTypeError.Create("not all arguments converted during string formatting");
                }

                return new PyStr(result);
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
        public PyStr GetItem(int index)
        {
            // Python식 음수 인덱스 지원
            if (index < 0) index += Value.Length;
            
            if (index < 0 || index >= Value.Length)
                throw PyIndexError.Create("string index out of range");
            
            return new PyStr(Value[index].ToString());
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
                
                return new PyStr(new string(chars.ToArray()));
            }
            else
            {
                throw PyTypeError.Create($"string indices must be integers or slices, not {index.GetTypeName()}");
            }
        }

        /// <summary>
        /// 슬라이싱 str[start:end]
        /// </summary>
        public PyStr GetSlice(int? start = null, int? end = null, int step = 1)
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
            
            return new PyStr(result.ToString());
        }

        #endregion

        #region String Methods

        // Upper()/Lower() with per-instance cache are defined in Core Properties region
        public PyStr Capitalize() => new PyStr(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Value.ToLower()));
        public PyStr Title() => Capitalize(); // 간단한 구현
        
        public PyStr Strip(string chars = null)
        {
            if (chars == null)
                return new PyStr(Value.Trim());
            return new PyStr(Value.Trim(chars.ToCharArray()));
        }
        
        public PyStr LStrip(string chars = null)
        {
            if (chars == null)
                return new PyStr(Value.TrimStart());
            return new PyStr(Value.TrimStart(chars.ToCharArray()));
        }
        
        public PyStr RStrip(string chars = null)
        {
            if (chars == null)
                return new PyStr(Value.TrimEnd());
            return new PyStr(Value.TrimEnd(chars.ToCharArray()));
        }

        public PyStr Replace(string old, string newStr, int count = -1)
        {
            if (count == -1)
                return new PyStr(Value.Replace(old, newStr));
            
            // 제한된 횟수만 바꾸기
            var result = Value;
            for (int i = 0; i < count; i++)
            {
                var index = result.IndexOf(old);
                if (index == -1) break;
                result = result.Substring(0, index) + newStr + result.Substring(index + old.Length);
            }
            return new PyStr(result);
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

        public PyStr Join(PyObject iterable)
        {
            if (iterable is PyList list)
            {
                // Performance: Eliminated LINQ (.Select) - manual string extraction + Cache
                var items = new string[list.Items.Length];
                for (int i = 0; i < list.Items.Length; i++)
                {
                    if (list.Items[i] is PyStr str)
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
            if (item is PyStr pyStr)
            {
                return PyBool.FromBool(Value.Contains(pyStr.Value));
            }
            
            // 다른 타입은 문자열로 변환하여 검사
            var itemStr = item.ToStr();
            return PyBool.FromBool(Value.Contains(itemStr.Value));
        }

        #endregion

        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyStr → C# basic types) ===
        
        /// <summary>
        /// CPython PyObject_IsTrue 호환: PyStr에서 C# bool 값 추출
        /// </summary>
        public override bool PyBoolValue() => Value.Length > 0;
        
        /// <summary>
        /// CPython PyUnicode_AsLong 호환: PyStr에서 C# int 값 추출
        /// </summary>
        public override int ToInt()
        {
            if (int.TryParse(Value.Trim(), out int result))
                return result;
            throw PyValueError.Create($"invalid literal for int() with base 10: '{Value}'");
        }
        
        /// <summary>
        /// CPython PyUnicode_AsDouble 호환: PyStr에서 C# float (32-bit) 값 추출
        /// </summary>
        // CPython 3.12: Objects/floatobject.c:165-202 (float_from_string_inner)
        // CPython 3.12: Python/pystrtod.c:27-57 (_Py_parse_inf_or_nan)
        public override float ToFloat()
        {
            var trimmed = Value.Trim();

            if (trimmed.Length > 0)
            {
                bool negate = false;
                string s = trimmed;

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
                        return negate ? float.NegativeInfinity : float.PositiveInfinity;
                    }
                    // Just "inf"
                    else if (s.Length == 3 || !char.IsLetterOrDigit(s[3]))
                    {
                        return negate ? float.NegativeInfinity : float.PositiveInfinity;
                    }
                }
                // Check for "nan" (case insensitive)
                else if (s.Length >= 3 && s.Substring(0, 3).ToLowerInvariant() == "nan")
                {
                    if (s.Length == 3 || !char.IsLetterOrDigit(s[3]))
                    {
                        return float.NaN;
                    }
                }
            }

            // Standard numeric parsing
            if (float.TryParse(trimmed, out float result))
                return result;

            throw PyValueError.Create($"could not convert string to float: '{Value}'");
        }

        public override double ToDouble()
        {
            var trimmed = Value.Trim();

            if (trimmed.Length > 0)
            {
                bool negate = false;
                string s = trimmed;

                if (s[0] == '-')
                {
                    negate = true;
                    s = s.Substring(1).TrimStart();
                }
                else if (s[0] == '+')
                {
                    s = s.Substring(1).TrimStart();
                }

                if (s.Length >= 3 && s.Substring(0, 3).ToLowerInvariant() == "inf")
                {
                    if (s.Length >= 8 && s.Substring(0, 8).ToLowerInvariant() == "infinity")
                        return negate ? double.NegativeInfinity : double.PositiveInfinity;
                    else if (s.Length == 3 || !char.IsLetterOrDigit(s[3]))
                        return negate ? double.NegativeInfinity : double.PositiveInfinity;
                }
                else if (s.Length >= 3 && s.Substring(0, 3).ToLowerInvariant() == "nan")
                {
                    if (s.Length == 3 || !char.IsLetterOrDigit(s[3]))
                        return double.NaN;
                }
            }

            if (double.TryParse(trimmed, out double result))
                return result;

            throw PyValueError.Create($"could not convert string to double: '{Value}'");
        }

        // === As* Methods: Type Conversion (PyStr → PyObject types) ===
        
        /// <summary>
        /// C# 네이티브 타입 변환: PyStr → C# string
        /// </summary>
        public override string AsString() => Value;
        
        /// <summary>
        /// CPython 호환: PyStr을 PyInt로 변환
        /// </summary>
        public override PyInt AsInt()
        {
            return new PyInt(ToInt());
        }
        
        /// <summary>
        /// CPython 호환: PyStr을 PyFloat로 변환
        /// </summary>
        public override PyFloat AsFloat()
        {
            return new PyFloat(ToFloat());
        }
        
        /// <summary>
        /// CPython 호환: PyStr을 PyBool로 변환
        /// </summary>
        public override PyBool AsBool()
        {
            return PyBool.FromBool(Value.Length > 0);
        }
        
        /// <summary>
        /// CPython 호환: PyStr을 PyList로 변환 (각 문자를 PyStr 요소로)
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

        public static PyStr FromBytes(byte[] bytes, string encoding = "utf-8")
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
                return new PyStr(result);
            }
            catch (Exception)
            {
                throw PyValueError.Create($"'{encoding}' codec can't decode bytes");
            }
        }

        #endregion

        #region Constants

        public static readonly PyStr Empty = new PyStr("");

        #endregion

        #region String Methods

        public override PyObject GetAttribute(string name)
        {
            // CPython 3.12: For methods that have descriptors in TypeDict, delegate to base
            // to use the descriptor protocol. This ensures correct behavior with start/end parameters.
            if (name == "startswith" || name == "endswith")
            {
                return base.GetAttribute(name);
            }

            return name switch
            {
                "upper" => new PyStrMethod(this, "upper", Upper),
                "lower" => new PyStrMethod(this, "lower", Lower),
                "title" => new PyStrMethod(this, "title", TitleMethod),
                "strip" => new PyStrMethod(this, "strip", Strip),
                "lstrip" => new PyStrMethod(this, "lstrip", LStrip),
                "rstrip" => new PyStrMethod(this, "rstrip", RStrip),
                "replace" => new PyStrMethod(this, "replace", Replace),
                "split" => new PyStrMethod(this, "split", Split),
                "join" => new PyStrMethod(this, "join", Join),
                "find" => new PyStrMethod(this, "find", Find),
                "count" => new PyStrMethod(this, "count", Count),
                "encode" => new PyStrMethod(this, "encode", EncodeMethod),
                "isidentifier" => new PyStrMethod(this, "isidentifier", IsIdentifier),
                _ => base.GetAttribute(name)
            };
        }

        private PyObject Upper(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"upper() takes no arguments ({args.Length} given)");
            return new PyStr(Value.ToUpperInvariant());
        }

        private PyObject Lower(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"lower() takes no arguments ({args.Length} given)");
            return new PyStr(Value.ToLowerInvariant());
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
                return new PyStr(Value.Trim());
            
            var chars = args[0] switch
            {
                PyStr str => str.Value.ToCharArray(),
                _ => throw PyTypeError.Create("strip arg must be None or str")
            };
            
            return new PyStr(Value.Trim(chars));
        }

        private PyObject LStrip(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"lstrip() takes at most 1 argument ({args.Length} given)");
            
            if (args.Length == 0)
                return new PyStr(Value.TrimStart());
            
            var chars = args[0] switch
            {
                PyStr str => str.Value.ToCharArray(),
                _ => throw PyTypeError.Create("lstrip arg must be None or str")
            };
            
            return new PyStr(Value.TrimStart(chars));
        }

        private PyObject RStrip(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"rstrip() takes at most 1 argument ({args.Length} given)");
            
            if (args.Length == 0)
                return new PyStr(Value.TrimEnd());
            
            var chars = args[0] switch
            {
                PyStr str => str.Value.ToCharArray(),
                _ => throw PyTypeError.Create("rstrip arg must be None or str")
            };
            
            return new PyStr(Value.TrimEnd(chars));
        }

        private PyObject Replace(PyObject[] args)
        {
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"replace() takes 2 or 3 arguments ({args.Length} given)");
            
            var old = args[0] switch
            {
                PyStr str => str.Value,
                _ => throw PyTypeError.Create("replace() old must be str")
            };
            
            var newStr = args[1] switch
            {
                PyStr str => str.Value,
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
                return new PyStr(result);
            }
            
            return new PyStr(Value.Replace(old, newStr));
        }

        private PyObject Split(PyObject[] args)
        {
            if (args.Length > 2)
                throw PyTypeError.Create($"split() takes at most 2 arguments ({args.Length} given)");
            
            string sep = null;
            int maxsplit = -1;
            
            if (args.Length >= 1 && args[0] is PyStr sepStr)
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
                    if (item is PyStr str)
                        items.Add(str.Value);
                    else
                        throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                }
            }
            else if (iterable is PyTuple tuple)
            {
                foreach (var item in tuple.Items)
                {
                    if (item is PyStr str)
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
                        if (item is PyStr str)
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
                                if (item is PyStr str)
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

            return new PyStr(string.Join(Value, items));
        }

        private PyObject StartsWith(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"startswith() takes exactly one argument ({args.Length} given)");
            
            var prefix = args[0] switch
            {
                PyStr str => str.Value,
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
                PyStr str => str.Value,
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
                PyStr str => str.Value,
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
                PyStr str => str.Value,
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
                if (args[0] is PyStr encodingStr)
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
            return new PyStrIterator(this);
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
        private static string FormatValue(PyObject value, string formatSpec)
        {
            if (string.IsNullOrEmpty(formatSpec))
                return value.ToStr().Value;

            // Parse format spec
            char? type = null;
            int? width = null;
            int? precision = null;
            char? align = null;
            char? fill = ' ';
            char? sign = null;
            bool alternate = false;
            bool zeroPad = false;

            int i = 0;

            // Parse fill and align (up to 2 chars)
            if (formatSpec.Length >= 2 && IsAlignChar(formatSpec[1]))
            {
                fill = formatSpec[0];
                align = formatSpec[1];
                i = 2;
            }
            else if (formatSpec.Length >= 1 && IsAlignChar(formatSpec[0]))
            {
                align = formatSpec[0];
                i = 1;
            }

            // Parse sign
            if (i < formatSpec.Length && (formatSpec[i] == '+' || formatSpec[i] == '-' || formatSpec[i] == ' '))
            {
                sign = formatSpec[i];
                i++;
            }

            // Parse #
            if (i < formatSpec.Length && formatSpec[i] == '#')
            {
                alternate = true;
                i++;
            }

            // Parse 0 (zero padding)
            if (i < formatSpec.Length && formatSpec[i] == '0')
            {
                zeroPad = true;
                if (!align.HasValue)
                {
                    align = '=';
                    fill = '0';
                }
                i++;
            }

            // Parse width
            int widthStart = i;
            while (i < formatSpec.Length && char.IsDigit(formatSpec[i]))
                i++;
            if (i > widthStart)
                width = int.Parse(formatSpec.Substring(widthStart, i - widthStart));

            // Parse comma (grouping)
            if (i < formatSpec.Length && formatSpec[i] == ',')
                i++;

            // Parse precision
            if (i < formatSpec.Length && formatSpec[i] == '.')
            {
                i++;
                int precStart = i;
                while (i < formatSpec.Length && char.IsDigit(formatSpec[i]))
                    i++;
                if (i > precStart)
                    precision = int.Parse(formatSpec.Substring(precStart, i - precStart));
            }

            // Parse type
            if (i < formatSpec.Length)
            {
                type = formatSpec[i];
                i++;
            }

            // CPython 3.12: Objects/stringlib/formatter.h:820-950 - format_int_or_long
            // Format the value based on type
            string result;
            if (value is PyInt pyInt)
            {
                result = FormatInt((long)pyInt.Value, type, width, precision, sign, alternate, fill, align);
            }
            else if (value is PyFloat pyFloat)
            {
                result = FormatFloat(pyFloat.Value, type, width, precision, sign, alternate);
            }
            else if (value is PyStr pyStr)
            {
                result = FormatString(pyStr.Value, width, precision, align, fill);
            }
            else
            {
                result = value.ToStr().Value;
                if (width.HasValue)
                    result = ApplyAlignment(result, width.Value, align ?? '<', fill ?? ' ');
            }

            return result;
        }

        private static bool IsAlignChar(char c)
        {
            return c == '<' || c == '>' || c == '=' || c == '^';
        }

        // CPython 3.12: Python/formatter_unicode.c:format_long_internal
        private static string FormatInt(long value, char? type, int? width, int? precision, char? sign, bool alternate, char? fill, char? align)
        {
            string result;
            int baseValue = 10;
            string prefix = "";

            switch (type)
            {
                case 'b': // Binary
                    baseValue = 2;
                    result = Convert.ToString(Math.Abs(value), 2);
                    if (alternate && value != 0) prefix = "0b";
                    break;
                case 'o': // Octal
                    baseValue = 8;
                    result = Convert.ToString(Math.Abs(value), 8);
                    if (alternate && value != 0) prefix = "0o";
                    break;
                case 'x': // Hex lowercase
                    result = Math.Abs(value).ToString("x");
                    if (alternate && value != 0) prefix = "0x";
                    break;
                case 'X': // Hex uppercase
                    result = Math.Abs(value).ToString("X");
                    if (alternate && value != 0) prefix = "0X";
                    break;
                case 'd':
                case 'n':
                case null: // Default is decimal
                    result = Math.Abs(value).ToString();
                    break;
                default:
                    throw PyValueError.Create($"Unknown format code '{type}' for object of type 'int'");
            }

            // Apply precision (minimum digits)
            if (precision.HasValue && result.Length < precision.Value)
                result = result.PadLeft(precision.Value, '0');

            // Add sign
            string signStr = "";
            if (value < 0)
                signStr = "-";
            else if (sign == '+')
                signStr = "+";
            else if (sign == ' ')
                signStr = " ";

            result = signStr + prefix + result;

            // Apply width and alignment
            if (width.HasValue && result.Length < width.Value)
                result = ApplyAlignment(result, width.Value, align ?? '>', fill ?? ' ');

            return result;
        }

        private static string FormatFloat(double value, char? type, int? width, int? precision, char? sign, bool alternate)
        {
            string result;
            int prec = precision ?? 6;

            switch (type)
            {
                case 'e': result = value.ToString($"e{prec}"); break;
                case 'E': result = value.ToString($"E{prec}"); break;
                case 'f':
                case 'F':
                case null:
                    result = value.ToString($"F{prec}");
                    break;
                case 'g': result = value.ToString($"g{prec}"); break;
                case 'G': result = value.ToString($"G{prec}"); break;
                case '%': result = (value * 100).ToString($"F{prec}") + "%"; break;
                default:
                    throw PyValueError.Create($"Unknown format code '{type}' for object of type 'float'");
            }

            // Apply width
            if (width.HasValue && result.Length < width.Value)
                result = result.PadLeft(width.Value);

            return result;
        }

        private static string FormatString(string value, int? width, int? precision, char? align, char? fill)
        {
            string result = value;

            // Apply precision (max length)
            if (precision.HasValue && result.Length > precision.Value)
                result = result.Substring(0, precision.Value);

            // Apply width and alignment
            if (width.HasValue && result.Length < width.Value)
                result = ApplyAlignment(result, width.Value, align ?? '<', fill ?? ' ');

            return result;
        }

        private static string ApplyAlignment(string value, int width, char align, char fill)
        {
            int padding = width - value.Length;
            if (padding <= 0)
                return value;

            return align switch
            {
                '<' => value + new string(fill, padding),  // Left align
                '>' => new string(fill, padding) + value,  // Right align
                '=' => // Sign-aware (for numbers)
                    (value.Length > 0 && (value[0] == '+' || value[0] == '-' || value[0] == ' '))
                        ? value[0] + new string(fill, padding) + value.Substring(1)
                        : new string(fill, padding) + value,
                '^' => // Center
                    new string(fill, padding / 2) + value + new string(fill, (padding + 1) / 2),
                _ => value
            };
        }

        /// <summary>
        /// CPython 3.12: Objects/unicodeobject.c - Format a string value according to format_spec
        /// Format spec mini-language: [[fill]align][width][.precision][type]
        /// Type can be: 's' (string) or '' (default, same as s)
        /// </summary>
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