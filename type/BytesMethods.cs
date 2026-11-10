// Objects/bytes_methods.c, Objects/stringlib/{find,split,partition,transmogrifier,join,ctype}.h,
// Python/pystrhex.c and the constructors of Objects/bytesobject.c and bytearrayobject.c, CPython v3.12.15.
// As in CPython, bytes and bytearray are two instances of one implementation.
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace SharpPy
{
    internal static class BytesMethods
    {
        private static bool IsSpace(byte c) => c == ' ' || (c >= 9 && c <= 13);
        private static bool IsAlpha(byte c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        private static bool IsDigit(byte c) => c >= '0' && c <= '9';
        private static bool IsLower(byte c) => c >= 'a' && c <= 'z';
        private static bool IsUpper(byte c) => c >= 'A' && c <= 'Z';
        private static byte ToLower(byte c) => IsUpper(c) ? (byte)(c + 32) : c;
        private static byte ToUpper(byte c) => IsLower(c) ? (byte)(c - 32) : c;

        private static byte[] Self(PyObject self) => self is PyBytes bytes ? bytes.Value : ((PyByteArray)self).ToByteArrayCopy();

        // STRINGLIB_NEW
        private static PyObject New(PyObject self, byte[] value) =>
            self is PyBytes ? PyBytes.From(value) : new PyByteArray(value);

        // The result of a method that fills a fresh PyBytes_FromStringAndSize(NULL, n).
        private static PyObject NewFresh(PyObject self, byte[] value) =>
            self is PyBytes ? PyBytes.Fresh(value) : new PyByteArray(value);

        // return_self: bytes is immutable, an exact one comes back as it is.
        private static PyObject ReturnSelf(PyObject self, byte[] value) =>
            self is PyBytes && ReferenceEquals(self.GetPyType(), PyType.BytesType) ? self : New(self, value);

        private static PyObject BoolResult(bool value) => PyBool.FromBool(value);

        // PyObject_GetBuffer
        internal static byte[] Buffer(PyObject value)
        {
            var data = PyBytes.BufferOrNull(value);
            if (data != null)
                return data;
            if (PyMemoryView.FromBufferSlot(value, 0) is { } exported)
                return exported.Data;
            if (value is PyString || !value.SupportsBuffer())
                return null;
            return value.GetBuffer(0).Data;
        }

        /// <summary>
        /// PyObject_Bytes's __bytes__ step: the method found on the type (_PyObject_LookupSpecial),
        /// whose answer must be bytes. Null when the type has none.
        /// </summary>
        internal static PyBytes BytesSlot(PyObject value)
        {
            if (value.LookupSpecialMethodUncached("__bytes__") is not PyObject method)
                return null;
            var result = value.BindSpecialMethod(method).Call(new PyObject[0], null);
            return result as PyBytes
                ?? throw PyTypeError.Create($"__bytes__ returned non-bytes (type {result.GetTypeName()})");
        }

        /// <summary>
        /// PyObject_Bytes (Objects/object.c): an exact bytes is itself, then __bytes__, then
        /// PyBytes_FromObject (a buffer, a list or tuple, an iterable of ints; not a str).
        /// </summary>
        internal static PyBytes ObjectBytes(PyObject value)
        {
            if (value.IsExactly(PyType.BytesType))
                return (PyBytes)value;
            return BytesSlot(value) ?? PyBytes.From(FromObject(value, "bytes"));
        }

        /// <summary>PyObject_GetBuffer, the Py_buffer converter: what any buffer holds, else its refusal.</summary>
        internal static byte[] RequireBuffer(PyObject value) =>
            Buffer(value) ?? throw PyTypeError.Create($"a bytes-like object is required, not '{value.GetTypeName()}'");

        /// <summary>Py_buffer(accept={str, buffer}): a str is its UTF-8 (PyUnicode_AsUTF8AndSize).</summary>
        internal static byte[] StrOrBuffer(PyObject value) =>
            value is PyString text ? text.Encode("utf-8").Value : RequireBuffer(value);

        private static bool HasIndex(PyObject value) => PyObject.IndexCheck(value);

        private static BigInteger IndexValue(PyObject value) =>
            (PyObject.IndexOrThrow(value)).Value;


        // _PyEval_SliceIndex
        private static long SliceIndex(PyObject value, long missing) => PyArgs.SliceIndex(value, missing);

        // ADJUST_INDICES
        private static void Adjust(ref long start, ref long end, long length)
        {
            if (end > length)
                end = length;
            else if (end < 0)
            {
                end += length;
                if (end < 0)
                    end = 0;
            }
            if (start < 0)
            {
                start += length;
                if (start < 0)
                    start = 0;
            }
        }

        private static void Range(PyObject[] args, int first, string name, out long start, out long end)
        {
            if (args.Length > first + 2)
                throw PyTypeError.Create($"{name}() takes at most {first + 2} arguments ({args.Length} given)");
            start = SliceIndex(args.Length > first ? args[first] : null, 0);
            end = SliceIndex(args.Length > first + 1 ? args[first + 1] : null, long.MaxValue);
        }

        // parse_args_finds_byte
        private static byte[] Needle(PyObject sub)
        {
            var data = Buffer(sub);
            if (data != null)
                return data;
            if (!HasIndex(sub))
                throw PyTypeError.Create($"argument should be integer or bytes-like object, not '{sub.GetTypeName()}'");
            var value = IndexValue(sub);
            if (value < 0 || value > 255)
                throw PyValueError.Create("byte must be in range(0, 256)");
            return new[] { (byte)value };
        }

        // find_internal
        private static long Find(byte[] text, string name, PyObject[] args, bool forward)
        {
            if (args.Length < 1)
                throw PyTypeError.Create($"{name}() takes at least 1 argument (0 given)");
            // stringlib_parse_args_finds reads start and end before parse_args_finds_byte reads sub.
            Range(args, 1, name, out long start, out long end);
            var sub = Needle(args[0]);
            Adjust(ref start, ref end, text.Length);
            if (end - start < sub.Length)
                return -1;
            if (sub.Length == 0)
                return forward ? start : end;
            var range = text.AsSpan((int)start, (int)(end - start));
            int found = forward ? PyBytes.IndexOfBytes(range, sub) : PyBytes.LastIndexOfBytes(range, sub);
            return found < 0 ? -1 : found + start;
        }

        private static PyObject Index(byte[] text, string name, PyObject[] args, bool forward)
        {
            long found = Find(text, name, args, forward);
            if (found == -1)
                throw PyValueError.Create("subsection not found");
            return PyInt.From(found);
        }

        private static PyObject Count(byte[] text, PyObject[] args)
        {
            if (args.Length < 1)
                throw PyTypeError.Create("count() takes at least 1 argument (0 given)");
            // stringlib_parse_args_finds reads start and end before parse_args_finds_byte reads sub.
            Range(args, 1, "count", out long start, out long end);
            var sub = Needle(args[0]);
            Adjust(ref start, ref end, text.Length);
            long length = end - start;
            if (length < 0)
                return PyInt.From(0);
            if (sub.Length == 0)
                return PyInt.From(length + 1);
            long count = 0;
            var range = text.AsSpan((int)start, (int)length);
            while (true)
            {
                int found = PyBytes.IndexOfBytes(range, sub);
                if (found < 0)
                    break;
                count++;
                range = range.Slice(found + sub.Length);
            }
            return PyInt.From(count);
        }

        // tailmatch
        private static bool TailMatch(byte[] text, byte[] sub, long start, long end, bool front)
        {
            long length = text.Length;
            Adjust(ref start, ref end, length);
            if (front)
            {
                if (start > length - sub.Length)
                    return false;
            }
            else
            {
                if (end - start < sub.Length || start > length)
                    return false;
                if (end - sub.Length > start)
                    start = end - sub.Length;
            }
            if (end - start < sub.Length)
                return false;
            return text.AsSpan((int)start, sub.Length).SequenceEqual(sub);
        }

        // _Py_bytes_tailmatch
        private static PyObject StartsEnds(byte[] text, string name, PyObject[] args, bool front)
        {
            if (args.Length < 1)
                throw PyTypeError.Create($"{name}() takes at least 1 argument (0 given)");
            Range(args, 1, name, out long start, out long end);
            if (args[0] is PyTuple tuple)
            {
                foreach (var item in tuple.Items)
                {
                    var part = RequireBuffer(item);
                    if (TailMatch(text, part, start, end, front))
                        return PyBool.True;
                }
                return PyBool.False;
            }
            var sub = Buffer(args[0]) ?? throw PyTypeError.Create($"{name} first arg must be bytes or a tuple of bytes, not {args[0].GetTypeName()}");
            return BoolResult(TailMatch(text, sub, start, end, front));
        }

        // _Py_bytes_contains
        internal static bool Contains(byte[] text, PyObject value)
        {
            if (HasIndex(value) && Buffer(value) == null)
            {
                var number = IndexValue(value);
                if (number < 0 || number > 255)
                    throw PyValueError.Create("byte must be in range(0, 256)");
                return Array.IndexOf(text, (byte)number) >= 0;
            }
            var data = RequireBuffer(value);
            return PyBytes.IndexOfBytes(text, data) >= 0;
        }

        private static long MaxSplit(PyObject value)
        {
            if (value == null)
                return long.MaxValue;
            long count = PyArgs.Ssize(value);
            return count < 0 ? long.MaxValue : count;
        }

        // stringlib_split_whitespace and stringlib_split
        private static PyObject Split(PyObject self, PyObject separator, PyObject maxsplit)
        {
            var text = Self(self);
            long left = MaxSplit(maxsplit);
            var parts = new List<PyObject>();
            if (separator == null || separator is PyNone)
            {
                int i = 0;
                while (left-- > 0)
                {
                    while (i < text.Length && IsSpace(text[i]))
                        i++;
                    if (i == text.Length)
                        break;
                    int j = i;
                    i++;
                    while (i < text.Length && !IsSpace(text[i]))
                        i++;
                    parts.Add(Piece(self, text, j, i));
                }
                if (i < text.Length)
                {
                    while (i < text.Length && IsSpace(text[i]))
                        i++;
                    if (i != text.Length)
                        parts.Add(Piece(self, text, i, text.Length));
                }
                return new PyList(parts.ToArray());
            }
            var sep = RequireBuffer(separator);
            if (sep.Length == 0)
                throw PyValueError.Create("empty separator");
            int position = 0;
            while (left-- > 0)
            {
                int found = PyBytes.IndexOfBytes(text.AsSpan(position), sep);
                if (found < 0)
                    break;
                parts.Add(Piece(self, text, position, position + found));
                position += found + sep.Length;
            }
            parts.Add(position == 0 ? ReturnSelf(self, text) : Piece(self, text, position, text.Length));
            return new PyList(parts.ToArray());
        }

        private static PyObject RSplit(PyObject self, PyObject separator, PyObject maxsplit)
        {
            var text = Self(self);
            long left = MaxSplit(maxsplit);
            var parts = new List<PyObject>();
            if (separator == null || separator is PyNone)
            {
                int i = text.Length - 1;
                while (left-- > 0)
                {
                    while (i >= 0 && IsSpace(text[i]))
                        i--;
                    if (i < 0)
                        break;
                    int j = i;
                    i--;
                    while (i >= 0 && !IsSpace(text[i]))
                        i--;
                    parts.Add(Piece(self, text, i + 1, j + 1));
                }
                if (i >= 0)
                {
                    while (i >= 0 && IsSpace(text[i]))
                        i--;
                    if (i >= 0)
                        parts.Add(Piece(self, text, 0, i + 1));
                }
                parts.Reverse();
                return new PyList(parts.ToArray());
            }
            var sep = RequireBuffer(separator);
            if (sep.Length == 0)
                throw PyValueError.Create("empty separator");
            int end = text.Length;
            while (left-- > 0)
            {
                int found = PyBytes.LastIndexOfBytes(text.AsSpan(0, end), sep);
                if (found < 0)
                    break;
                parts.Add(Piece(self, text, found + sep.Length, end));
                end = found;
            }
            parts.Add(end == text.Length ? ReturnSelf(self, text) : Piece(self, text, 0, end));
            parts.Reverse();
            return new PyList(parts.ToArray());
        }

        private static PyObject Piece(PyObject self, byte[] text, int from, int to) =>
            New(self, text.AsSpan(from, to - from).ToArray());

        // stringlib_splitlines
        private static PyObject SplitLines(PyObject self, bool keepends)
        {
            var text = Self(self);
            var lines = new List<PyObject>();
            int i = 0;
            while (i < text.Length)
            {
                int j = i;
                while (i < text.Length && text[i] != '\n' && text[i] != '\r')
                    i++;
                int eol = i;
                if (i < text.Length)
                {
                    if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i += 2;
                    else
                        i++;
                    if (keepends)
                        eol = i;
                }
                lines.Add(Piece(self, text, j, eol));
            }
            return new PyList(lines.ToArray());
        }

        // stringlib_partition and stringlib_rpartition
        private static PyObject Partition(PyObject self, PyObject separator, bool forward)
        {
            var text = Self(self);
            var sep = RequireBuffer(separator);
            if (sep.Length == 0)
                throw PyValueError.Create("empty separator");
            int found = forward ? PyBytes.IndexOfBytes(text, sep) : PyBytes.LastIndexOfBytes(text, sep);
            if (found < 0)
                return forward
                    ? PyTuple.From(new[] { ReturnSelf(self, text), New(self, new byte[0]), New(self, new byte[0]) })
                    : PyTuple.From(new[] { New(self, new byte[0]), New(self, new byte[0]), ReturnSelf(self, text) });
            return PyTuple.From(new[] { Piece(self, text, 0, found), New(self, sep), Piece(self, text, found + sep.Length, text.Length) });
        }

        // do_strip and do_xstrip
        private static PyObject Strip(PyObject self, PyObject chars, bool left, bool right)
        {
            var text = Self(self);
            Func<byte, bool> strip = IsSpace;
            if (chars != null && chars is not PyNone)
            {
                var set = RequireBuffer(chars);
                strip = value => Array.IndexOf(set, value) >= 0;
            }
            int i = 0, j = text.Length;
            if (left)
                while (i < j && strip(text[i]))
                    i++;
            if (right)
                while (j > i && strip(text[j - 1]))
                    j--;
            if (i == 0 && j == text.Length)
                return ReturnSelf(self, text);
            return Piece(self, text, i, j);
        }

        private static byte FillByte(string name, PyObject[] args)
        {
            if (args.Length < 2)
                return (byte)' ';
            var fill = args[1];
            if (fill is PyBytes { Value.Length: 1 } bytes)
                return bytes.Value[0];
            if (fill is PyByteArray array && array.Length() == 1)
                return array.ToByteArrayCopy()[0];
            throw PyArgs.BadArgument(name, "argument 2", "a byte string of length 1", fill);
        }

        // pad
        private static PyObject Pad(PyObject self, string name, PyObject[] args, Func<long, (long Left, long Right)> split)
        {
            if (args.Length < 1)
                throw PyTypeError.Create($"{name}() takes at least 1 argument (0 given)");
            if (args.Length > 2)
                throw PyTypeError.Create($"{name} expected at most 2 arguments, got {args.Length}");
            long width = PyArgs.Ssize(args[0]);
            byte fill = FillByte(name, args);
            var text = Self(self);
            long missing = width - text.Length;
            if (missing <= 0)
                return ReturnSelf(self, text);
            var (left, right) = split(missing);
            var result = new byte[PyAllocation.ArrayLength(width)];
            Array.Fill(result, fill);
            Array.Copy(text, 0, result, left, text.Length);
            return New(self, result);
        }

        private static PyObject ZFill(PyObject self, PyObject widthArgument)
        {
            long width = PyArgs.Ssize(widthArgument);
            var text = Self(self);
            if (width <= text.Length)
                return ReturnSelf(self, text);
            long fill = width - text.Length;
            var result = new byte[PyAllocation.ArrayLength(width)];
            Array.Fill(result, (byte)'0');
            Array.Copy(text, 0, result, fill, text.Length);
            if (text.Length != 0 && (text[0] == '+' || text[0] == '-'))
            {
                result[0] = text[0];
                result[fill] = (byte)'0';
            }
            return New(self, result);
        }

        // stringlib_expandtabs
        private static PyObject ExpandTabs(PyObject self, long tabsize)
        {
            var text = Self(self);
            var result = new List<byte>(text.Length);
            long column = 0;
            foreach (byte c in text)
            {
                if (c == '\t')
                {
                    if (tabsize > 0)
                    {
                        long incr = tabsize - (column % tabsize);
                        column += incr;
                        for (long i = 0; i < incr; i++)
                            result.Add((byte)' ');
                    }
                }
                else
                {
                    column++;
                    result.Add(c);
                    if (c == '\n' || c == '\r')
                        column = 0;
                }
            }
            return New(self, result.ToArray());
        }

        // stringlib_replace
        private static PyObject Replace(PyObject self, PyObject[] args)
        {
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create(args.Length < 2 ? $"replace expected at least 2 arguments, got {args.Length}" : $"replace expected at most 3 arguments, got {args.Length}");
            var from = RequireBuffer(args[0]);
            var to = RequireBuffer(args[1]);
            long count = args.Length > 2 ? PyArgs.Ssize(args[2]) : -1;
            var text = Self(self);
            if (count < 0)
                count = long.MaxValue;
            if (text.Length < from.Length || count == 0)
                return ReturnSelf(self, text);
            if (from.Length == 0)
            {
                if (to.Length == 0)
                    return ReturnSelf(self, text);
                var interleaved = new List<byte>();
                long done = 0;
                for (int i = 0; i <= text.Length; i++)
                {
                    if (done < count)
                    {
                        interleaved.AddRange(to);
                        done++;
                    }
                    if (i < text.Length)
                        interleaved.Add(text[i]);
                }
                return NewFresh(self, interleaved.ToArray());
            }
            if (text.Length == 0)
                return ReturnSelf(self, text);
            var result = new List<byte>(text.Length);
            int position = 0;
            long replaced = 0;
            while (replaced < count)
            {
                int found = PyBytes.IndexOfBytes(text.AsSpan(position), from);
                if (found < 0)
                    break;
                result.AddRange(text.AsSpan(position, found).ToArray());
                result.AddRange(to);
                position += found + from.Length;
                replaced++;
            }
            if (replaced == 0)
                return ReturnSelf(self, text);
            result.AddRange(text.AsSpan(position).ToArray());
            // stringlib replace fills a fresh PyBytes_FromStringAndSize(NULL, n).
            return NewFresh(self, result.ToArray());
        }

        // bytes_translate_impl
        private static PyObject Translate(PyObject self, PyObject[] args, PyDict kwargs)
        {
            PyObject tableArgument = args.Length > 0 ? args[0] : null;
            if (tableArgument == null)
                throw PyArgs.MissingArgument("translate", "table", 1);
            PyObject deleteArgument = args.Length > 1 ? args[1] : kwargs != null ? PyObject.KwValue(kwargs, "delete") : null;
            var text = Self(self);
            byte[] table = null;
            if (tableArgument is not PyNone)
            {
                table = RequireBuffer(tableArgument);
                if (table.Length != 256)
                    throw PyValueError.Create("translation table must be 256 characters long");
            }
            var delete = new bool[256];
            bool anyDelete = false;
            if (deleteArgument != null)
                foreach (byte b in RequireBuffer(deleteArgument))
                    anyDelete = delete[b] = true;
            var result = new List<byte>(text.Length);
            bool changed = false;
            foreach (byte b in text)
            {
                if (delete[b])
                {
                    changed = true;
                    continue;
                }
                byte mapped = table != null ? table[b] : b;
                changed |= mapped != b;
                result.Add(mapped);
            }
            return changed || anyDelete && result.Count != text.Length ? NewFresh(self, result.ToArray()) : ReturnSelf(self, text);
        }

        // stringlib_bytes_join (_PyBytes_Join)
        internal static PyObject Join(PyObject self, PyObject iterable)
        {
            PyObject[] items;
            // bytearray_join: the buffer of self stays exported while the iterable runs.
            var array = self as PyByteArray;
            array?.HoldExport();
            try
            {
                items = PyList.SequenceFast(iterable, "can only join an iterable");
            }
            finally
            {
                array?.ReleaseExport();
            }
            if (items.Length == 1 && ReferenceEquals(items[0].GetPyType(), PyType.BytesType) && self is PyBytes)
                return items[0];
            var separator = Self(self);
            var result = new List<byte>();
            for (int i = 0; i < items.Length; i++)
            {
                // bytes_join: a failing PyObject_GetBuffer is reported as this item's TypeError.
                byte[] part;
                try { part = Buffer(items[i]); }
                catch (PythonException) { part = null; }
                if (part == null)
                    throw PyTypeError.Create($"sequence item {i}: expected a bytes-like object, {items[i].GetTypeName()} found");
                if (i != 0)
                    result.AddRange(separator);
                result.AddRange(part);
            }
            return New(self, result.ToArray());
        }

        private static bool All(byte[] text, Func<byte, bool> test)
        {
            if (text.Length == 0)
                return false;
            foreach (byte b in text)
                if (!test(b))
                    return false;
            return true;
        }

        private static bool CaseTest(byte[] text, Func<byte, bool> cased, Func<byte, bool> other)
        {
            bool found = false;
            foreach (byte b in text)
            {
                if (other(b))
                    return false;
                found |= cased(b);
            }
            return found;
        }

        // _Py_bytes_istitle
        private static bool IsTitle(byte[] text)
        {
            bool cased = false, previousCased = false;
            foreach (byte c in text)
            {
                if (IsUpper(c))
                {
                    if (previousCased)
                        return false;
                    previousCased = cased = true;
                }
                else if (IsLower(c))
                {
                    if (!previousCased)
                        return false;
                    previousCased = cased = true;
                }
                else
                    previousCased = false;
            }
            return cased;
        }

        private static byte[] Map(byte[] text, Func<byte, byte> map)
        {
            var result = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
                result[i] = map(text[i]);
            return result;
        }

        private static byte[] Title(byte[] text)
        {
            var result = new byte[text.Length];
            bool previousCased = false;
            for (int i = 0; i < text.Length; i++)
            {
                byte c = text[i];
                if (IsLower(c))
                {
                    result[i] = previousCased ? c : ToUpper(c);
                    previousCased = true;
                }
                else if (IsUpper(c))
                {
                    result[i] = previousCased ? ToLower(c) : c;
                    previousCased = true;
                }
                else
                {
                    result[i] = c;
                    previousCased = false;
                }
            }
            return result;
        }

        private static byte[] Capitalize(byte[] text)
        {
            var result = Map(text, ToLower);
            if (result.Length != 0)
                result[0] = ToUpper(text[0]);
            return result;
        }

        // _Py_strhex_impl
        // bytes.hex, bytearray.hex and memoryview.hex: one _Py_strhex_with_sep.
        internal static PyObject HexMethod(byte[] data, PyObject[] args, PyDict kwargs)
        {
            // The clinic's _PyArg_UnpackKeywords for hex(sep=<unrepresentable>, bytes_per_sep=1).
            var a = PyArgs.Unpack("hex", args, kwargs, new[] { "sep", "bytes_per_sep" }, 0, 0, 2);
            return Hex(data, a[0], a[1]);
        }

        // _Py_strhex_with_sep: bytes.hex, bytearray.hex, memoryview.hex and binascii.hexlify.
        internal static PyObject Hex(byte[] text, PyObject sep, PyObject bytesPerSep)
        {
            int group = bytesPerSep == null ? 1 : (int)Math.Clamp((long)IndexValue(bytesPerSep), int.MinValue, int.MaxValue);
            char separator = '\0';
            bool hasSeparator = sep != null;
            if (sep != null)
            {
                if (sep.Length() != 1)
                    throw PyValueError.Create("sep must be length 1.");
                if (sep is PyString s)
                    separator = s.Value[0];
                else if (sep is PyBytes b)
                    separator = (char)b.Value[0];
                else
                    throw PyTypeError.Create("sep must be str or bytes.");
                if (separator > 127)
                    throw PyValueError.Create("sep must be ASCII.");
            }
            var result = new StringBuilder(text.Length * 2);
            int abs = Math.Abs(group);
            bool grouping = hasSeparator && abs != 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (grouping && i != 0)
                {
                    // Counted from the right when positive, from the left when negative.
                    int distance = group > 0 ? text.Length - i : i;
                    if (distance % abs == 0)
                        result.Append(separator);
                }
                result.Append(text[i].ToString("x2"));
            }
            return PyString.From(result.ToString());
        }

        // _PyBytes_FromHex
        private static byte[] FromHex(PyObject argument)
        {
            if (argument is not PyString text)
                throw PyArgs.BadArgument("fromhex", "argument", "str", argument);
            var result = new List<byte>();
            string s = text.Value;
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c < 128 && IsSpace((byte)c))
                {
                    i++;
                    continue;
                }
                int top = PyBytes.HexDigitOf(c);
                if (top < 0)
                    throw PyValueError.Create($"non-hexadecimal number found in fromhex() arg at position {i}");
                int low = i + 1 < s.Length ? PyBytes.HexDigitOf(s[i + 1]) : -1;
                if (low < 0)
                    throw PyValueError.Create($"non-hexadecimal number found in fromhex() arg at position {i + 1}");
                result.Add((byte)(top << 4 | low));
                i += 2;
            }
            return result.ToArray();
        }

        // _Py_bytes_maketrans
        private static PyObject MakeTrans(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"maketrans expected 2 arguments, got {args.Length}");
            var from = RequireBuffer(args[0]);
            var to = RequireBuffer(args[1]);
            if (from.Length != to.Length)
                throw PyValueError.Create("maketrans arguments must have same length");
            var table = new byte[256];
            for (int i = 0; i < 256; i++)
                table[i] = (byte)i;
            for (int i = 0; i < from.Length; i++)
                table[from[i]] = to[i];
            return PyBytes.From(table);
        }

        private static PyObject Decode(PyObject self, PyObject[] args, PyDict kwargs)
        {
            PyObject encoding = args.Length > 0 ? args[0] : kwargs != null ? PyObject.KwValue(kwargs, "encoding") : null;
            PyObject errors = args.Length > 1 ? args[1] : kwargs != null ? PyObject.KwValue(kwargs, "errors") : null;
            if (encoding != null && encoding is not PyString)
                throw PyArgs.BadArgument("decode", "argument 'encoding'", "str", encoding);
            if (errors != null && errors is not PyString)
                throw PyArgs.BadArgument("decode", "argument 'errors'", "str", errors);
            return PyString.From(PyStringNewArguments.DecodeBytes(Self(self), (encoding as PyString)?.Value ?? "utf-8", (errors as PyString)?.Value ?? "strict"));
        }

        private static byte ByteValue(PyObject item, string message)
        {
            if (!HasIndex(item))
                throw PyTypeError.Create($"'{item.GetTypeName()}' object cannot be interpreted as an integer");
            var value = IndexValue(item);
            if (value < 0 || value > 255)
                throw PyValueError.Create(message);
            return (byte)value;
        }

        // PyBytes_FromObject and the slow path of bytearray___init___impl
        internal static byte[] FromObject(PyObject source, string kind, long lengthHint = 0, List<byte> growing = null, string refusal = null)
        {
            string range = kind == "bytes" ? "bytes must be in range(0, 256)" : "byte must be in range(0, 256)";
            var data = Buffer(source);
            if (data != null)
                return (byte[])data.Clone();
            var result = growing ?? new List<byte>();
            if (source is PyList list && ReferenceEquals(list.GetPyType(), PyType.ListType))
            {
                // The list may change while __index__ runs.
                for (int i = 0; i < list.Length(); i++)
                    result.Add(ByteValue(list.GetItem(PyInt.From(i)), range));
                return result.ToArray();
            }
            if (source is PyTuple tuple && ReferenceEquals(tuple.GetPyType(), PyType.TupleType))
            {
                foreach (var item in tuple.Items)
                    result.Add(ByteValue(item, range));
                return result.ToArray();
            }
            PyObject iterator;
            try
            {
                if (source is PyString && kind == "bytes")
                    throw PyTypeError.Create("");
                iterator = source.GetIterator();
            }
            catch (PythonException error) when (error.PyException is PyTypeError)
            {
                throw PyTypeError.Create(refusal ?? $"cannot convert '{source.GetTypeName()}' object to {kind}");
            }
            // _PyBytes_FromIterator, bytearray_extend. bytearray() itself does not ask.
            if (kind == "bytes" || lengthHint > 0)
                source.LengthHint(kind == "bytes" ? 64 : lengthHint);
            while (true)
            {
                PyObject item;
                try
                {
                    item = iterator.Next();
                }
                catch (PythonException error) when (error.PyException is PyStopIteration)
                {
                    return result.ToArray();
                }
                result.Add(ByteValue(item, range));
            }
        }

        private static byte[] Encoded(PyObject text, PyObject encoding, PyObject errors)
        {
            var callArgs = new List<PyObject> { encoding };
            if (errors != null)
                callArgs.Add(errors);
            var encoded = text.GetAttribute("encode").Call(callArgs.ToArray(), null);
            return encoded is PyBytes bytes ? bytes.Value
                : throw PyTypeError.Create($"encoder did not return a bytes object (type={encoded.GetTypeName()})");
        }

        private static void ConstructorArguments(PyObject[] args, PyDict kwargs, string kind, out PyObject source, out PyObject encoding, out PyObject errors)
        {
            // bytes_new and bytearray___init__'s clinic: (source=<unrepresentable>, encoding=NULL, errors=NULL).
            var a = PyArgs.Unpack(kind, args, kwargs, new[] { "source", "encoding", "errors" }, 0, 0, 3);
            source = a[0];
            encoding = a[1];
            errors = a[2];
            if (encoding != null && encoding is not PyString)
                throw PyArgs.BadArgument(kind, "argument 'encoding'", "str", encoding);
            if (errors != null && errors is not PyString)
                throw PyArgs.BadArgument(kind, "argument 'errors'", "str", errors);
        }

        // A size from __index__, or -1 when it is not an integer after all.
        private static long CountOf(PyObject source)
        {
            long count;
            try
            {
                count = PyArgs.AsSsize(source, PyArgs.SsizeError.Overflow);
            }
            catch (PythonException error) when (error.PyException is PyTypeError)
            {
                return -1;
            }
            if (count < 0)
                throw PyValueError.Create("negative count");
            return count;
        }

        // bytes_new_impl
        internal static PyObject BytesNew(PyObject[] args, PyDict kwargs)
        {
            ConstructorArguments(args, kwargs, "bytes", out var source, out var encoding, out var errors);
            if (source == null)
            {
                if (encoding != null || errors != null)
                    throw PyTypeError.Create(encoding != null ? "encoding without a string argument" : "errors without a string argument");
                return PyBytes.From(new byte[0]);
            }
            if (encoding != null)
            {
                if (source is not PyString)
                    throw PyTypeError.Create("encoding without a string argument");
                return PyBytes.From(Encoded(source, encoding, errors));
            }
            if (errors != null)
                throw PyTypeError.Create(source is PyString ? "string argument without an encoding" : "errors without a string argument");
            if (!ReferenceEquals(source.GetPyType(), PyType.BytesType) && BytesSlot(source) is { } converted)
                return converted;
            if (source is PyString)
                throw PyTypeError.Create("string argument without an encoding");
            if (HasIndex(source) && Buffer(source) == null)
            {
                long count = CountOf(source);
                if (count >= 0)
                    return PyBytes.From(new byte[PyAllocation.ArrayLength(count)]);
            }
            if (ReferenceEquals(source.GetPyType(), PyType.BytesType))
                return source;
            byte[] made = FromObject(source, "bytes");
            bool filled = Buffer(source) != null || source is PyTuple && ReferenceEquals(source.GetPyType(), PyType.TupleType);
            return filled ? PyBytes.Fresh(made) : PyBytes.From(made);
        }

        // bytearray___init___impl
        internal static byte[] ByteArrayInit(PyObject[] args, PyDict kwargs, List<byte> growing = null)
        {
            ConstructorArguments(args, kwargs, "bytearray", out var source, out var encoding, out var errors);
            if (source == null)
            {
                if (encoding != null || errors != null)
                    throw PyTypeError.Create(encoding != null ? "encoding without a string argument" : "errors without a string argument");
                return new byte[0];
            }
            if (source is PyString)
            {
                if (encoding == null)
                    throw PyTypeError.Create("string argument without an encoding");
                return Encoded(source, encoding, errors);
            }
            if (encoding != null || errors != null)
                throw PyTypeError.Create(encoding != null ? "encoding without a string argument" : "errors without a string argument");
            if (HasIndex(source) && Buffer(source) == null)
            {
                long count = CountOf(source);
                if (count >= 0)
                    return new byte[PyAllocation.ArrayLength(count)];
            }
            return FromObject(source, "bytearray", 0, growing);
        }

        private static void NoArguments(string owner, string name, PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"{owner}.{name}() takes no arguments ({args.Length} given)");
        }

        private static void Def(PyType type, string name, Func<PyObject, PyObject[], PyDict, PyObject> implementation, bool keywords = false)
        {
            string owner = type.Name;
            type.TypeDict[name] = new PyMethodDescriptor(name, type, (self, args, kwargs) =>
            {
                if (!keywords && kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"{owner}.{name}() takes no keyword arguments");
                return implementation(self, args, kwargs);
            }, acceptsKwargs: true);
        }

        private static void Zero(PyType type, string name, Func<PyObject, PyObject> implementation) =>
            Def(type, name, (self, args, kwargs) =>
            {
                NoArguments(type.Name, name, args);
                return implementation(self);
            });

        private static void One(PyType type, string name, Func<PyObject, PyObject, PyObject> implementation) =>
            Def(type, name, (self, args, kwargs) =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create($"{type.Name}.{name}() takes exactly one argument ({args.Length} given)");
                return implementation(self, args[0]);
            });

        private static void Predicate(PyType type, string name, Func<byte[], bool> test) =>
            Zero(type, name, self => BoolResult(test(Self(self))));

        private static void Transform(PyType type, string name, Func<byte[], byte[]> transform) =>
            Zero(type, name, self => NewFresh(self, transform(Self(self))));

        // PyNumber_AsSsize_t(index, PyExc_IndexError)
        internal static bool IsIndex(PyObject index) => index is not PySlice && HasIndex(index);

        internal static long AsIndex(PyObject index) => PyArgs.AsSsize(index, PyArgs.SsizeError.Index);

        // bytes_concat and bytearray_concat: any buffer on the right, the type of the left.
        internal static PyObject Concat(PyObject self, PyObject other)
        {
            var right = Buffer(other) ?? throw PyTypeError.Create($"can't concat {other.GetTypeName()} to {self.GetTypeName()}");
            var left = Self(self);
            var result = new byte[left.Length + right.Length];
            Array.Copy(left, result, left.Length);
            Array.Copy(right, 0, result, left.Length, right.Length);
            return New(self, result);
        }

        // _common_reduce (Objects/bytearrayobject.c): before protocol 3 there is no bytes, so
        // latin-1 text stands in; the state is _PyObject_GetState's.
        private static PyObject ReduceEx(PyObject self, long protocol)
        {
            var text = Self(self);
            PyObject state = PyType.GetState(self);
            PyObject[] arguments = text.Length == 0 ? Array.Empty<PyObject>() : protocol < 3
                ? new PyObject[] { PyString.From(Encoding.Latin1.GetString(text)), PyString.From("latin-1") }
                : new PyObject[] { PyBytes.From(text) };
            return PyTuple.From(new[] { self.GetPyType(), PyTuple.From(arguments), state });
        }

        internal static void InstallByteArray(PyType type)
        {
            type.TypeDict["__hash__"] = PyNone.Instance;
            Zero(type, "__alloc__", self => PyInt.From(((PyByteArray)self).Allocated));
            Zero(type, "__reduce__", self => ReduceEx(self, 2));
            Def(type, "__reduce_ex__", (self, args, kwargs) => ReduceEx(self, args.Length > 0 ? PyArgs.Int(args[0]) : 3));
        }

        internal static void InstallBytes(PyType type)
        {
            Zero(type, "__getnewargs__", self => PyTuple.From(new PyObject[] { PyBytes.From(Self(self)) }));
            Zero(type, "__bytes__", self => ReferenceEquals(self.GetPyType(), PyType.BytesType) ? self : PyBytes.From(Self(self)));
        }

        internal static void Install(PyType type)
        {
            Def(type, "find", (self, args, kwargs) => PyInt.From(Find(Self(self), "find", args, true)));
            Def(type, "rfind", (self, args, kwargs) => PyInt.From(Find(Self(self), "rfind", args, false)));
            Def(type, "index", (self, args, kwargs) => Index(Self(self), "index", args, true));
            Def(type, "rindex", (self, args, kwargs) => Index(Self(self), "rindex", args, false));
            Def(type, "count", (self, args, kwargs) => Count(Self(self), args));
            Def(type, "startswith", (self, args, kwargs) => StartsEnds(Self(self), "startswith", args, true));
            Def(type, "endswith", (self, args, kwargs) => StartsEnds(Self(self), "endswith", args, false));
            Def(type, "split", (self, args, kwargs) =>
            {
                var a = PyArgs.Unpack("split", args, kwargs, new[] { "sep", "maxsplit" }, 0, 0, 2);
                return Split(self, a[0], a[1]);
            }, keywords: true);
            Def(type, "rsplit", (self, args, kwargs) =>
            {
                var a = PyArgs.Unpack("rsplit", args, kwargs, new[] { "sep", "maxsplit" }, 0, 0, 2);
                return RSplit(self, a[0], a[1]);
            }, keywords: true);
            Def(type, "splitlines", (self, args, kwargs) =>
            {
                var keep = PyArgs.Unpack("splitlines", args, kwargs, new[] { "keepends" }, 0, 0, 1)[0];
                return SplitLines(self, keep != null && keep.IsTrue());
            }, keywords: true);
            One(type, "partition", (self, sep) => Partition(self, sep, true));
            One(type, "rpartition", (self, sep) => Partition(self, sep, false));
            Def(type, "strip", (self, args, kwargs) => Strip(self, args.Length > 0 ? args[0] : null, true, true));
            Def(type, "lstrip", (self, args, kwargs) => Strip(self, args.Length > 0 ? args[0] : null, true, false));
            Def(type, "rstrip", (self, args, kwargs) => Strip(self, args.Length > 0 ? args[0] : null, false, true));
            Def(type, "center", (self, args, kwargs) => Pad(self, "center", args, missing =>
            {
                long width = Self(self).Length + missing;
                long left = missing / 2 + (missing & width & 1);
                return (left, missing - left);
            }));
            Def(type, "ljust", (self, args, kwargs) => Pad(self, "ljust", args, missing => (0, missing)));
            Def(type, "rjust", (self, args, kwargs) => Pad(self, "rjust", args, missing => (missing, 0)));
            One(type, "zfill", ZFill);
            Def(type, "expandtabs", (self, args, kwargs) =>
            {
                var size = PyArgs.Unpack("expandtabs", args, kwargs, new[] { "tabsize" }, 0, 0, 1)[0];
                return ExpandTabs(self, size == null ? 8 : PyArgs.Int(size));
            }, keywords: true);
            Def(type, "replace", (self, args, kwargs) => Replace(self, args));
            Def(type, "translate", Translate, keywords: true);
            One(type, "join", Join);
            One(type, "removeprefix", (self, prefix) =>
            {
                var text = Self(self);
                var value = RequireBuffer(prefix);
                return value.Length != 0 && text.AsSpan().StartsWith(value) ? Piece(self, text, value.Length, text.Length) : ReturnSelf(self, text);
            });
            One(type, "removesuffix", (self, suffix) =>
            {
                var text = Self(self);
                var value = RequireBuffer(suffix);
                return value.Length != 0 && text.AsSpan().EndsWith(value) ? Piece(self, text, 0, text.Length - value.Length) : ReturnSelf(self, text);
            });
            Predicate(type, "isalnum", text => All(text, c => IsAlpha(c) || IsDigit(c)));
            Predicate(type, "isalpha", text => All(text, IsAlpha));
            Predicate(type, "isascii", text => Array.TrueForAll(text, c => c < 128));
            Predicate(type, "isdigit", text => All(text, IsDigit));
            Predicate(type, "isspace", text => All(text, IsSpace));
            Predicate(type, "islower", text => CaseTest(text, IsLower, IsUpper));
            Predicate(type, "isupper", text => CaseTest(text, IsUpper, IsLower));
            Predicate(type, "istitle", IsTitle);
            Transform(type, "lower", text => Map(text, ToLower));
            Transform(type, "upper", text => Map(text, ToUpper));
            Transform(type, "swapcase", text => Map(text, c => IsUpper(c) ? ToLower(c) : ToUpper(c)));
            Transform(type, "title", Title);
            Transform(type, "capitalize", Capitalize);
            Def(type, "decode", Decode, keywords: true);
            Def(type, "hex", (self, args, kwargs) => HexMethod(Self(self), args, kwargs), keywords: true);
            type.TypeDict["fromhex"] = new PyClassMethodDescriptor("fromhex", type, new PyBuiltinFunction("fromhex", (args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"{type.Name}.fromhex() takes no keyword arguments");
                var cls = args[0];
                var value = FromHex(args[1]);
                PyObject result = ReferenceEquals(type, PyType.BytesType) ? PyBytes.From(value) : new PyByteArray(value);
                return ReferenceEquals(cls, type) ? result : cls.Call(new[] { result }, null);
            }));
            type.TypeDict["maketrans"] = PyStaticmethod.ForBuiltin(type, "maketrans", (args, kwargs) =>
            {
                if (kwargs != null && kwargs.Length() != 0)
                    throw PyTypeError.Create($"{type.Name}.maketrans() takes no keyword arguments");
                return MakeTrans(args);
            });
        }
    }
}
