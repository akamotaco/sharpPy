using System;

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

                    if (args.Length > 0 && args[0] is PyStr encStr)
                        encoding = encStr.Value;
                    if (args.Length > 1 && args[1] is PyStr errStr)
                        errors = errStr.Value;

                    // Handle encoding parameter
                    System.Text.Encoding enc = encoding.ToLower() switch
                    {
                        "utf-8" or "utf8" => System.Text.Encoding.UTF8,
                        "ascii" => System.Text.Encoding.ASCII,
                        "utf-16" or "utf16" => System.Text.Encoding.Unicode,
                        "utf-32" or "utf32" => System.Text.Encoding.UTF32,
                        "latin-1" or "latin1" or "iso-8859-1" => System.Text.Encoding.Latin1,
                        _ => throw PyLookupError.Create($"unknown encoding: {encoding}")
                    };

                    try
                    {
                        return new PyStr(enc.GetString(bytes.Value));
                    }
                    catch (System.Text.DecoderFallbackException ex)
                    {
                        if (errors == "strict")
                            throw PyUnicodeDecodeError.Create($"'{encoding}' codec can't decode bytes: {ex.Message}");
                        else if (errors == "ignore")
                            return new PyStr(enc.GetString(bytes.Value)); // Try without exceptions
                        else if (errors == "replace")
                            return new PyStr(enc.GetString(bytes.Value)); // Use replacement char
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
                    return new PyStr(sb.ToString());
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

                    if (args[0] is not PyStr hexStr)
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

            // CPython 3.12: Objects/bytesobject.c:1900-1950 - bytes_find
            // B.find(sub[, start[, end]]) -> int
            bytesType.TypeDict["find"] = new PyMethodDescriptor(
                "find", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'find' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"find() takes at least 1 argument ({args.Length} given)");

                    if (args[0] is not PyBytes sub)
                        throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");

                    int start = 0;
                    int end = bytes.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start += bytes.Value.Length;
                    if (end < 0) end += bytes.Value.Length;
                    start = Math.Max(0, Math.Min(start, bytes.Value.Length));
                    end = Math.Max(0, Math.Min(end, bytes.Value.Length));

                    // Search for substring
                    for (int i = start; i <= end - sub.Value.Length; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < sub.Value.Length; j++)
                        {
                            if (bytes.Value[i + j] != sub.Value[j])
                            {
                                match = false;
                                break;
                            }
                        }
                        if (match) return new PyInt(i);
                    }
                    return new PyInt(-1);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/bytesobject.c:2100-2150 - bytes_count
            // B.count(sub[, start[, end]]) -> int
            bytesType.TypeDict["count"] = new PyMethodDescriptor(
                "count", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'count' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"count() takes at least 1 argument ({args.Length} given)");

                    if (args[0] is not PyBytes sub)
                        throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");

                    int start = 0;
                    int end = bytes.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start += bytes.Value.Length;
                    if (end < 0) end += bytes.Value.Length;
                    start = Math.Max(0, Math.Min(start, bytes.Value.Length));
                    end = Math.Max(0, Math.Min(end, bytes.Value.Length));

                    // Count occurrences
                    int count = 0;
                    for (int i = start; i <= end - sub.Value.Length; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < sub.Value.Length; j++)
                        {
                            if (bytes.Value[i + j] != sub.Value[j])
                            {
                                match = false;
                                break;
                            }
                        }
                        if (match)
                        {
                            count++;
                            i += sub.Value.Length - 1; // Skip past this match
                        }
                    }
                    return new PyInt(count);
                },
                minArgs: 1, maxArgs: 3
            );

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

            // CPython 3.12: Objects/bytesobject.c:2000-2030 - bytes_startswith
            // B.startswith(prefix[, start[, end]]) -> bool
            bytesType.TypeDict["startswith"] = new PyMethodDescriptor(
                "startswith", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'startswith' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"startswith() takes at least 1 argument ({args.Length} given)");

                    if (args[0] is not PyBytes prefix)
                        throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");

                    int start = 0;
                    int end = bytes.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start += bytes.Value.Length;
                    if (end < 0) end += bytes.Value.Length;
                    start = Math.Max(0, Math.Min(start, bytes.Value.Length));
                    end = Math.Max(0, Math.Min(end, bytes.Value.Length));

                    if (end - start < prefix.Value.Length)
                        return PyBool.False;

                    for (int i = 0; i < prefix.Value.Length; i++)
                    {
                        if (bytes.Value[start + i] != prefix.Value[i])
                            return PyBool.False;
                    }
                    return PyBool.True;
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/bytesobject.c:2050-2080 - bytes_endswith
            // B.endswith(suffix[, start[, end]]) -> bool
            bytesType.TypeDict["endswith"] = new PyMethodDescriptor(
                "endswith", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'endswith' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"endswith() takes at least 1 argument ({args.Length} given)");

                    if (args[0] is not PyBytes suffix)
                        throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");

                    int start = 0;
                    int end = bytes.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start += bytes.Value.Length;
                    if (end < 0) end += bytes.Value.Length;
                    start = Math.Max(0, Math.Min(start, bytes.Value.Length));
                    end = Math.Max(0, Math.Min(end, bytes.Value.Length));

                    if (end - start < suffix.Value.Length)
                        return PyBool.False;

                    int offset = end - suffix.Value.Length;
                    for (int i = 0; i < suffix.Value.Length; i++)
                    {
                        if (bytes.Value[offset + i] != suffix.Value[i])
                            return PyBool.False;
                    }
                    return PyBool.True;
                },
                minArgs: 1, maxArgs: 3
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

                    var iterable = args[0];
                    var bytesList = new System.Collections.Generic.List<PyBytes>();

                    if (iterable is PyList list)
                    {
                        for (int i = 0; i < list.Items.Length; i++)
                        {
                            if (list.Items[i] is not PyBytes item)
                                throw PyTypeError.Create($"sequence item {i}: expected a bytes-like object, {list.Items[i].GetTypeName()} found");
                            bytesList.Add(item);
                        }
                    }
                    else if (iterable is PyTuple tuple)
                    {
                        for (int i = 0; i < tuple.Items.Length; i++)
                        {
                            if (tuple.Items[i] is not PyBytes item)
                                throw PyTypeError.Create($"sequence item {i}: expected a bytes-like object, {tuple.Items[i].GetTypeName()} found");
                            bytesList.Add(item);
                        }
                    }
                    else
                    {
                        // General iterable (generator, etc.)
                        try
                        {
                            var iter = iterable.GetIterator();
                            int idx = 0;
                            while (iter.TryNext(out var item))
                            {
                                if (item is not PyBytes bytesItem)
                                    throw PyTypeError.Create($"sequence item {idx}: expected a bytes-like object, {item.GetTypeName()} found");
                                bytesList.Add(bytesItem);
                                idx++;
                            }
                        }
                        catch (PythonException ex) when (ex.PyException is PyTypeError)
                        {
                            throw;
                        }
                        catch (PythonException ex) when (ex.PyException is PyStopIteration)
                        {
                            // iteration complete
                        }
                        catch (PythonException)
                        {
                            throw PyTypeError.Create("can only join an iterable");
                        }
                    }

                    var result = new System.Collections.Generic.List<byte>();
                    for (int i = 0; i < bytesList.Count; i++)
                    {
                        if (i > 0)
                            result.AddRange(separator.Value);
                        result.AddRange(bytesList[i].Value);
                    }

                    return new PyBytes(result.ToArray());
                },
                minArgs: 1, maxArgs: 1
            );

            // CPython 3.12: Objects/bytesobject.c:1950-2000 - bytes_index
            // B.index(sub[, start[, end]]) -> int
            bytesType.TypeDict["index"] = new PyMethodDescriptor(
                "index", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'index' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"index() takes at least 1 argument ({args.Length} given)");

                    if (args[0] is not PyBytes sub)
                        throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");

                    int start = 0;
                    int end = bytes.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start += bytes.Value.Length;
                    if (end < 0) end += bytes.Value.Length;
                    start = Math.Max(0, Math.Min(start, bytes.Value.Length));
                    end = Math.Max(0, Math.Min(end, bytes.Value.Length));

                    // Search for substring
                    for (int i = start; i <= end - sub.Value.Length; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < sub.Value.Length; j++)
                        {
                            if (bytes.Value[i + j] != sub.Value[j])
                            {
                                match = false;
                                break;
                            }
                        }
                        if (match) return new PyInt(i);
                    }
                    throw PyValueError.Create("subsection not found");
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/bytesobject.c:1900-1950 - bytes_rfind
            // B.rfind(sub[, start[, end]]) -> int
            bytesType.TypeDict["rfind"] = new PyMethodDescriptor(
                "rfind", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'rfind' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"rfind() takes at least 1 argument ({args.Length} given)");

                    if (args[0] is not PyBytes sub)
                        throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");

                    int start = 0;
                    int end = bytes.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start += bytes.Value.Length;
                    if (end < 0) end += bytes.Value.Length;
                    start = Math.Max(0, Math.Min(start, bytes.Value.Length));
                    end = Math.Max(0, Math.Min(end, bytes.Value.Length));

                    // Search backwards for substring
                    for (int i = end - sub.Value.Length; i >= start; i--)
                    {
                        bool match = true;
                        for (int j = 0; j < sub.Value.Length; j++)
                        {
                            if (bytes.Value[i + j] != sub.Value[j])
                            {
                                match = false;
                                break;
                            }
                        }
                        if (match) return new PyInt(i);
                    }
                    return new PyInt(-1);
                },
                minArgs: 1, maxArgs: 3
            );

            // CPython 3.12: Objects/bytesobject.c:2000-2050 - bytes_rindex
            // B.rindex(sub[, start[, end]]) -> int
            bytesType.TypeDict["rindex"] = new PyMethodDescriptor(
                "rindex", bytesType,
                (self, args, kwargs) => {
                    if (self is not PyBytes bytes)
                        throw PyTypeError.Create($"descriptor 'rindex' requires a 'bytes' object but received a '{self.GetTypeName()}'");

                    if (args.Length < 1 || args.Length > 3)
                        throw PyTypeError.Create($"rindex() takes at least 1 argument ({args.Length} given)");

                    if (args[0] is not PyBytes sub)
                        throw PyTypeError.Create($"a bytes-like object is required, not '{args[0].GetTypeName()}'");

                    int start = 0;
                    int end = bytes.Value.Length;

                    if (args.Length >= 2 && args[1] is PyInt startInt)
                        start = (int)startInt.Value;
                    if (args.Length >= 3 && args[2] is PyInt endInt)
                        end = (int)endInt.Value;

                    // Normalize indices
                    if (start < 0) start += bytes.Value.Length;
                    if (end < 0) end += bytes.Value.Length;
                    start = Math.Max(0, Math.Min(start, bytes.Value.Length));
                    end = Math.Max(0, Math.Min(end, bytes.Value.Length));

                    // Search backwards for substring
                    for (int i = end - sub.Value.Length; i >= start; i--)
                    {
                        bool match = true;
                        for (int j = 0; j < sub.Value.Length; j++)
                        {
                            if (bytes.Value[i + j] != sub.Value[j])
                            {
                                match = false;
                                break;
                            }
                        }
                        if (match) return new PyInt(i);
                    }
                    throw PyValueError.Create("subsection not found");
                },
                minArgs: 1, maxArgs: 3
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

            // CPython 3.12: Objects/bytesobject.c:2100-2140 (bytes_removeprefix_impl)
            // bytes.removeprefix(prefix) - Remove prefix if present (Python 3.9+)
            bytesType.TypeDict["removeprefix"] = new PyMethodDescriptor(
                "removeprefix",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'removeprefix' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"removeprefix() takes exactly 1 argument ({args.Length} given)");

                    if (args[0] is not PyBytes prefix)
                        throw PyTypeError.Create("removeprefix() argument must be bytes");

                    byte[] data = selfBytes.Value;
                    byte[] prefixData = prefix.Value;

                    if (prefixData.Length == 0 || prefixData.Length > data.Length)
                        return selfBytes;

                    // Check if starts with prefix
                    bool matches = true;
                    for (int i = 0; i < prefixData.Length; i++)
                    {
                        if (data[i] != prefixData[i])
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (!matches)
                        return selfBytes;

                    // Remove prefix
                    byte[] result = new byte[data.Length - prefixData.Length];
                    Array.Copy(data, prefixData.Length, result, 0, result.Length);
                    return new PyBytes(result);
                },
                minArgs: 1,
                maxArgs: 1
            );

            // CPython 3.12: Objects/bytesobject.c:2150-2190 (bytes_removesuffix_impl)
            // bytes.removesuffix(suffix) - Remove suffix if present (Python 3.9+)
            bytesType.TypeDict["removesuffix"] = new PyMethodDescriptor(
                "removesuffix",
                bytesType,
                (self, args, kwargs) =>
                {
                    if (self is not PyBytes selfBytes)
                        throw PyTypeError.Create("descriptor 'removesuffix' for 'bytes' objects doesn't apply to a '" + self.GetTypeName() + "' object");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"removesuffix() takes exactly 1 argument ({args.Length} given)");

                    if (args[0] is not PyBytes suffix)
                        throw PyTypeError.Create("removesuffix() argument must be bytes");

                    byte[] data = selfBytes.Value;
                    byte[] suffixData = suffix.Value;

                    if (suffixData.Length == 0 || suffixData.Length > data.Length)
                        return selfBytes;

                    // Check if ends with suffix
                    bool matches = true;
                    int startPos = data.Length - suffixData.Length;
                    for (int i = 0; i < suffixData.Length; i++)
                    {
                        if (data[startPos + i] != suffixData[i])
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (!matches)
                        return selfBytes;

                    // Remove suffix
                    byte[] result = new byte[data.Length - suffixData.Length];
                    Array.Copy(data, 0, result, 0, result.Length);
                    return new PyBytes(result);
                },
                minArgs: 1,
                maxArgs: 1
            );

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
            return new PyListIterator(new PyList(items));
        }
        
        public override PyStr ToRepr()
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
            return new PyStr(sb.ToString());
        }
        
        public override int Length() => Value.Length;
        public override bool PyBoolValue() => Value.Length > 0;

        #region Buffer Protocol (PEP 688)

        /// <summary>
        /// PEP 688: bytes objects implement the buffer protocol
        /// </summary>
        public override PyMemoryView GetBuffer(int flags)
        {
            // bytes objects are read-only buffers
            return new PyMemoryView(Value, true);
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
            
            throw PyTypeError.Create($"can't concat bytes to {other.GetTypeName()}");
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
        public override bool Equals(object? obj)
        {
            if (obj is PyBytes other)
            {
                if (Value.Length != other.Value.Length)
                    return false;
                
                for (int i = 0; i < Value.Length; i++)
                {
                    if (Value[i] != other.Value[i])
                        return false;
                }
                return true;
            }
            return false;
        }

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
                var (start, stop, step) = slice.Indices(Value.Length);
                
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
                    // Step slice
                    var resultList = new List<byte>();
                    if (step > 0)
                    {
                        for (int i = start; i < stop; i += step)
                        {
                            resultList.Add(Value[i]);
                        }
                    }
                    else
                    {
                        for (int i = start; i > stop; i += step)
                        {
                            resultList.Add(Value[i]);
                        }
                    }
                    return new PyBytes(resultList.ToArray());
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