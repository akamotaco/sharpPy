using System;
using System.Collections.Generic;

namespace SharpPy
{
    public class PyBytes : PyObject
    {
        static PyBytes()
        {
            InitializeBytesDescriptors();
        }

        /// <summary>
        /// Initialize bytes type descriptors (CPython 3.12 compatible)
        /// CPython reference: Objects/bytesobject.c:2400-2600 - bytes_methods
        /// </summary>
        /// <summary>
        /// CPython 3.12 UTF-8 decoder semantics (Objects/unicodeobject.c
        /// unicode_decode_utf8). Errors are reported at the sequence start byte;
        /// truncated sequences report "unexpected end of data" with a byte range.
        /// </summary>
        internal static string DecodeUtf8Python(byte[] data, string errors)
        {
            if (errors != "strict" && errors != "replace" && errors != "ignore"
                && errors != "surrogateescape" && errors != "backslashreplace")
            {
                throw PyLookupError.Create($"unknown error handler name '{errors}'");
            }

            var sb = new System.Text.StringBuilder();
            int i = 0;
            int n = data.Length;
            while (i < n)
            {
                byte b0 = data[i];
                if (b0 < 0x80)
                {
                    sb.Append((char)b0);
                    i++;
                    continue;
                }

                int extra;      // continuation bytes required
                uint minCp;     // range guards
                uint maxCp;
                if (b0 >= 0xC2 && b0 <= 0xDF) { extra = 1; minCp = 0x80; maxCp = 0x7FF; }
                else if (b0 >= 0xE0 && b0 <= 0xEF) { extra = 2; minCp = 0x800; maxCp = 0xFFFF; }
                else if (b0 >= 0xF0 && b0 <= 0xF4) { extra = 3; minCp = 0x10000; maxCp = 0x10FFFF; }
                else
                {
                    HandleDecodeError(data, i, i, errors, sb, "invalid start byte");
                    i++;
                    continue;
                }

                if (i + extra >= n)
                {
                    // Truncated: all continuation bytes that exist must still be valid
                    for (int k = 1; k < n - i; k++)
                    {
                        if ((data[i + k] & 0xC0) != 0x80)
                        {
                            HandleDecodeError(data, i, i, errors, sb, "invalid continuation byte");
                            i++;
                            goto nextInput;
                        }
                    }
                    HandleDecodeError(data, i, n - 1, errors, sb, "unexpected end of data", true);
                    i = n;
                    continue;
                }

                bool ok = true;
                uint cp = (uint)(b0 & (0x7F >> extra));
                for (int k = 1; k <= extra; k++)
                {
                    byte bk = data[i + k];
                    byte lo = 0x80, hi = 0xBF;
                    if (k == 1)
                    {
                        if (b0 == 0xE0) lo = 0xA0;         // overlong guard
                        else if (b0 == 0xED) hi = 0x9F;    // surrogate guard
                        else if (b0 == 0xF0) lo = 0x90;
                        else if (b0 == 0xF4) hi = 0x8F;
                    }
                    if (bk < lo || bk > hi) { ok = false; break; }
                    cp = (cp << 6) | (uint)(bk & 0x3F);
                }
                if (!ok || cp < minCp || cp > maxCp)
                {
                    HandleDecodeError(data, i, i, errors, sb, "invalid continuation byte");
                    i++;
                    continue;
                }

                if (cp <= 0xFFFF)
                {
                    sb.Append((char)cp);
                }
                else
                {
                    cp -= 0x10000;
                    sb.Append((char)(0xD800 + (cp >> 10)));
                    sb.Append((char)(0xDC00 + (cp & 0x3FF)));
                }
                i += extra + 1;
                nextInput: ;
            }
            return sb.ToString();
        }

        private static void HandleDecodeError(byte[] data, int start, int end, string errors,
            System.Text.StringBuilder sb, string reason, bool range = false)
        {
            if (errors == "strict")
            {
                string where = range && end > start
                    ? $"bytes in position {start}-{end}"
                    : $"byte 0x{data[start]:x2} in position {start}";
                throw PyUnicodeDecodeError.Create($"'utf-8' codec can't decode {where}: {reason}");
            }
            if (errors == "replace")
            {
                sb.Append('\uFFFD');
            }
            else if (errors == "surrogateescape")
            {
                for (int k = start; k <= end; k++)
                {
                    sb.Append((char)(0xDC00 + data[k]));
                }
            }
            else if (errors == "backslashreplace")
            {
                for (int k = start; k <= end; k++)
                {
                    sb.Append($"\\x{data[k]:x2}");
                }
            }
            // ignore: emit nothing
        }

        /// <summary>
        /// CPython ascii decoder: "'ascii' codec can't decode byte 0x80 in position 0:
        /// ordinal not in range(128)".
        /// </summary>
        internal static string DecodeAsciiPython(byte[] data, string errors)
        {
            if (errors != "strict" && errors != "replace" && errors != "ignore"
                && errors != "surrogateescape" && errors != "backslashreplace")
            {
                throw PyLookupError.Create($"unknown error handler name '{errors}'");
            }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (b < 0x80)
                {
                    sb.Append((char)b);
                    continue;
                }
                if (errors == "strict")
                {
                    throw PyUnicodeDecodeError.Create($"'ascii' codec can't decode byte 0x{b:x2} in position {i}: ordinal not in range(128)");
                }
                if (errors == "replace") sb.Append('\uFFFD');
                else if (errors == "surrogateescape") sb.Append((char)(0xDC00 + b));
                else if (errors == "backslashreplace") sb.Append($"\\x{b:x2}");
            }
            return sb.ToString();
        }

        private static void InitializeBytesDescriptors()
        {
            var bytesType = PyType.BytesType;

            // CPython 3.12: Objects/bytesobject.c:2300-2320 - bytes_decode
            // B.decode(encoding='utf-8', errors='strict') -> str
            bytesType.TypeDict["decode"] = new PyMethodDescriptor(
                "decode", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'decode' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    string encoding = "utf-8";
                    string errors = "strict";

                    if (args.Length > 0 && args[0] is PyString encStr)
                        encoding = encStr.Value;
                    if (args.Length > 1 && args[1] is PyString errStr)
                        errors = errStr.Value;

                    var encLower = encoding.ToLowerInvariant();
                    if (encLower == "utf-8" || encLower == "utf8")
                    {
                        // CPython 3.12: Objects/unicodeobject.c ascii/utf8 decoder with
                        // per-handler behaviour and byte-accurate messages.
                        return new PyString(DecodeUtf8Python(bytes.Value, errors));
                    }
                    if (encLower == "ascii" || encLower == "us-ascii" || encLower == "646")
                    {
                        return new PyString(DecodeAsciiPython(bytes.Value, errors));
                    }

                    // Handle encoding parameter
                    System.Text.Encoding enc = encLower switch
                    {
                        "utf-16" or "utf16" => System.Text.Encoding.Unicode,
                        "utf-32" or "utf32" => System.Text.Encoding.UTF32,
                        "latin-1" or "latin1" or "iso-8859-1" => System.Text.Encoding.Latin1,
                        _ => throw PyLookupError.Create($"unknown encoding: {encoding}")
                    };

                    try
                    {
                        return new PyString(enc.GetString(bytes.Value));
                    }
                    catch (System.Text.DecoderFallbackException ex)
                    {
                        if (errors == "strict")
                            throw PyUnicodeDecodeError.Create($"'{encoding}' codec can't decode bytes: {ex.Message}");
                        else if (errors == "ignore")
                            return new PyString(enc.GetString(bytes.Value)); // Try without exceptions
                        else if (errors == "replace")
                            return new PyString(enc.GetString(bytes.Value)); // Use replacement char
                        else
                            throw PyLookupError.Create($"unknown error handler name '{errors}'");
                    }
                },
                minArgs: 0, maxArgs: 2
            );

            // CPython 3.12: Objects/bytesobject.c:2560-2580 - bytes_hex
            // B.hex([sep[, bytes_per_sep]]) -> str
            bytesType.TypeDict["hex"] = new PyMethodDescriptor(
                "hex", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'hex' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    // Simple hex conversion (separator support can be added later)
                    var sb = new System.Text.StringBuilder(bytes.Value.Length * 2);
                    foreach (byte b in bytes.Value)
                    {
                        sb.Append($"{b:x2}");
                    }
                    return new PyString(sb.ToString());
                },
                minArgs: 0, maxArgs: 0
            );

            // CPython 3.12: Objects/bytesobject.c:2450-2500 - bytes_fromhex (classmethod)
            // bytes.fromhex(string) -> bytes
            var fromhexMethod = new PyMethodDescriptor(
                "fromhex", bytesType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"fromhex() takes exactly one argument ({args.Length} given)");

                    if (args[0] is not PyString hexStr)
                        throw PyTypeError.Create($"fromhex() argument must be str, not {args[0].GetTypeName()}");

                    string s = hexStr.Value.Replace(" ", "").Replace("\t", "").Replace("\n", "").Replace("\r", "");

                    if (s.Length % 2 != 0)
                        throw PyValueError.Create("non-hexadecimal number found in fromhex() arg");

                    var bytes = new byte[s.Length / 2];
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        try
                        {
                            bytes[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
                        }
                        catch
                        {
                            throw PyValueError.Create("non-hexadecimal number found in fromhex() arg");
                        }
                    }
                    return new PyBytes(bytes);
                },
                minArgs: 1, maxArgs: 1
            );
            bytesType.TypeDict["fromhex"] = new PyClassMethodDescriptor("fromhex", bytesType, fromhexMethod);

                        // p41: one CPython-shaped dispatcher for the whole byte search/query family.
            // CPython 3.12 Objects/bytesobject.c owns these: find_impl / reverse_find_impl
            // (index_impl and rindex_impl wrap them with "subsection not found"),
            // counting_impl, startswith_impl, and removeprefix_impl / removesuffix_impl
            // (METH_O, hence "bytes.NAME() takes exactly one argument (N given)").
            // PyByteArray delegates to the same cores, as bytearrayobject.c does.
            PyMethodDescriptor Bts(string name, System.Func<PyBytes, PyObject[], PyObject> body, int min, int max)
                => new PyMethodDescriptor(name, bytesType, (self, args, kwargs) => {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create($"descriptor '{name}' requires a 'bytes' object but received a '{self.GetTypeName()}'");
                    if (args.Length < min)
                        throw PyTypeError.Create($"{name}() takes at least {min} argument ({args.Length} given)");
                    if (args.Length > max)
                        throw PyTypeError.Create(max == 1
                            ? $"bytes.{name}() takes exactly one argument ({args.Length} given)"
                            : $"{name}() takes at most {max} arguments ({args.Length} given)");
                    return body(selfBytes, args);
                }, minArgs: min, maxArgs: max);

            bytesType.TypeDict["find"] = Bts("find", (b, args) => FindCore(b.Value, args, false, false), 1, 3);
            bytesType.TypeDict["rfind"] = Bts("rfind", (b, args) => FindCore(b.Value, args, true, false), 1, 3);
            bytesType.TypeDict["index"] = Bts("index", (b, args) => FindCore(b.Value, args, false, true), 1, 3);
            bytesType.TypeDict["rindex"] = Bts("rindex", (b, args) => FindCore(b.Value, args, true, true), 1, 3);
            bytesType.TypeDict["count"] = Bts("count", (b, args) => CountCore(b.Value, args), 1, 3);
            bytesType.TypeDict["startswith"] = Bts("startswith", (b, args) => StartEndCore(b.Value, args, true), 1, 3);
            bytesType.TypeDict["endswith"] = Bts("endswith", (b, args) => StartEndCore(b.Value, args, false), 1, 3);
            bytesType.TypeDict["removeprefix"] = Bts("removeprefix", (b, args) => new PyBytes(TrimEdgeBytes(b.Value, args, true)), 1, 1);
            bytesType.TypeDict["removesuffix"] = Bts("removesuffix", (b, args) => new PyBytes(TrimEdgeBytes(b.Value, args, false)), 1, 1);


            
            // CPython 3.12: Objects/bytesobject.c:2200-2250 - bytes_replace
            // B.replace(old, new[, count]) -> bytes
            bytesType.TypeDict["replace"] = new PyMethodDescriptor(
                "replace", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'replace' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 2 || args.Length > 3)
                        throw PyTypeError.Create($"replace() takes at least 2 arguments ({args.Length} given)");

                    if (args[0] is not PyBytes old)
                        throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");
                    if (args[1] is not PyBytes newBytes)
                        throw PyTypeError.Create($"a bytes-like object is required, not '{args[1].GetTypeName()}'");

                    int maxCount = -1;
                    if (args.Length >= 3 && args[2] is PyInt countInt)
                        maxCount = (int)countInt.Value;

                    // Build result
                    var result = new System.Collections.Generic.List<byte>();
                    int i = 0;
                    int replaced = 0;

                    while (i < bytes.Value.Length)
                    {
                        if (maxCount >= 0 && replaced >= maxCount)
                        {
                            // Copy rest of bytes
                            for (; i < bytes.Value.Length; i++)
                                result.Add(bytes.Value[i]);
                            break;
                        }

                        bool match = false;
                        if (i <= bytes.Value.Length - old.Value.Length)
                        {
                            match = true;
                            for (int j = 0; j < old.Value.Length; j++)
                            {
                                if (bytes.Value[i + j] != old.Value[j])
                                {
                                    match = false;
                                    break;
                                }
                            }
                        }

                        if (match)
                        {
                            // Replace
                            result.AddRange(newBytes.Value);
                            i += old.Value.Length;
                            replaced++;
                        }
                        else
                        {
                            result.Add(bytes.Value[i]);
                            i++;
                        }
                    }

                    return new PyBytes(result.ToArray());
                },
                minArgs: 2, maxArgs: 3
            );

            
            
            // CPython 3.12: Objects/bytesobject.c:1700-1800 - bytes_split
            // B.split(sep=None, maxsplit=-1) -> list of bytes
            bytesType.TypeDict["split"] = new PyMethodDescriptor(
                "split", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'split' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    PyBytes? sep = null;
                    int maxsplit = -1;

                    if (args.Length > 0 && args[0] != PyNone.Instance)
                    {
                        if (args[0] is not PyBytes sepBytes)
                            throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");
                        sep = sepBytes;
                    }
                    if (args.Length > 1 && args[1] is PyInt maxsplitInt)
                        maxsplit = (int)maxsplitInt.Value;

                    var result = new System.Collections.Generic.List<PyObject>();

                    if (sep == null)
                    {
                        // Split on whitespace
                        int i = 0;
                        while (i < bytes.Value.Length && (maxsplit < 0 || result.Count < maxsplit))
                        {
                            // Skip whitespace
                            while (i < bytes.Value.Length && IsWhitespace(bytes.Value[i]))
                                i++;

                            if (i >= bytes.Value.Length)
                                break;

                            // Find end of word
                            int start = i;
                            while (i < bytes.Value.Length && !IsWhitespace(bytes.Value[i]))
                                i++;

                            var word = new byte[i - start];
                            Array.Copy(bytes.Value, start, word, 0, i - start);
                            result.Add(new PyBytes(word));
                        }
                    }
                    else
                    {
                        // Split on separator
                        int i = 0;
                        int splitCount = 0;
                        while (i < bytes.Value.Length && (maxsplit < 0 || splitCount < maxsplit))
                        {
                            bool match = false;
                            if (i <= bytes.Value.Length - sep.Value.Length)
                            {
                                match = true;
                                for (int j = 0; j < sep.Value.Length; j++)
                                {
                                    if (bytes.Value[i + j] != sep.Value[j])
                                    {
                                        match = false;
                                        break;
                                    }
                                }
                            }

                            if (match)
                            {
                                var part = new byte[i];
                                Array.Copy(bytes.Value, 0, part, 0, i);
                                result.Add(new PyBytes(part));
                                i += sep.Value.Length;
                                splitCount++;

                                // Remaining bytes as result
                                var remaining = new byte[bytes.Value.Length - i];
                                Array.Copy(bytes.Value, i, remaining, 0, remaining.Length);
                                bytes = new PyBytes(remaining);
                                i = 0;
                            }
                            else
                            {
                                i++;
                            }
                        }
                        // Add remaining
                        result.Add(bytes);
                    }

                    return new PyList(result.ToArray());
                },
                minArgs: 0, maxArgs: 2
            );

            // CPython 3.12: Objects/bytesobject.c:1600-1650 - bytes_join
            // B.join(iterable_of_bytes) -> bytes
            bytesType.TypeDict["join"] = new PyMethodDescriptor(
                "join", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes separator)
                        throw PyTypeError.Create($"descriptor 'join' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    if (args.Length != 1)
                        throw PyTypeError.Create($"join() takes exactly one argument ({args.Length} given)");

                    if (args[0] is not PyList list)
                        throw PyTypeError.Create($"can only join an iterable");

                    var result = new System.Collections.Generic.List<byte>();
                    for (int i = 0; i < list.Items.Length; i++)
                    {
                        if (list.Items[i] is not PyBytes item)
                            throw PyTypeError.Create($"sequence item {i}: expected bytes, {list.Items[i].GetTypeName()} found");

                        if (i > 0)
                            result.AddRange(separator.Value);
                        result.AddRange(item.Value);
                    }

                    return new PyBytes(result.ToArray());
                },
                minArgs: 1, maxArgs: 1
            );

            
            
            
            // CPython 3.12: Objects/bytesobject.c:1550-1600 - bytes_partition
            // B.partition(sep) -> (head, sep, tail)
            bytesType.TypeDict["partition"] = new PyMethodDescriptor(
                "partition", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'partition' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    if (args.Length != 1)
                        throw PyTypeError.Create($"partition() takes exactly one argument ({args.Length} given)");

                    if (args[0] is not PyBytes sep)
                        throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");

                    if (sep.Value.Length == 0)
                        throw PyValueError.Create("empty separator");

                    // Find separator
                    for (int i = 0; i <= bytes.Value.Length - sep.Value.Length; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < sep.Value.Length; j++)
                        {
                            if (bytes.Value[i + j] != sep.Value[j])
                            {
                                match = false;
                                break;
                            }
                        }
                        if (match)
                        {
                            // Found separator
                            var head = new byte[i];
                            Array.Copy(bytes.Value, 0, head, 0, i);

                            var tail = new byte[bytes.Value.Length - i - sep.Value.Length];
                            Array.Copy(bytes.Value, i + sep.Value.Length, tail, 0, tail.Length);

                            return new PyTuple(
                                new PyBytes(head),
                                sep,
                                new PyBytes(tail)
                            );
                        }
                    }

                    // Separator not found
                    return new PyTuple(bytes, new PyBytes(new byte[0]), new PyBytes(new byte[0]));
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/bytesobject.c:1550-1600 - bytes_rpartition
            // B.rpartition(sep) -> (head, sep, tail)
            bytesType.TypeDict["rpartition"] = new PyMethodDescriptor(
                "rpartition", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'rpartition' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    if (args.Length != 1)
                        throw PyTypeError.Create($"rpartition() takes exactly one argument ({args.Length} given)");

                    if (args[0] is not PyBytes sep)
                        throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");

                    if (sep.Value.Length == 0)
                        throw PyValueError.Create("empty separator");

                    // Find separator from right
                    for (int i = bytes.Value.Length - sep.Value.Length; i >= 0; i--)
                    {
                        bool match = true;
                        for (int j = 0; j < sep.Value.Length; j++)
                        {
                            if (bytes.Value[i + j] != sep.Value[j])
                            {
                                match = false;
                                break;
                            }
                        }
                        if (match)
                        {
                            // Found separator
                            var head = new byte[i];
                            Array.Copy(bytes.Value, 0, head, 0, i);

                            var tail = new byte[bytes.Value.Length - i - sep.Value.Length];
                            Array.Copy(bytes.Value, i + sep.Value.Length, tail, 0, tail.Length);

                            return new PyTuple(
                                new PyBytes(head),
                                sep,
                                new PyBytes(tail)
                            );
                        }
                    }

                    // Separator not found
                    return new PyTuple(new PyBytes(new byte[0]), new PyBytes(new byte[0]), bytes);
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/bytearrayobject.c:1510-1538 (bytearray_isalnum_impl)
            // bytes.isalnum() - Return True if all characters are alphanumeric and not empty
            bytesType.TypeDict["isalnum"] = new PyMethodDescriptor(
                "isalnum",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'isalnum' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"isalnum() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    if (data.Length == 0) return PyBool.False;

                    foreach (byte b in data)
                    {
                        // Alphanumeric: 0-9, A-Z, a-z
                        if (!((b >= '0' && b <= '9') || (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z')))
                            return PyBool.False;
                    }
                    return PyBool.True;
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1540-1568 (bytearray_isalpha_impl)
            // bytes.isalpha() - Return True if all characters are alphabetic and not empty
            bytesType.TypeDict["isalpha"] = new PyMethodDescriptor(
                "isalpha",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'isalpha' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"isalpha() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    if (data.Length == 0) return PyBool.False;

                    foreach (byte b in data)
                    {
                        // Alphabetic: A-Z, a-z
                        if (!((b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z')))
                            return PyBool.False;
                    }
                    return PyBool.True;
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1570-1598 (bytearray_isascii_impl)
            // bytes.isascii() - Return True if all bytes are < 128
            bytesType.TypeDict["isascii"] = new PyMethodDescriptor(
                "isascii",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'isascii' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"isascii() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    // Empty bytes is ASCII
                    foreach (byte b in data)
                    {
                        if (b >= 128)
                            return PyBool.False;
                    }
                    return PyBool.True;
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1600-1628 (bytearray_isdigit_impl)
            // bytes.isdigit() - Return True if all characters are digits and not empty
            bytesType.TypeDict["isdigit"] = new PyMethodDescriptor(
                "isdigit",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'isdigit' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"isdigit() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    if (data.Length == 0) return PyBool.False;

                    foreach (byte b in data)
                    {
                        // Digits: 0-9
                        if (!(b >= '0' && b <= '9'))
                            return PyBool.False;
                    }
                    return PyBool.True;
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1630-1658 (bytearray_islower_impl)
            // bytes.islower() - Return True if all cased characters are lowercase
            bytesType.TypeDict["islower"] = new PyMethodDescriptor(
                "islower",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'islower' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"islower() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    if (data.Length == 0) return PyBool.False;

                    bool hasLower = false;
                    foreach (byte b in data)
                    {
                        // Upper case exists?
                        if (b >= 'A' && b <= 'Z')
                            return PyBool.False;
                        // At least one lower case?
                        if (b >= 'a' && b <= 'z')
                            hasLower = true;
                    }
                    return PyBool.FromBool(hasLower);
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1660-1688 (bytearray_isspace_impl)
            // bytes.isspace() - Return True if all characters are whitespace and not empty
            bytesType.TypeDict["isspace"] = new PyMethodDescriptor(
                "isspace",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'isspace' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"isspace() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    if (data.Length == 0) return PyBool.False;

                    foreach (byte b in data)
                    {
                        // Whitespace: space, tab, newline, carriage return, form feed, vertical tab
                        if (!(b == ' ' || b == '\t' || b == '\n' || b == '\r' || b == '\f' || b == '\v'))
                            return PyBool.False;
                    }
                    return PyBool.True;
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1690-1745 (bytearray_istitle_impl)
            // bytes.istitle() - Return True if the bytes is titlecased
            bytesType.TypeDict["istitle"] = new PyMethodDescriptor(
                "istitle",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'istitle' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"istitle() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    if (data.Length == 0) return PyBool.False;

                    bool cased = false;
                    bool previousIsCased = false;

                    foreach (byte b in data)
                    {
                        bool isUpper = (b >= 'A' && b <= 'Z');
                        bool isLower = (b >= 'a' && b <= 'z');

                        if (isUpper)
                        {
                            // Upper case after cased char is wrong
                            if (previousIsCased)
                                return PyBool.False;
                            previousIsCased = true;
                            cased = true;
                        }
                        else if (isLower)
                        {
                            // Lower case must follow cased char
                            if (!previousIsCased)
                                return PyBool.False;
                            cased = true;
                        }
                        else
                        {
                            previousIsCased = false;
                        }
                    }
                    return PyBool.FromBool(cased);
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1747-1775 (bytearray_isupper_impl)
            // bytes.isupper() - Return True if all cased characters are uppercase
            bytesType.TypeDict["isupper"] = new PyMethodDescriptor(
                "isupper",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'isupper' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"isupper() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    if (data.Length == 0) return PyBool.False;

                    bool hasUpper = false;
                    foreach (byte b in data)
                    {
                        // Lower case exists?
                        if (b >= 'a' && b <= 'z')
                            return PyBool.False;
                        // At least one upper case?
                        if (b >= 'A' && b <= 'Z')
                            hasUpper = true;
                    }
                    return PyBool.FromBool(hasUpper);
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1777-1810 (bytearray_capitalize_impl)
            // bytes.capitalize() - Return a capitalized version of the bytes
            bytesType.TypeDict["capitalize"] = new PyMethodDescriptor(
                "capitalize",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'capitalize' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"capitalize() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    if (data.Length == 0) return selfBytes;

                    byte[] result = new byte[data.Length];
                    // First char to upper
                    if (data[0] >= 'a' && data[0] <= 'z')
                        result[0] = (byte)(data[0] - 32);
                    else
                        result[0] = data[0];

                    // Rest to lower
                    for (int i = 1; i < data.Length; i++)
                    {
                        if (data[i] >= 'A' && data[i] <= 'Z')
                            result[i] = (byte)(data[i] + 32);
                        else
                            result[i] = data[i];
                    }
                    return new PyBytes(result);
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1812-1840 (bytearray_lower_impl)
            // bytes.lower() - Return a copy with all the cased characters converted to lowercase
            bytesType.TypeDict["lower"] = new PyMethodDescriptor(
                "lower",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'lower' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"lower() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    byte[] result = new byte[data.Length];
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (data[i] >= 'A' && data[i] <= 'Z')
                            result[i] = (byte)(data[i] + 32);
                        else
                            result[i] = data[i];
                    }
                    return new PyBytes(result);
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1842-1870 (bytearray_upper_impl)
            // bytes.upper() - Return a copy with all the cased characters converted to uppercase
            bytesType.TypeDict["upper"] = new PyMethodDescriptor(
                "upper",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'upper' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"upper() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    byte[] result = new byte[data.Length];
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (data[i] >= 'a' && data[i] <= 'z')
                            result[i] = (byte)(data[i] - 32);
                        else
                            result[i] = data[i];
                    }
                    return new PyBytes(result);
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1872-1900 (bytearray_swapcase_impl)
            // bytes.swapcase() - Return a copy with uppercase characters converted to lowercase and vice versa
            bytesType.TypeDict["swapcase"] = new PyMethodDescriptor(
                "swapcase",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'swapcase' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"swapcase() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    byte[] result = new byte[data.Length];
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (data[i] >= 'A' && data[i] <= 'Z')
                            result[i] = (byte)(data[i] + 32);  // Upper to lower
                        else if (data[i] >= 'a' && data[i] <= 'z')
                            result[i] = (byte)(data[i] - 32);  // Lower to upper
                        else
                            result[i] = data[i];
                    }
                    return new PyBytes(result);
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytearrayobject.c:1902-1970 (bytearray_title_impl)
            // bytes.title() - Return a titlecased version of the bytes
            bytesType.TypeDict["title"] = new PyMethodDescriptor(
                "title",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'title' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 0)
                        throw PyTypeError.Create($"title() takes no arguments ({args.Length} given)");

                    byte[] data = selfBytes.Value;
                    byte[] result = new byte[data.Length];
                    bool previousIsCased = false;

                    for (int i = 0; i < data.Length; i++)
                    {
                        bool isUpper = (data[i] >= 'A' && data[i] <= 'Z');
                        bool isLower = (data[i] >= 'a' && data[i] <= 'z');

                        if (isLower)
                        {
                            if (!previousIsCased)
                            {
                                // Start of word - convert to upper
                                result[i] = (byte)(data[i] - 32);
                                previousIsCased = true;
                            }
                            else
                            {
                                // Middle of word - keep lower
                                result[i] = data[i];
                            }
                        }
                        else if (isUpper)
                        {
                            if (previousIsCased)
                            {
                                // Middle of word - convert to lower
                                result[i] = (byte)(data[i] + 32);
                            }
                            else
                            {
                                // Start of word - keep upper
                                result[i] = data[i];
                                previousIsCased = true;
                            }
                        }
                        else
                        {
                            // Non-cased character
                            result[i] = data[i];
                            previousIsCased = false;
                        }
                    }
                    return new PyBytes(result);
                },
                minArgs: 0,
                maxArgs: 0
            );

            // CPython 3.12: Objects/bytesobject.c:1300-1350 (bytes_strip_impl)
            // bytes.strip([chars]) - Remove leading and trailing bytes
            bytesType.TypeDict["strip"] = new PyMethodDescriptor(
                "strip",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'strip' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    byte[] data = selfBytes.Value;
                    byte[] stripChars = null;

                    if (args.Length > 0)
                    {
                        if (args[0] is PyBytes stripBytes)
                            stripChars = stripBytes.Value;
                        else if (args[0] is PyNone)
                            stripChars = null;
                        else
                            throw PyTypeError.Create("strip() argument must be bytes or None");
                    }

                    int start = 0;
                    int end = data.Length;

                    // Strip from left
                    while (start < end && ShouldStrip(data[start], stripChars))
                        start++;

                    // Strip from right
                    while (end > start && ShouldStrip(data[end - 1], stripChars))
                        end--;

                    if (start == 0 && end == data.Length)
                        return selfBytes;

                    byte[] result = new byte[end - start];
                    Array.Copy(data, start, result, 0, end - start);
                    return new PyBytes(result);
                },
                minArgs: 0,
                maxArgs: 1
            );

            // CPython 3.12: Objects/bytesobject.c:1000-1050 (bytes_center_impl)
            // bytes.center(width[, fillchar]) - Center bytes in field of width
            bytesType.TypeDict["center"] = new PyMethodDescriptor(
                "center",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'center' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length < 1)
                        throw PyTypeError.Create("center() takes at least 1 argument (0 given)");

                    if (args[0] is not PyInt widthInt)
                        throw PyTypeError.Create("center() argument 1 must be int");

                    int width = (int)widthInt.Value;
                    byte fillByte = (byte)' ';

                    if (args.Length > 1)
                    {
                        if (args[1] is PyBytes fillBytes && fillBytes.Value.Length == 1)
                            fillByte = fillBytes.Value[0];
                        else
                            throw PyTypeError.Create("center() argument 2 must be a byte string of length 1");
                    }

                    byte[] data = selfBytes.Value;
                    if (width <= data.Length)
                        return selfBytes;

                    int totalPad = width - data.Length;
                    int leftPad = totalPad / 2;
                    int rightPad = totalPad - leftPad;

                    byte[] result = new byte[width];
                    for (int i = 0; i < leftPad; i++)
                        result[i] = fillByte;
                    Array.Copy(data, 0, result, leftPad, data.Length);
                    for (int i = leftPad + data.Length; i < width; i++)
                        result[i] = fillByte;

                    return new PyBytes(result);
                },
                minArgs: 1,
                maxArgs: 2
            );

            // CPython 3.12: Objects/bytesobject.c:900-940 (bytes_ljust_impl)
            // bytes.ljust(width[, fillchar]) - Left justify bytes
            bytesType.TypeDict["ljust"] = new PyMethodDescriptor(
                "ljust",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'ljust' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length < 1)
                        throw PyTypeError.Create("ljust() takes at least 1 argument (0 given)");

                    if (args[0] is not PyInt widthInt)
                        throw PyTypeError.Create("ljust() argument 1 must be int");

                    int width = (int)widthInt.Value;
                    byte fillByte = (byte)' ';

                    if (args.Length > 1)
                    {
                        if (args[1] is PyBytes fillBytes && fillBytes.Value.Length == 1)
                            fillByte = fillBytes.Value[0];
                        else
                            throw PyTypeError.Create("ljust() argument 2 must be a byte string of length 1");
                    }

                    byte[] data = selfBytes.Value;
                    if (width <= data.Length)
                        return selfBytes;

                    byte[] result = new byte[width];
                    Array.Copy(data, 0, result, 0, data.Length);
                    for (int i = data.Length; i < width; i++)
                        result[i] = fillByte;

                    return new PyBytes(result);
                },
                minArgs: 1,
                maxArgs: 2
            );

            // CPython 3.12: Objects/bytesobject.c:950-990 (bytes_rjust_impl)
            // bytes.rjust(width[, fillchar]) - Right justify bytes
            bytesType.TypeDict["rjust"] = new PyMethodDescriptor(
                "rjust",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'rjust' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length < 1)
                        throw PyTypeError.Create("rjust() takes at least 1 argument (0 given)");

                    if (args[0] is not PyInt widthInt)
                        throw PyTypeError.Create("rjust() argument 1 must be int");

                    int width = (int)widthInt.Value;
                    byte fillByte = (byte)' ';

                    if (args.Length > 1)
                    {
                        if (args[1] is PyBytes fillBytes && fillBytes.Value.Length == 1)
                            fillByte = fillBytes.Value[0];
                        else
                            throw PyTypeError.Create("rjust() argument 2 must be a byte string of length 1");
                    }

                    byte[] data = selfBytes.Value;
                    if (width <= data.Length)
                        return selfBytes;

                    int padSize = width - data.Length;
                    byte[] result = new byte[width];
                    for (int i = 0; i < padSize; i++)
                        result[i] = fillByte;
                    Array.Copy(data, 0, result, padSize, data.Length);

                    return new PyBytes(result);
                },
                minArgs: 1,
                maxArgs: 2
            );

            // CPython 3.12: Objects/bytesobject.c:1100-1140 (bytes_zfill_impl)
            // bytes.zfill(width) - Pad bytes with zeros on the left
            bytesType.TypeDict["zfill"] = new PyMethodDescriptor(
                "zfill",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'zfill' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"zfill() takes exactly 1 argument ({args.Length} given)");

                    if (args[0] is not PyInt widthInt)
                        throw PyTypeError.Create("zfill() argument must be int");

                    int width = (int)widthInt.Value;
                    byte[] data = selfBytes.Value;

                    if (width <= data.Length)
                        return selfBytes;

                    int padSize = width - data.Length;
                    byte[] result = new byte[width];

                    // Handle sign
                    int dataStart = 0;
                    int resultStart = padSize;
                    if (data.Length > 0 && (data[0] == '+' || data[0] == '-'))
                    {
                        result[0] = data[0];
                        dataStart = 1;
                        resultStart = padSize + 1;
                        padSize++;
                    }

                    // Fill with zeros
                    for (int i = (dataStart > 0 ? 1 : 0); i < resultStart; i++)
                        result[i] = (byte)'0';

                    // Copy data
                    Array.Copy(data, dataStart, result, resultStart, data.Length - dataStart);

                    return new PyBytes(result);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // bytes.removeprefix(prefix) - Remove prefix if present (Python 3.9+)
            
            // bytes.removesuffix(suffix) - Remove suffix if present (Python 3.9+)
            
            // CPython 3.12: Objects/bytesobject.c:1360-1400 (bytes_lstrip_impl)
            // bytes.lstrip([chars]) - Remove leading bytes
            bytesType.TypeDict["lstrip"] = new PyMethodDescriptor(
                "lstrip",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'lstrip' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    byte[] data = selfBytes.Value;
                    byte[] stripChars = null;

                    if (args.Length > 0)
                    {
                        if (args[0] is PyBytes stripBytes)
                            stripChars = stripBytes.Value;
                        else if (args[0] is PyNone)
                            stripChars = null;
                        else
                            throw PyTypeError.Create("lstrip() argument must be bytes or None");
                    }

                    int start = 0;
                    while (start < data.Length && ShouldStrip(data[start], stripChars))
                        start++;

                    if (start == 0)
                        return selfBytes;

                    byte[] result = new byte[data.Length - start];
                    Array.Copy(data, start, result, 0, data.Length - start);
                    return new PyBytes(result);
                },
                minArgs: 0,
                maxArgs: 1
            );

            // CPython 3.12: Objects/bytesobject.c:1410-1450 (bytes_rstrip_impl)
            // bytes.rstrip([chars]) - Remove trailing bytes
            bytesType.TypeDict["rstrip"] = new PyMethodDescriptor(
                "rstrip",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'rstrip' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    byte[] data = selfBytes.Value;
                    byte[] stripChars = null;

                    if (args.Length > 0)
                    {
                        if (args[0] is PyBytes stripBytes)
                            stripChars = stripBytes.Value;
                        else if (args[0] is PyNone)
                            stripChars = null;
                        else
                            throw PyTypeError.Create("rstrip() argument must be bytes or None");
                    }

                    int end = data.Length;
                    while (end > 0 && ShouldStrip(data[end - 1], stripChars))
                        end--;

                    if (end == data.Length)
                        return selfBytes;

                    byte[] result = new byte[end];
                    Array.Copy(data, 0, result, 0, end);
                    return new PyBytes(result);
                },
                minArgs: 0,
                maxArgs: 1
            );

            // CPython 3.12: Objects/bytesobject.c:1700-1760 (bytes_splitlines_impl)
            // bytes.splitlines([keepends]) - Return a list of lines
            bytesType.TypeDict["splitlines"] = new PyMethodDescriptor(
                "splitlines",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'splitlines' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    bool keepends = false;
                    if (args.Length > 0)
                    {
                        if (args[0] is PyBool boolVal)
                            keepends = boolVal.Value;
                        else if (args[0] is PyInt intVal)
                            keepends = intVal.Value != 0;
                    }

                    byte[] data = selfBytes.Value;
                    var lines = new System.Collections.Generic.List<PyObject>();
                    int lineStart = 0;

                    for (int i = 0; i < data.Length; i++)
                    {
                        if (data[i] == '\n' || data[i] == '\r')
                        {
                            int lineEnd = i;
                            int nextStart = i + 1;

                            // Handle \r\n
                            if (data[i] == '\r' && i + 1 < data.Length && data[i + 1] == '\n')
                            {
                                nextStart = i + 2;
                                if (keepends)
                                    lineEnd = i + 2;
                            }
                            else if (keepends)
                            {
                                lineEnd = i + 1;
                            }

                            byte[] line = new byte[lineEnd - lineStart];
                            Array.Copy(data, lineStart, line, 0, lineEnd - lineStart);
                            lines.Add(new PyBytes(line));
                            lineStart = nextStart;
                            i = nextStart - 1;
                        }
                    }

                    // Add last line if any
                    if (lineStart < data.Length)
                    {
                        byte[] line = new byte[data.Length - lineStart];
                        Array.Copy(data, lineStart, line, 0, data.Length - lineStart);
                        lines.Add(new PyBytes(line));
                    }

                    return new PyList(lines.ToArray());
                },
                minArgs: 0,
                maxArgs: 1
            );

            // CPython 3.12: Objects/bytesobject.c:1850-1900 (bytes_expandtabs_impl)
            // bytes.expandtabs([tabsize]) - Expand tabs to spaces
            bytesType.TypeDict["expandtabs"] = new PyMethodDescriptor(
                "expandtabs",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'expandtabs' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    int tabsize = 8;
                    if (args.Length > 0)
                    {
                        if (args[0] is PyInt tabInt)
                            tabsize = (int)tabInt.Value;
                        else
                            throw PyTypeError.Create("expandtabs() argument must be int");
                    }

                    byte[] data = selfBytes.Value;
                    var result = new System.Collections.Generic.List<byte>();
                    int column = 0;

                    foreach (byte b in data)
                    {
                        if (b == '\t')
                        {
                            // Add spaces to next tab stop
                            int spaces = tabsize - (column % tabsize);
                            for (int i = 0; i < spaces; i++)
                            {
                                result.Add((byte)' ');
                            }
                            column += spaces;
                        }
                        else if (b == '\n' || b == '\r')
                        {
                            result.Add(b);
                            column = 0;
                        }
                        else
                        {
                            result.Add(b);
                            column++;
                        }
                    }

                    return new PyBytes(result.ToArray());
                },
                minArgs: 0,
                maxArgs: 1
            );

            // CPython 3.12: Objects/bytesobject.c:2200-2280 (bytes_translate_impl)
            // bytes.translate(table[, delete]) - Translate bytes using table
            bytesType.TypeDict["translate"] = new PyMethodDescriptor(
                "translate",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'translate' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length < 1)
                        throw PyTypeError.Create("translate() takes at least 1 argument (0 given)");

                    byte[] data = selfBytes.Value;
                    byte[] table = null;
                    byte[] deleteBytes = null;

                    // Get translation table
                    if (args[0] is PyBytes tableBytes)
                    {
                        table = tableBytes.Value;
                        if (table.Length != 256)
                            throw PyValueError.Create("translation table must be 256 bytes");
                    }
                    else if (args[0] is not PyNone)
                    {
                        throw PyTypeError.Create("translate() argument 1 must be bytes or None");
                    }

                    // Get delete bytes
                    if (args.Length > 1)
                    {
                        if (args[1] is PyBytes delBytes)
                            deleteBytes = delBytes.Value;
                        else
                            throw PyTypeError.Create("translate() argument 2 must be bytes");
                    }

                    var result = new System.Collections.Generic.List<byte>();
                    foreach (byte b in data)
                    {
                        // Check if byte should be deleted
                        bool shouldDelete = false;
                        if (deleteBytes != null)
                        {
                            foreach (byte delByte in deleteBytes)
                            {
                                if (b == delByte)
                                {
                                    shouldDelete = true;
                                    break;
                                }
                            }
                        }

                        if (!shouldDelete)
                        {
                            // Apply translation if table exists
                            if (table != null)
                                result.Add(table[b]);
                            else
                                result.Add(b);
                        }
                    }

                    return new PyBytes(result.ToArray());
                },
                minArgs: 1,
                maxArgs: 2
            );

            // CPython 3.12: Objects/bytesobject.c:2350-2380 (bytes_maketrans) - classmethod
            // bytes.maketrans(frm, to) -> bytes (translation table)
            var maketransMethod = new PyMethodDescriptor(
                "maketrans",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (args.Length != 2)
                        throw PyTypeError.Create($"maketrans() takes exactly 2 arguments ({args.Length} given)");

                    if (args[0] is not PyBytes frmBytes)
                        throw PyTypeError.Create("maketrans() argument 1 must be bytes");
                    if (args[1] is not PyBytes toBytes)
                        throw PyTypeError.Create("maketrans() argument 2 must be bytes");

                    byte[] frm = frmBytes.Value;
                    byte[] to = toBytes.Value;

                    if (frm.Length != to.Length)
                        throw PyValueError.Create("maketrans arguments must have same length");

                    // Create identity table
                    byte[] table = new byte[256];
                    for (int i = 0; i < 256; i++)
                        table[i] = (byte)i;

                    // Apply translations
                    for (int i = 0; i < frm.Length; i++)
                    {
                        table[frm[i]] = to[i];
                    }

                    return new PyBytes(table);
                },
                minArgs: 2,
                maxArgs: 2
            );
            bytesType.TypeDict["maketrans"] = new PyClassMethodDescriptor("maketrans", bytesType, maketransMethod);

            // CPython 3.12: Objects/bytesobject.c:1600-1660 (bytes_rsplit_impl)
            // bytes.rsplit([sep[, maxsplit]]) - Split from right
            bytesType.TypeDict["rsplit"] = new PyMethodDescriptor(
                "rsplit",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'rsplit' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    byte[] data = selfBytes.Value;
                    byte[] sep = null;
                    int maxsplit = -1;

                    if (args.Length > 0 && args[0] is not PyNone)
                    {
                        if (args[0] is PyBytes sepBytes)
                            sep = sepBytes.Value;
                        else
                            throw PyTypeError.Create("rsplit() argument 1 must be bytes or None");
                    }

                    if (args.Length > 1)
                    {
                        if (args[1] is PyInt maxInt)
                            maxsplit = (int)maxInt.Value;
                        else
                            throw PyTypeError.Create("rsplit() argument 2 must be int");
                    }

                    var parts = new System.Collections.Generic.List<PyBytes>();

                    if (sep == null || sep.Length == 0)
                    {
                        // Split on whitespace from right
                        int end = data.Length;
                        int splits = 0;

                        for (int i = data.Length - 1; i >= 0; i--)
                        {
                            if (IsWhitespace(data[i]))
                            {
                                if (end > i + 1)
                                {
                                    byte[] part = new byte[end - i - 1];
                                    Array.Copy(data, i + 1, part, 0, end - i - 1);
                                    parts.Insert(0, new PyBytes(part));
                                    splits++;
                                    if (maxsplit >= 0 && splits >= maxsplit)
                                    {
                                        // Add remaining as one part
                                        int remaining = i;
                                        while (remaining > 0 && IsWhitespace(data[remaining - 1]))
                                            remaining--;
                                        if (remaining > 0)
                                        {
                                            byte[] lastPart = new byte[remaining];
                                            Array.Copy(data, 0, lastPart, 0, remaining);
                                            parts.Insert(0, new PyBytes(lastPart));
                                        }
                                        return new PyList(parts.ToArray());
                                    }
                                }
                                end = i;
                            }
                        }

                        // Add first part
                        if (end > 0)
                        {
                            int start = 0;
                            while (start < end && IsWhitespace(data[start]))
                                start++;
                            if (start < end)
                            {
                                byte[] part = new byte[end - start];
                                Array.Copy(data, start, part, 0, end - start);
                                parts.Insert(0, new PyBytes(part));
                            }
                        }
                    }
                    else
                    {
                        // Split on separator from right
                        int end = data.Length;
                        int splits = 0;

                        for (int i = data.Length - sep.Length; i >= 0; i--)
                        {
                            bool match = true;
                            for (int j = 0; j < sep.Length; j++)
                            {
                                if (data[i + j] != sep[j])
                                {
                                    match = false;
                                    break;
                                }
                            }

                            if (match)
                            {
                                byte[] part = new byte[end - i - sep.Length];
                                Array.Copy(data, i + sep.Length, part, 0, end - i - sep.Length);
                                parts.Insert(0, new PyBytes(part));
                                splits++;
                                if (maxsplit >= 0 && splits >= maxsplit)
                                {
                                    // Add remaining
                                    byte[] lastPart = new byte[i];
                                    Array.Copy(data, 0, lastPart, 0, i);
                                    parts.Insert(0, new PyBytes(lastPart));
                                    return new PyList(parts.ToArray());
                                }
                                end = i;
                                i -= sep.Length - 1;
                            }
                        }

                        // Add first part
                        byte[] firstPart = new byte[end];
                        Array.Copy(data, 0, firstPart, 0, end);
                        parts.Insert(0, new PyBytes(firstPart));
                    }

                    return new PyList(parts.ToArray());
                },
                minArgs: 0,
                maxArgs: 2
            );

            // CPython 3.12: Objects/bytesobject.c:2890-2920 - bytes_iter
            // bytes.__iter__() - Return an iterator over the bytes
            // Each element is an integer in range(0, 256)
            bytesType.TypeDict["__iter__"] = new PyMethodDescriptor(
                "__iter__",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor '__iter__' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");

                    // Return a bytes iterator that yields PyInt for each byte
                    var items = new PyObject[selfBytes.Value.Length];
                    for (int i = 0; i < selfBytes.Value.Length; i++)
                    {
                        items[i] = new PyInt(selfBytes.Value[i]);
                    }
                    return new PyListIterator(new PyList(items));
                },
                minArgs: 0,
                maxArgs: 0
            );
        }

        private static bool ShouldStrip(byte b, byte[] stripChars)
        {
            if (stripChars == null)
            {
                // Default whitespace
                return b == ' ' || b == '\t' || b == '\n' || b == '\r' || b == '\f' || b == '\v';
            }
            else
            {
                // Check if b is in stripChars
                foreach (byte c in stripChars)
                {
                    if (b == c) return true;
                }
                return false;
            }
        }

        private static bool IsWhitespace(byte b)
        {
            return b == (byte)' ' || b == (byte)'\t' || b == (byte)'\n' || b == (byte)'\r';
        }

        public byte[] Value { get; }

        public PyBytes(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        public override string GetTypeName() => "bytes";
        public override PyType GetPyType() => PyType.BytesType;

        public override string ToString() => ToRepr().Value;

        /// <summary>
        /// CPython 3.12: Objects/bytesobject.c:2890-2920 - bytes iterator
        /// Return an iterator that yields PyInt for each byte (0-255)
        /// </summary>
        public override PyObject GetIterator()
        {
            var items = new PyObject[Value.Length];
            for (int i = 0; i < Value.Length; i++)
            {
                items[i] = new PyInt(Value[i]);
            }
            return new PyListIterator(new PyList(items), "bytes_iterator");
        }
        
        public override PyString ToRepr()
        {
            var sb = new System.Text.StringBuilder("b'");
            foreach (byte b in Value)
            {
                if (b >= 32 && b < 127 && b != '\\' && b != '\'')
                {
                    sb.Append((char)b);
                }
                else
                {
                    switch (b)
                    {
                        case (byte)'\\': sb.Append("\\\\"); break;
                        case (byte)'\'': sb.Append("\\'"); break;
                        case (byte)'\n': sb.Append("\\n"); break;
                        case (byte)'\r': sb.Append("\\r"); break;
                        case (byte)'\t': sb.Append("\\t"); break;
                        default: sb.Append($"\\x{b:x2}"); break;
                    }
                }
            }
            sb.Append('\'');
            return new PyString(sb.ToString());
        }
        
        public override int Length() => Value.Length;
        public override bool PyBoolValue() => Value.Length > 0;

        #region Buffer Protocol (PEP 688)

        // CPython 3.12: Objects/floatobject.c PyFloat_FromString accepts any buffer, so
        // float(b'1.5') is a text conversion, not a type error.
        public override PyFloat AsFloat() => PyFloat.FromBufferBytes(Value, this);

        /// <summary>
        /// PEP 688: bytes objects implement the buffer protocol
        /// </summary>
        public override PyMemoryView GetBuffer(int flags)
        {
            // bytes objects are read-only buffers; memoryview.obj reports this object.
            return new PyMemoryView(Value, true, 1, this);
        }

        /// <summary>
        /// Check if this object supports the buffer protocol (always true for bytes)
        /// </summary>
        public override bool SupportsBuffer() => true;

        #endregion

        #region Operators

        /// <summary>
        /// bytes + bytes concatenation
        /// </summary>
        public override PyObject Add(PyObject other)
        {
            if (other is PyBytes otherBytes)
            {
                var result = new byte[Value.Length + otherBytes.Value.Length];
                Array.Copy(Value, 0, result, 0, Value.Length);
                Array.Copy(otherBytes.Value, 0, result, Value.Length, otherBytes.Value.Length);
                return new PyBytes(result);
            }
            
            // CPython 3.12: bytes_concat reports "can't concat <type> to bytes".
            throw PyTypeError.Create($"can't concat {other.GetTypeName()} to bytes");
        }

        /// <summary>
        /// CPython 3.12: bytes % params - Objects/bytesobject.c bytes_format.
        /// Positional (tuple or single) and named (dict with bytes keys) forms.
        /// </summary>
        public override PyObject Modulo(PyObject other)
        {
            List<PyObject> seq = null;
            PyDict named = null;
            if (other is PyTuple tup)
            {
                seq = new List<PyObject>(tup.Items);
            }
            else if (other is PyDict dict)
            {
                named = dict;
            }
            else
            {
                seq = new List<PyObject> { other };
            }

            var fmt = Value;
            var outBytes = new List<byte>();
            int argIndex = 0;
            int i = 0;
            while (i < fmt.Length)
            {
                byte ch = fmt[i];
                if (ch != (byte)'%')
                {
                    outBytes.Add(ch);
                    i++;
                    continue;
                }
                i++;
                if (i >= fmt.Length)
                {
                    throw PyValueError.Create("incomplete format");
                }
                if (fmt[i] == (byte)'%')
                {
                    outBytes.Add((byte)'%');
                    i++;
                    continue;
                }

                PyObject arg;
                if (ch == (byte)'(')
                {
                    if (named == null)
                    {
                        throw PyTypeError.Create("format requires a mapping");
                    }
                    int close = i;
                    var keyBytes = new List<byte>();
                    int depth = 1;
                    while (close < fmt.Length && depth > 0)
                    {
                        if (fmt[close] == (byte)'(') depth++;
                        else if (fmt[close] == (byte)')') { depth--; if (depth == 0) break; }
                        keyBytes.Add(fmt[close]);
                        close++;
                    }
                    if (close >= fmt.Length)
                    {
                        throw PyValueError.Create("incomplete format key");
                    }
                    var key = new PyBytes(keyBytes.ToArray());
                    i = close + 1;
                    if (!named.InternalDict.TryGetValue(key, out arg))
                    {
                        throw PyKeyError.Create(key);
                    }
                }
                else if (named != null)
                {
                    throw PyTypeError.Create("format requires a mapping");
                }
                else
                {
                    if (argIndex >= seq.Count)
                    {
                        throw PyTypeError.Create("not enough arguments for format string");
                    }
                    arg = seq[argIndex++];
                }

                // flags
                bool alt = false, leftAlign = false, zeroPad = false, signPlus = false, signSpace = false;
                while (i < fmt.Length)
                {
                    char f = (char)fmt[i];
                    if (f == '#') { alt = true; i++; }
                    else if (f == '-') { leftAlign = true; i++; }
                    else if (f == '0') { zeroPad = true; i++; }
                    else if (f == '+') { signPlus = true; i++; }
                    else if (f == ' ') { signSpace = true; i++; }
                    else break;
                }
                int width = 0;
                while (i < fmt.Length && fmt[i] >= (byte)'0' && fmt[i] <= (byte)'9')
                {
                    width = width * 10 + (fmt[i] - (byte)'0');
                    i++;
                }
                int precision = -1;
                if (i < fmt.Length && fmt[i] == (byte)'.')
                {
                    i++;
                    precision = 0;
                    while (i < fmt.Length && fmt[i] >= (byte)'0' && fmt[i] <= (byte)'9')
                    {
                        precision = precision * 10 + (fmt[i] - (byte)'0');
                        i++;
                    }
                }
                if (i >= fmt.Length)
                {
                    throw PyValueError.Create("incomplete format");
                }
                char conv = (char)fmt[i++];

                string text;
                bool isNumeric = false;
                switch (conv)
                {
                    case 'a':
                    case 'r':
                        throw PyTypeError.Create($"{conv} formatting: a bytes-like object is required, not 'str'");
                    case 's':
                    case 'b':
                    {
                        // CPython converts %s to %b; the failure message always names %b.
                        if (arg is PyBytes pyb) text = System.Text.Encoding.Latin1.GetString(pyb.Value);
                        else if (arg is PyByteArray pyba) text = System.Text.Encoding.Latin1.GetString(pyba.ToByteArrayCopy());
                        else if (arg is PyTuple || arg is PyDict || arg is PyInt || arg is PyBool || arg is PyFloat
                                 || !(arg.LookupAttribute("__bytes__") != null || arg.SupportsBuffer()))
                        {
                            throw PyTypeError.Create($"%b requires a bytes-like object, or an object that implements __bytes__, not '{arg.GetTypeName()}'");
                        }
                        else
                        {
                            var asBytes = arg.GetAttribute("__bytes__");
                            var converted = asBytes != null && asBytes.IsCallable()
                                ? asBytes.Call(new PyObject[0], null)
                                : arg;
                            if (converted is PyBytes cb) text = System.Text.Encoding.Latin1.GetString(cb.Value);
                            else if (converted is PyByteArray cba) text = System.Text.Encoding.Latin1.GetString(cba.ToByteArrayCopy());
                            else
                            {
                                throw PyTypeError.Create($"%b requires a bytes-like object, or an object that implements __bytes__, not '{arg.GetTypeName()}'");
                            }
                        }
                        break;
                    }
                    case 'c':
                    {
                        int cv;
                        if (arg is PyBytes one && one.Value.Length == 1) cv = one.Value[0];
                        else if (TryGetIntArg(arg, out var ci))
                        {
                            if (ci < 0 || ci > 255)
                            {
                                throw PyOverflowError.Create("%c arg not in range(256)");
                            }
                            cv = (int)ci;
                        }
                        else
                        {
                            throw PyTypeError.Create($"%c requires an integer in range(256) or a single byte, not '{arg.GetTypeName()}'");
                        }
                        text = ((char)cv).ToString();
                        break;
                    }
                    case 'd':
                    case 'i':
                    case 'u':
                    {
                        if (!TryGetIntArg(arg, out var di))
                        {
                            throw PyTypeError.Create($"%{conv} format: a real number is required, not '{arg.GetTypeName()}'");
                        }
                        text = di.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        isNumeric = true;
                        break;
                    }
                    case 'o':
                    case 'x':
                    case 'X':
                    {
                        if (!TryGetIntArg(arg, out var bi))
                        {
                            throw PyTypeError.Create($"%{conv} format: a real number is required, not '{arg.GetTypeName()}'");
                        }
                        bool neg = bi < 0;
                        var digits = conv == 'o'
                            ? ToRadixString(System.Numerics.BigInteger.Abs(bi), 8)
                            : System.Numerics.BigInteger.Abs(bi).ToString("x");
                        if (conv == 'X') digits = digits.ToUpperInvariant();
                        text = (neg ? "-" : "") + (alt && bi != 0 ? (conv == 'o' ? "0" : conv == 'x' ? "0x" : "0X") : "") + digits;
                        isNumeric = true;
                        break;
                    }
                    case 'e':
                    case 'E':
                    case 'f':
                    case 'F':
                    case 'g':
                    case 'G':
                    {
                        if (!TryGetRealArg(arg, out double dv))
                        {
                            throw PyTypeError.Create($"%{conv} format: a real number is required, not '{arg.GetTypeName()}'");
                        }
                        var culture = System.Globalization.CultureInfo.InvariantCulture;
                        if (conv == 'f' || conv == 'F')
                        {
                            text = dv.ToString((precision >= 0 ? "F" + precision : "F6"), culture);
                        }
                        else if (conv == 'e' || conv == 'E')
                        {
                            text = dv.ToString((precision >= 0 ? "E" + precision : "E6"), culture);
                            text = NormalizeExp(text, conv == 'e');
                        }
                        else
                        {
                            text = dv.ToString(precision == 0 ? "G1" : "G" + (precision > 0 ? precision : 6), culture);
                            if (conv == 'G') text = text.ToUpperInvariant();
                        }
                        isNumeric = true;
                        break;
                    }
                    default:
                        throw PyValueError.Create($"unsupported format character '{conv}' (0x{(int)conv:x2}) at index {i - 1}");
                }

                if (isNumeric && (signPlus || signSpace))
                {
                    if (!text.StartsWith("-"))
                    {
                        text = (signPlus ? "+" : " ") + text;
                    }
                }
                if (text.Length < width)
                {
                    if (leftAlign) text = text.PadRight(width);
                    else if (zeroPad && isNumeric)
                    {
                        string sign = "";
                        if (text.Length > 0 && (text[0] == '-' || text[0] == '+' || text[0] == ' '))
                        {
                            sign = text.Substring(0, 1);
                            text = text.Substring(1);
                        }
                        text = sign + text.PadLeft(width - sign.Length, '0');
                    }
                    else text = text.PadLeft(width);
                }
                outBytes.AddRange(System.Text.Encoding.Latin1.GetBytes(text));
            }

            if (seq != null && argIndex < seq.Count)
            {
                throw PyTypeError.Create("not all arguments converted during bytes formatting");
            }
            return new PyBytes(outBytes.ToArray());
        }

        private static string ToRadixString(System.Numerics.BigInteger value, int radix)
        {
            if (value.IsZero)
            {
                return "0";
            }
            const string chars = "0123456789abcdef";
            var digits = new List<char>();
            var baseB = (System.Numerics.BigInteger)radix;
            while (value > 0)
            {
                digits.Add(chars[(int)(value % baseB)]);
                value /= baseB;
            }
            digits.Reverse();
            return new string(digits.ToArray());
        }

        private static bool TryGetIntArg(PyObject arg, out System.Numerics.BigInteger value)
        {
            if (arg is PyInt pi) { value = pi.Value; return true; }
            if (arg is PyBool pb) { value = pb.Value ? 1 : 0; return true; }
            value = 0;
            return false;
        }

        // CPython bytes find/count/index start/stop: accept int, bool, or __index__;
        // anything else -> TypeError. None is filtered by the caller (means default).
        // ---- p40/p41: CPython byte search/query family (shared by bytes+bytearray) ---
        // Objects/bytesobject.c owns find_impl / counting_impl / startswith_impl
        // and bytearray delegates to them, so the core lives on PyBytes.

        internal static void ParseRange(PyObject[] args, int length, out int start, out int end)
        {
            start = 0;
            end = length;
            if (args.Length >= 2 && args[1] is not PyNone) start = PyBytes.BytesIndex(args[1]);
            if (args.Length >= 3 && args[2] is not PyNone) end = PyBytes.BytesIndex(args[2]);
            if (start < 0) start += length;
            if (end < 0) end += length;
            start = Math.Max(0, Math.Min(start, length));
            end = Math.Max(0, Math.Min(end, length));
        }

        // find/count take int(0..255) or bytes-like; the prefix/suffix family takes
        // bytes-like only, and the two cases have different CPython messages.
        internal static byte[] SubArg(PyObject arg, bool allowInt)
        {
            if (arg is PyBytes pb) return pb.Value;
            if (arg is PyByteArray pba) return pba.ToByteArrayCopy();
            // Any object exposing a bytes-like buffer counts (CPythonPyObject_GetBuffer).
            if (arg is PyMemoryView mv) return mv.Data;
            if (allowInt && arg is PyInt pi)
            {
                long v = (long)pi.Value;
                if (v < 0 || v > 255) throw PyValueError.Create("byte must be in range(0, 256)");
                return new byte[] { (byte)v };
            }
            if (!allowInt)
                throw PyTypeError.Create($"a bytes-like object is required, not '{arg.GetTypeName()}'");
            throw PyTypeError.Create($"argument should be integer or bytes-like object, not '{arg.GetTypeName()}'");
        }

        internal static bool SameAt(System.Collections.Generic.IReadOnlyList<byte> data, int at, byte[] sub)
        {
            if (at < 0 || at + sub.Length > data.Count) return false;
            for (int i = 0; i < sub.Length; i++)
                if (data[at + i] != sub[i]) return false;
            return true;
        }

        // find/rfind and index/rindex share one scan.
        internal static PyObject FindCore(System.Collections.Generic.IReadOnlyList<byte> data, PyObject[] args, bool backward, bool raiseWhenMissing)
        {
            byte[] sub = SubArg(args[0], allowInt: true);
            ParseRange(args, data.Count, out int start, out int end);
            if (sub.Length == 0)
            {
                // CPython matches the empty subsection at the range boundary.
                return new PyInt(backward ? end : start);
            }
            if (backward)
            {
                for (int i = end - sub.Length; i >= start; i--)
                    if (SameAt(data, i, sub)) return new PyInt(i);
            }
            else
            {
                for (int i = start; i + sub.Length <= end; i++)
                    if (SameAt(data, i, sub)) return new PyInt(i);
            }
            if (raiseWhenMissing) throw PyValueError.Create("subsection not found");
            return new PyInt(-1);
        }

        internal static PyObject CountCore(System.Collections.Generic.IReadOnlyList<byte> data, PyObject[] args)
        {
            byte[] sub = SubArg(args[0], allowInt: true);
            ParseRange(args, data.Count, out int start, out int end);
            if (sub.Length == 0) return new PyInt(Math.Max(0, end - start + 1));
            int count = 0;
            for (int i = start; i + sub.Length <= end; i++)
            {
                if (SameAt(data, i, sub))
                {
                    count++;
                    i += sub.Length - 1; // non-overlapping
                }
            }
            return new PyInt(count);
        }

        // CPython startswith_impl: bytes-like or a tuple of bytes-like.
        internal static PyObject StartEndCore(System.Collections.Generic.IReadOnlyList<byte> data, PyObject[] args, bool prefix)
        {
            string name = prefix ? "startswith" : "endswith";
            var candidates = new List<byte[]>();
            if (args[0] is PyTuple tpl)
            {
                foreach (var item in tpl.Items)
                {
                    if (item is PyBytes or PyByteArray or PyMemoryView) candidates.Add(SubArg(item, allowInt: false));
                    else throw PyTypeError.Create($"a bytes-like object is required, not '{item.GetTypeName()}'");
                }
            }
            else if (args[0] is PyBytes or PyByteArray or PyMemoryView)
            {
                candidates.Add(SubArg(args[0], allowInt: false));
            }
            else
            {
                throw PyTypeError.Create($"{name} first arg must be bytes or a tuple of bytes, not {args[0].GetTypeName()}");
            }

            ParseRange(args, data.Count, out int start, out int end);
            foreach (var sub in candidates)
            {
                if (sub.Length > end - start) continue;
                if (prefix ? SameAt(data, start, sub) : SameAt(data, end - sub.Length, sub))
                    return PyBool.True;
            }
            return PyBool.False;
        }

        // removeprefix/removesuffix: METH_O, bytes-like only, returns a copy.
        internal static byte[] TrimEdgeBytes(System.Collections.Generic.IReadOnlyList<byte> data, PyObject[] args, bool prefix)
        {
            byte[] edge = SubArg(args[0], allowInt: false);
            var rest = new List<byte>(data);
            if (edge.Length > 0 && rest.Count >= edge.Length)
            {
                bool match = prefix
                    ? SameAt(rest, 0, edge)
                    : SameAt(rest, rest.Count - edge.Length, edge);
                if (match)
                {
                    if (prefix) rest.RemoveRange(0, edge.Length);
                    else rest.RemoveRange(rest.Count - edge.Length, edge.Length);
                }
            }
            return rest.ToArray();
        }

        internal static int BytesIndex(PyObject arg)
        {
            if (TryGetIntArg(arg, out var v)) return (int)v;
            var idx = PyObject.TryGetIndex(arg);
            if (idx == null)
                throw PyTypeError.Create("slice indices must be integers or None or have an __index__ method");
            return (int)idx.Value;
        }

        private static bool TryGetRealArg(PyObject arg, out double value)
        {
            if (arg is PyFloat pf) { value = pf.Value; return true; }
            if (arg is PyInt pi) { value = (double)pi.Value; return true; }
            if (arg is PyBool pb) { value = pb.Value ? 1.0 : 0.0; return true; }
            value = 0;
            return false;
        }

        private static string NormalizeExp(string text, bool lower)
        {
            // .NET may emit E+005; CPython uses e+05 (two-digit minimum).
            int eIdx = text.IndexOfAny(new[] { 'e', 'E' });
            if (eIdx < 0) return lower ? text : text.ToUpperInvariant();
            string mantissa = text.Substring(0, eIdx);
            string exp = text.Substring(eIdx + 1);
            char sign = '+';
            int start = 0;
            if (exp.Length > 0 && (exp[0] == '+' || exp[0] == '-'))
            {
                sign = exp[0];
                start = 1;
            }
            string digitsExp = exp.Substring(start).TrimStart('0');
            if (digitsExp.Length == 0) digitsExp = "0";
            if (digitsExp.Length < 2) digitsExp = digitsExp.PadLeft(2, '0');
            string result = mantissa + (lower ? "e" : "E") + sign + digitsExp;
            return lower ? result : result.ToUpperInvariant();
        }

        /// <summary>
        /// bytes * int repetition
        /// </summary>
        public override PyObject Multiply(PyObject other)
        {
            // CPython 3.12: Objects/bytesobject.c:1230-1250 - bytes_repeat
            if (other is PyInt count)
            {
                if (count.Value < 0)
                    return new PyBytes(new byte[0]);

                int countInt = (int)count.Value;
                var result = new byte[Value.Length * countInt];
                for (int i = 0; i < countInt; i++)
                {
                    Array.Copy(Value, 0, result, i * Value.Length, Value.Length);
                }
                return new PyBytes(result);
            }
            
            throw PyTypeError.Create($"can't multiply sequence by non-int of type '{other.GetTypeName()}'");
        }

        /// <summary>
        /// bytes equality comparison (used by equals operation)
        /// </summary>
        public override PyObject RichCompare(PyObject other, CompareOp op)
        {
            // bytes_richcompare leaves other types to reflected comparison.
            if (other is not PyBytes bytes) return PyNotImplemented.Instance;
            if (op == CompareOp.EQ || op == CompareOp.NE)
            {
                bool equal = ContentEquals(bytes);
                return PyBool.FromBool(op == CompareOp.EQ ? equal : !equal);
            }
            int order = Value.AsSpan().SequenceCompareTo(bytes.Value);
            return op switch
            {
                CompareOp.LT => PyBool.FromBool(order < 0),
                CompareOp.LE => PyBool.FromBool(order <= 0),
                CompareOp.GT => PyBool.FromBool(order > 0),
                CompareOp.GE => PyBool.FromBool(order >= 0),
                _ => throw PyValueError.Create($"Invalid comparison operator: {op}")
            };
        }

        public override bool Equals(object? obj)
        {
            if (obj is PyBytes other)
            {
                if (Value.Length != other.Value.Length) return false;
                for (int i = 0; i < Value.Length; i++)
                    if (Value[i] != other.Value[i]) return false;
                return true;
            }
            return false;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private bool ContentEquals(PyBytes other)
        {
            // bytes_compare_eq rejects different lengths/first bytes before
            // comparing the full buffer. Preserve these cheap rejection paths.
            if (Value.Length != other.Value.Length) return false;
            if (Value.Length == 0) return true;
            if (Value[0] != other.Value[0]) return false;
            return Value.AsSpan().SequenceEqual(other.Value);
        }

        public override int ToHash() => GetHashCode();

        public override int GetHashCode()
        {
            // Simple hash code for bytes
            int hash = 17;
            for (int i = 0; i < Value.Length; i++)
            {
                hash = hash * 31 + Value[i];
            }
            return hash;
        }

        /// <summary>
        /// bytes indexing - returns int (byte value)
        /// </summary>
        // CPython 3.12: Objects/bytesobject.c:1470-1510 - bytes_subscript
        public override PyObject GetItem(PyObject index)
        {
            if (index is PyInt pyInt)
            {
                var idx = pyInt.Value;
                if (idx < 0) idx += Value.Length;
                if (idx < 0 || idx >= Value.Length)
                    throw PyIndexError.Create("index out of range");

                return new PyInt(Value[(int)idx]);
            }
            else if (index is PySlice slice)
            {
                // Handle slice indexing
                var (start, stop, step) = slice.StorageIndices(Value.Length);
                
                if (step == 1)
                {
                    // Simple slice
                    var length = Math.Max(0, stop - start);
                    var result = new byte[length];
                    Array.Copy(Value, start, result, 0, length);
                    return new PyBytes(result);
                }
                else
                {
                    int count = PySlice.GetLength(start, stop, step);
                    var result = new byte[count];
                    for (int n = 0, position = start; n < count; n++, position = unchecked(position + step))
                        result[n] = Value[position];
                    return new PyBytes(result);
                }
            }
            
            throw PyTypeError.Create($"byte indices must be integers or slices, not {index.GetTypeName()}");
        }

        /// <summary>
        /// bytes containment check
        /// </summary>
        public override PyBool Contains(PyObject item)
        {
            if (item is PyBytes other)
            {
                // Check if 'other' bytes sequence is contained in this bytes
                return PyBool.FromBool(IndexOf(other.Value) >= 0);
            }
            else if (item is PyInt pyInt)
            {
                // Check if byte value is in bytes
                if (pyInt.Value < 0 || pyInt.Value > 255)
                    return PyBool.False;
                
                byte b = (byte)pyInt.Value;
                // Performance: Eliminated LINQ - manual loop instead of Array.Contains
                for (int i = 0; i < Value.Length; i++)
                {
                    if (Value[i] == b)
                        return PyBool.True;
                }
                return PyBool.False;
            }
            
            return PyBool.False;
        }

        /// <summary>
        /// Helper method to find index of byte sequence
        /// </summary>
        private int IndexOf(byte[] pattern)
        {
            for (int i = 0; i <= Value.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (Value[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        #endregion
    }
}
