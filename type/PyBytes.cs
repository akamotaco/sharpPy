using System;
using System.Collections.Generic;

namespace SharpPy
{
    public class PyBytes : PyObject
    {
        /// <summary>
        /// A subclass of tuple/bytes resolves the slot through its own type: a declared dunder wins
        /// and the inherited base slot only runs when the class declares none (measured: a tuple
        /// subclass with __len__/__iter__/__add__ answered 2, (1, 2) and (1, 2, 3) where CPython
        /// answered 99, ['t'] and 'user-tadd'). Every entry below is guarded by HasCustomType, so an
        /// exact tuple or bytes pays one field read; these are the hottest paths in the interpreter.
        /// </summary>



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
                /// <summary>
        /// unicode_decode_utf8 (Objects/unicodeobject.c), _PyUnicode_DecodeUTF8Stateful when not final:
        /// a lead byte, then each continuation byte checked against the range its position allows
        /// (E0 A0.., ED ..9F, F0 90.., F4 ..8F). A bad one is "invalid continuation byte" over the
        /// lead and the good ones before it; the end of the data inside a sequence is "unexpected
        /// end of data", or, when not final, where decoding stops.
        /// </summary>
        internal static string DecodeUtf8Python(byte[] data, string errors) => DecodeUtf8(data, errors, true, out _);

        internal static string DecodeUtf8(byte[] data, string errors, bool final, out int consumed)
        {
            var sb = new System.Text.StringBuilder(data.Length);
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

                // PyCodec_SurrogatePassErrors: the three bytes of a surrogate are that surrogate.
                if (errors == "surrogatepass" && b0 == 0xED && i + 2 < n && data[i + 1] >= 0xA0 && data[i + 1] <= 0xBF &&
                    (data[i + 2] & 0xC0) == 0x80)
                {
                    sb.Append((char)(0xD000 | (data[i + 1] & 0x3F) << 6 | (data[i + 2] & 0x3F)));
                    i += 3;
                    continue;
                }

                int extra;
                if (b0 >= 0xC2 && b0 <= 0xDF) extra = 1;
                else if (b0 >= 0xE0 && b0 <= 0xEF) extra = 2;
                else if (b0 >= 0xF0 && b0 <= 0xF4) extra = 3;
                else
                {
                    i = HandleDecodeError(ref data, i, i + 1, errors, sb, "invalid start byte");
                    n = data.Length;
                    continue;
                }

                int k = 1;
                for (; k <= extra && i + k < n; k++)
                {
                    byte bk = data[i + k];
                    byte lo = 0x80, hi = 0xBF;
                    if (k == 1)
                    {
                        if (b0 == 0xE0) lo = 0xA0;         // overlong
                        else if (b0 == 0xED) hi = 0x9F;    // surrogate
                        else if (b0 == 0xF0) lo = 0x90;    // overlong
                        else if (b0 == 0xF4) hi = 0x8F;    // past U+10FFFF
                    }
                    if (bk < lo || bk > hi) break;
                }
                if (k <= extra)
                {
                    if (i + k >= n)
                    {
                        // Every byte there is good, and the data ends inside the sequence.
                        if (!final) break;
                        i = HandleDecodeError(ref data, i, n, errors, sb, "unexpected end of data");
                    }
                    else
                    {
                        // A truncated surrogate waits for more when not final: surrogatepass may take it.
                        if (!final && b0 == 0xED && n - i == 2 && data[i + 1] >= 0xA0 && data[i + 1] <= 0xBF)
                            break;
                        i = HandleDecodeError(ref data, i, i + k, errors, sb, "invalid continuation byte");
                    }
                    n = data.Length;
                    continue;
                }

                int cp = b0 & (0x7F >> extra);
                for (int m = 1; m <= extra; m++) cp = (cp << 6) | (data[i + m] & 0x3F);
                if (cp <= 0xFFFF) sb.Append((char)cp);
                else sb.Append(char.ConvertFromUtf32(cp));
                i += extra + 1;
            }
            consumed = i;
            return sb.ToString();
        }

        /// <summary>
        /// _PyUnicode_DecodeUTF16Stateful (Objects/unicodeobject.c): byteorder -1 little, 1 big, 0
        /// read from a byte-order mark (consumed) and little-endian without one. Errors name
        /// utf-16-le or utf-16-be and count positions from the start of the data.
        /// </summary>
        internal static string DecodeUtf16Python(byte[] data, string errors, int byteorder) =>
            DecodeUtf16(data, errors, ref byteorder, true, out _);

        // byteorder comes back as the mark set it, 0 when there was none (native order decoded).
        internal static string DecodeUtf16(byte[] data, string errors, ref int byteorder, bool final, out int consumed)
        {
            int i = 0;
            if (byteorder == 0 && data.Length >= 2)
            {
                if (data[0] == 0xFF && data[1] == 0xFE) { byteorder = -1; i = 2; }
                else if (data[0] == 0xFE && data[1] == 0xFF) { byteorder = 1; i = 2; }
            }
            bool big = byteorder > 0;
            string encoding = big ? "utf-16-be" : "utf-16-le";
            int Unit(byte[] d, int at) => big ? d[at] << 8 | d[at + 1] : d[at + 1] << 8 | d[at];
            var sb = new System.Text.StringBuilder(data.Length / 2);
            while (i < data.Length)
            {
                int n = data.Length;
                if (n - i < 2)
                {
                    if (!final) break;
                    i = HandleDecodeError(ref data, i, n, errors, sb, "truncated data", encoding);
                    continue;
                }
                int unit = Unit(data, i);
                if (unit < 0xD800 || unit > 0xDFFF)
                {
                    sb.Append((char)unit);
                    i += 2;
                    continue;
                }
                string reason;
                int end;
                if (unit >= 0xDC00)
                {
                    reason = "illegal encoding";
                    end = i + 2;
                }
                else if (n - i < 4)
                {
                    if (!final) break;
                    reason = "unexpected end of data";
                    end = n;
                }
                else
                {
                    int low = Unit(data, i + 2);
                    if (low >= 0xDC00 && low <= 0xDFFF)
                    {
                        sb.Append((char)unit).Append((char)low);
                        i += 4;
                        continue;
                    }
                    reason = "illegal UTF-16 surrogate";
                    end = i + 2;
                }
                // PyCodec_SurrogatePassErrors: the surrogate code unit itself.
                if (errors == "surrogatepass")
                {
                    sb.Append((char)unit);
                    i += 2;
                    continue;
                }
                i = HandleDecodeError(ref data, i, end, errors, sb, reason, encoding);
            }
            consumed = i;
            return sb.ToString();
        }

        /// <summary>_PyUnicode_DecodeUTF32Stateful, with the byte orders of DecodeUtf16Python.</summary>
        internal static string DecodeUtf32Python(byte[] data, string errors, int byteorder) =>
            DecodeUtf32(data, errors, ref byteorder, true, out _);

        internal static string DecodeUtf32(byte[] data, string errors, ref int byteorder, bool final, out int consumed)
        {
            int i = 0;
            if (byteorder == 0 && data.Length >= 4)
            {
                if (data[0] == 0xFF && data[1] == 0xFE && data[2] == 0 && data[3] == 0) { byteorder = -1; i = 4; }
                else if (data[0] == 0 && data[1] == 0 && data[2] == 0xFE && data[3] == 0xFF) { byteorder = 1; i = 4; }
            }
            bool big = byteorder > 0;
            string encoding = big ? "utf-32-be" : "utf-32-le";
            var sb = new System.Text.StringBuilder(data.Length / 4);
            while (i < data.Length)
            {
                int n = data.Length;
                if (n - i < 4)
                {
                    if (!final) break;
                    i = HandleDecodeError(ref data, i, n, errors, sb, "truncated data", encoding);
                    continue;
                }
                long cp = big
                    ? (long)data[i] << 24 | (long)data[i + 1] << 16 | (long)data[i + 2] << 8 | data[i + 3]
                    : (long)data[i + 3] << 24 | (long)data[i + 2] << 16 | (long)data[i + 1] << 8 | data[i];
                if (cp > 0x10FFFF)
                {
                    i = HandleDecodeError(ref data, i, i + 4, errors, sb, "code point not in range(0x110000)", encoding);
                    continue;
                }
                if (cp >= 0xD800 && cp <= 0xDFFF)
                {
                    if (errors == "surrogatepass")
                    {
                        sb.Append((char)cp);
                        i += 4;
                        continue;
                    }
                    i = HandleDecodeError(ref data, i, i + 4, errors, sb, "code point in surrogate code point range(0xd800, 0xe000)", encoding);
                    continue;
                }
                if (cp > 0xFFFF) sb.Append(char.ConvertFromUtf32((int)cp));
                else sb.Append((char)cp);
                i += 4;
            }
            consumed = i;
            return sb.ToString();
        }

        // The decoder's fast path for the builtin handler names (unicode_decode_utf8 with
        // _Py_GetErrorHandler); any other name is the registry's. Answers where decoding resumes.
        private static int HandleDecodeError(ref byte[] data, int start, int end, string errors,
            System.Text.StringBuilder sb, string reason, string encoding = "utf-8")
        {
            switch (errors)
            {
                case "strict":
                case "surrogatepass":
                    throw PyUnicodeDecodeError.Failure(encoding, data, start, end, reason);
                case "replace":
                    sb.Append('\uFFFD');
                    return end;
                case "ignore":
                    return end;
                case "surrogateescape":
                {
                    // PyCodec_SurrogateEscapeErrors: up to four bytes of 128 or more each; none is
                    // the original error.
                    int consumed = 0;
                    while (consumed < 4 && start + consumed < end && data[start + consumed] >= 128)
                    {
                        sb.Append((char)(0xDC00 + data[start + consumed]));
                        consumed++;
                    }
                    if (consumed == 0)
                        throw PyUnicodeDecodeError.Failure(encoding, data, start, end, reason);
                    return start + consumed;
                }
                case "backslashreplace":
                    for (int k = start; k < end; k++) sb.Append($"\\x{data[k]:x2}");
                    return end;
                default:
                    return PyCodecErrors.CallDecode(errors, encoding, reason, ref data, start, end, sb);
            }
        }

        /// <summary>
        /// CPython ascii decoder: "'ascii' codec can't decode byte 0x80 in position 0:
        /// ordinal not in range(128)".
        /// </summary>
        internal static string DecodeAsciiPython(byte[] data, string errors)
        {
            var sb = new System.Text.StringBuilder();
            int i = 0;
            while (i < data.Length)
            {
                byte b = data[i];
                if (b < 0x80)
                {
                    sb.Append((char)b);
                    i++;
                    continue;
                }
                i = HandleDecodeError(ref data, i, i + 1, errors, sb, "ordinal not in range(128)", "ascii");
            }
            return sb.ToString();
        }

        private static void InitializeBytesDescriptors()
        {
            BytesMethods.Install(PyType.BytesType);
            BytesMethods.InstallBytes(PyType.BytesType);

            var bytesType = PyType.BytesType;

            // CPython publishes bytes' sequence slots as slot wrappers in type.__dict__
            // (Objects/slotdefs.c sq_concat / sq_repeat → bytes_concat / bytes_repeat). None were
            // registered here, so the reflected half was unreachable: 3 * B(b"a") for class B(bytes)
            // raised "unsupported operand type(s) for *" instead of repeating the value. Measured set
            // is __add__, __mul__, __rmul__ - CPython has no bytes.__radd__; sq_concat handles both
            // sides in C.
            // bytes.__new__ was not registered at all, so attribute lookup fell through to the generic
            // fallback and a user __new__ that forwards to bytes.__new__(cls, value) - a normal way to
            // write an immutable byte wrapper - died with "type.__new__() takes exactly 4 arguments".
            // CPython's bytes_new honours the requested type via bytes_subtype_new.
            bytesType.TypeDict["__new__"] = new PyStaticBuiltinMethod("__new__", owner: bytesType, method:
                (args, kwargs) => {
                    var requested = args.Length > 0 ? args[0] : (PyObject)bytesType;
                    var rest = args.Length > 1 ? args[1..] : System.Array.Empty<PyObject>();
                    if (requested is PyClass requestedClass && !ReferenceEquals(requestedClass, bytesType))
                        return PyBytesSubclass.BytesSubtypeNew(requestedClass, rest, kwargs);
                    // bytes_new for bytes itself: the argument checks and encodings CreateInstance holds.
                    return bytesType.CreateInstance(rest, kwargs);
                });

            bytesType.TypeDict["__add__"] = new PyWrapperDescriptor("__add__", bytesType,
                (self, args, kwargs) => {
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("wrapper __add__() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    return self.Add(args[0]);
                });
            bytesType.TypeDict["__mul__"] = new PyWrapperDescriptor("__mul__", bytesType,
                (self, args, kwargs) => {
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("wrapper __mul__() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    return self.Multiply(args[0]);
                });
            bytesType.TypeDict["__rmul__"] = new PyWrapperDescriptor("__rmul__", bytesType,
                (self, args, kwargs) => {
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("wrapper __rmul__() takes no keyword arguments");
                    if (args.Length != 1)
                        throw PyTypeError.Create($"expected 1 argument, got {args.Length}");
                    return self.Multiply(args[0]);
                });



            // CPython 3.12: Objects/bytesobject.c:2450-2500 - bytes_fromhex (classmethod)
            // bytes.fromhex(string) -> bytes
            var fromhexMethod = new PyMethodDescriptor(
                "fromhex", bytesType,
                (self, args, kwargs) => {
                    // Measured: the arity/keyword texts are qualified with the defining type, while the
                    // argument-type text is not. bytearray repeats the qualified form with its own name.
                    if (args.Length != 1)
                        throw PyTypeError.Create($"bytes.fromhex() takes exactly one argument ({args.Length} given)");
                    if (kwargs != null && kwargs.Length() != 0)
                        throw PyTypeError.Create("bytes.fromhex() takes no keyword arguments");

                    if (args[0] is not PyString hexStr)
                        throw PyTypeError.Create($"fromhex() argument must be str, not {args[0].GetTypeName()}");

                    // CPython 3.12 Objects/bytesobject.c bytes_fromhex scans the original string, skips
                    // ASCII whitespace, and reports the position in that original string. A dangling
                    // nibble reports the end of the string, so fromhex("abc") points at position 3.
                    string text = hexStr.Value;
                    var decoded = new System.Collections.Generic.List<byte>(text.Length / 2 + 1);
                    int high = -1;
                    for (int i = 0; i < text.Length; i++)
                    {
                        char c = text[i];
                        if (c == ' ' || c == '\t' || c == '\n' || c == '\v' || c == '\f' || c == '\r')
                            continue;
                        int digit = HexDigitOf(c);
                        if (digit < 0)
                            throw PyValueError.Create(
                                $"non-hexadecimal number found in fromhex() arg at position {i}");
                        if (high < 0)
                            high = digit;
                        else
                        {
                            decoded.Add((byte)((high << 4) | digit));
                            high = -1;
                        }
                    }
                    if (high >= 0)
                        throw PyValueError.Create(
                            $"non-hexadecimal number found in fromhex() arg at position {text.Length}");
                    return PyBytes.From(decoded.ToArray());
                },
                minArgs: 1, maxArgs: 1
            );

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



            

            
            


            
            
            




















            // bytes.removeprefix(prefix) - Remove prefix if present (Python 3.9+)
            
            // bytes.removesuffix(suffix) - Remove suffix if present (Python 3.9+)
            





            // CPython 3.12: Objects/bytesobject.c:2350-2380 (bytes_maketrans) - classmethod
            // bytes.maketrans(frm, to) -> bytes (translation table)
            var maketransMethod = new PyMethodDescriptor(
                "maketrans",
                bytesType,
                (self, args, kwargs) =>
                {
                    // Measured: bytes.maketrans reports its arity with its own wording, and the same
                    // text comes out of bytearray.maketrans, which delegates here.
                    if (args.Length != 2)
                        throw PyTypeError.Create($"maketrans expected 2 arguments, got {args.Length}");

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

                    return PyBytes.From(table);
                },
                // Validation happens in the body so the message above is the one reported.
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

                    return selfBytes.Iter();
                },
                minArgs: 0,
                maxArgs: 0
            );
        }

        public byte[] Value { get; }

        public PyBytes(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        /// <summary>
        /// The one way the interpreter makes an exact bytes, as _PyBytes_FromSize is CPython's: the
        /// empty bytes is always the one object. The public constructor stays for host code.
        /// </summary>
        public static PyBytes From(byte[] value) =>
            value == null || value.Length == 0 ? Empty : new PyBytes(value);

        public static readonly PyBytes Empty = new PyBytes(Array.Empty<byte>());

        public override string GetTypeName() => _customType?.Name ?? "bytes";
        public override PyType GetPyType() => _customType ?? PyType.BytesType;

        public override string ToString() => ToRepr().Value;

        /// <summary>
        /// CPython 3.12: Objects/bytesobject.c:2890-2920 - bytes iterator
        /// Return an iterator that yields PyInt for each byte (0-255)
        /// </summary>
        protected internal override PyObject Iter()
        {
            var items = new PyObject[Value.Length];
            for (int i = 0; i < Value.Length; i++)
            {
                items[i] = new PyInt(Value[i]);
            }
            return new PyListIterator(new PyList(items), "bytes_iterator", this);
        }
        
        // bytes_str and bytearray_str: this type's own repr function, not the type's slot.
        protected internal override PyString Str() => (PyString)Repr();

        protected internal override PyString Repr()
        {
            // PyBytes_Repr: single quotes, unless the text has them and no double quotes.
            bool hasSingle = false, hasDouble = false;
            foreach (byte b in Value)
            {
                hasSingle |= b == '\'';
                hasDouble |= b == '"';
            }
            char quote = hasSingle && !hasDouble ? '"' : '\'';
            var sb = new System.Text.StringBuilder("b").Append(quote);
            foreach (byte b in Value)
            {
                if (b == quote || b == '\\')
                    sb.Append('\\').Append((char)b);
                else if (b == '\n')
                    sb.Append("\\n");
                else if (b == '\r')
                    sb.Append("\\r");
                else if (b == '\t')
                    sb.Append("\\t");
                else if (b < 32 || b >= 127)
                    sb.Append($"\\x{b:x2}");
                else
                    sb.Append((char)b);
            }
            return PyString.From(sb.Append(quote).ToString());
        }
        
        protected internal override long Len() => Value.Length;
        protected internal override bool Bool() => Value.Length > 0;

        #region Buffer Protocol (PEP 688)

        // CPython 3.12: Objects/floatobject.c PyFloat_FromString accepts any buffer, so
        // float(b'1.5') is a text conversion, not a type error.

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
            return BytesMethods.Concat(this, other);
        }

        /// <summary>
        /// CPython 3.12: bytes % params - Objects/bytesobject.c bytes_format.
        /// Positional (tuple or single) and named (dict with bytes keys) forms.
        /// </summary>
        public override PyObject Modulo(PyObject other) => PyBytes.From(Format(Value, other));

        // _PyBytes_FormatEx
        internal static byte[] Format(byte[] format, PyObject other)
        {
            List<PyObject> seq = null;
            PyObject named = null;
            if (other is PyTuple tup)
            {
                seq = new List<PyObject>(tup.Items);
            }
            else
            {
                seq = new List<PyObject> { other };
                // _PyBytes_FormatEx: a mapping that is no tuple, bytes or bytearray serves %(key)s.
                if (other is not PyBytes and not PyByteArray && other.HasTypeSlot("__getitem__") || other is PyDict)
                    named = other;
            }

            var fmt = format;
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

                PyObject keyed = null;

                // getnextarg: the value of %(key), or the next of the arguments
                PyObject NextArgument()
                {
                    if (keyed != null)
                        return keyed;
                    if (seq == null || argIndex >= seq.Count)
                        throw PyTypeError.Create("not enough arguments for format string");
                    return seq[argIndex++];
                }

                // _PyBytes_FormatEx: a "*" takes the next argument, which PyLong_Check must pass.
                PyObject StarValue()
                {
                    var given = NextArgument();
                    if (!PyInt.LongCheck(given))
                        throw PyTypeError.Create("* wants int");
                    return given;
                }

                if (fmt[i] == (byte)'(')
                {
                    if (named == null)
                    {
                        throw PyTypeError.Create("format requires a mapping");
                    }
                    i++;
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
                    var key = PyBytes.From(keyBytes.ToArray());
                    i = close + 1;
                    keyed = named.GetItem(key);
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
                // _PyBytes_FormatEx: the width is a Py_ssize_t (a negative "*" one left-adjusts), the
                // precision a C int (a negative "*" one is 0); digits past those are "too big"; one
                // "h", "l" or "L" before the conversion is skipped.
                long widthValue = 0;
                if (i < fmt.Length && fmt[i] == (byte)'*')
                {
                    i++;
                    widthValue = PyArgs.Ssize(StarValue());
                    if (widthValue < 0)
                    {
                        leftAlign = true;
                        widthValue = widthValue == long.MinValue ? long.MaxValue : -widthValue;
                    }
                }
                else
                    while (i < fmt.Length && fmt[i] >= (byte)'0' && fmt[i] <= (byte)'9')
                    {
                        int d = fmt[i] - (byte)'0';
                        if (widthValue > (long.MaxValue - d) / 10)
                            throw PyValueError.Create("width too big");
                        widthValue = widthValue * 10 + d;
                        i++;
                    }
                // A result no CLR array can hold is the MemoryError of its allocation.
                if (widthValue > int.MaxValue)
                    throw PyMemoryError.Create();
                int width = (int)widthValue;
                int precision = -1;
                if (i < fmt.Length && fmt[i] == (byte)'.')
                {
                    i++;
                    precision = 0;
                    if (i < fmt.Length && fmt[i] == (byte)'*')
                    {
                        i++;
                        precision = Math.Max(0, PyArgs.Int(StarValue()));
                    }
                    else
                        while (i < fmt.Length && fmt[i] >= (byte)'0' && fmt[i] <= (byte)'9')
                        {
                            int d = fmt[i] - (byte)'0';
                            if (precision > (int.MaxValue - d) / 10)
                                throw PyValueError.Create("prec too big");
                            precision = precision * 10 + d;
                            i++;
                        }
                }
                if (i < fmt.Length && fmt[i] is (byte)'h' or (byte)'l' or (byte)'L')
                    i++;
                if (i >= fmt.Length)
                {
                    throw PyValueError.Create("incomplete format");
                }
                char conv = (char)fmt[i++];
                PyObject arg = NextArgument();

                string text;
                bool isNumeric = false;
                switch (conv)
                {
                    case 'a':
                    case 'r':
                        // _PyBytes_FormatEx: %r is %a here, the ASCII form of the object.
                        text = ((PyString)PyBuiltinsModule.Instance.GetAttribute("ascii").Call(new[] { arg }, null)).Value;
                        if (precision >= 0 && text.Length > precision)
                            text = text.Substring(0, precision);
                        break;
                    case '\0':
                        throw PyTypeError.Create($"{conv} formatting: a bytes-like object is required, not 'str'");
                    case 's':
                    case 'b':
                    {
                        // format_obj (Objects/bytesobject.c), %s being %b: bytes, a buffer, then
                        // __bytes__ from the type; the failure message always names %b.
                        var data = BytesMethods.Buffer(arg) ?? BytesMethods.BytesSlot(arg)?.Value
                            ?? throw PyTypeError.Create($"%b requires a bytes-like object, or an object that implements __bytes__, not '{arg.GetTypeName()}'");
                        text = System.Text.Encoding.Latin1.GetString(data);
                        if (precision >= 0 && text.Length > precision)
                            text = text.Substring(0, precision);
                        break;
                    }
                    case 'c':
                    {
                        int cv;
                        if (arg is PyBytes one && one.Value.Length == 1) cv = one.Value[0];
                        else if (arg is PyByteArray array && array.Length() == 1) cv = BytesMethods.Buffer(array)[0];
                        else
                        {
                            var ci = PyPercentFormat.Character(arg) ??
                                throw PyTypeError.Create("%c requires an integer in range(256) or a single byte");
                            if (ci < 0 || ci > 255)
                            {
                                throw PyOverflowError.Create("%c arg not in range(256)");
                            }
                            cv = (int)ci;
                        }
                        text = ((char)cv).ToString();
                        break;
                    }
                    case 'd':
                    case 'i':
                    case 'u':
                    {
                        // formatlong: "if (type == 'i') type = 'd';", which str % does not do.
                        var di = PyPercentFormat.Integer(arg, conv == 'i' ? 'd' : conv);
                        // _PyUnicode_FormatLong: precision is a minimum of digits.
                        text = System.Numerics.BigInteger.Abs(di).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if (precision > text.Length) text = text.PadLeft(precision, '0');
                        if (di < 0) text = "-" + text;
                        isNumeric = true;
                        break;
                    }
                    case 'o':
                    case 'x':
                    case 'X':
                    {
                        var bi = PyPercentFormat.Integer(arg, conv);
                        bool neg = bi < 0;
                        var digits = conv == 'o'
                            ? ToRadixString(System.Numerics.BigInteger.Abs(bi), 8)
                            : System.Numerics.BigInteger.Abs(bi).ToString("x").TrimStart('0').PadLeft(1, '0');
                        if (conv == 'X') digits = digits.ToUpperInvariant();
                        if (precision > digits.Length) digits = digits.PadLeft(precision, '0');
                        text = (neg ? "-" : "") + (alt ? (conv == 'o' ? "0o" : conv == 'x' ? "0x" : "0X") : "") + digits;
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
                        double dv;
                        try
                        {
                            dv = Modules.PyMathFunction.RealNumber(arg);
                        }
                        catch (PythonException)
                        {
                            // formatfloat puts this over any failure of PyFloat_AsDouble.
                            throw PyTypeError.Create($"float argument required, not {arg.GetTypeName()}");
                        }
                        // formatfloat: one writer with str % and format().
                        if (double.IsNaN(dv)) text = char.IsUpper(conv) ? "NAN" : "nan";
                        else if (double.IsInfinity(dv)) text = char.IsUpper(conv) ? "INF" : "inf";
                        else text = PyFloatFormatter.DoubleToString(Math.Abs(dv), conv, precision, alt);
                        if (!double.IsNaN(dv) && double.IsNegative(dv)) text = "-" + text;
                        isNumeric = true;
                        break;
                    }
                    default:
                        throw PyValueError.Create($"unsupported format character '{conv}' (0x{(int)conv:x2}) at index {i - 1}");
                }

                if (isNumeric && (signPlus || signSpace))
                {
                    if (!text.StartsWith("-", StringComparison.Ordinal))
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
                        // The zeros go between the prefix and the digits.
                        if (alt && text.Length > 1 && text[0] == '0' && text[1] is 'x' or 'X' or 'o')
                        {
                            sign += text.Substring(0, 2);
                            text = text.Substring(2);
                        }
                        text = sign + text.PadLeft(width - sign.Length, '0');
                    }
                    else text = text.PadLeft(width);
                }
                outBytes.AddRange(System.Text.Encoding.Latin1.GetBytes(text));
            }

            if (seq != null && argIndex < seq.Count && named == null)
            {
                throw PyTypeError.Create("not all arguments converted during bytes formatting");
            }
            return outBytes.ToArray();
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

        // CPython bytes find/count/index start/stop: accept int, bool, or __index__;
        // anything else -> TypeError. None is filtered by the caller (means default).
        // ---- p40/p41: CPython byte search/query family (shared by bytes+bytearray) ---
        // Objects/bytesobject.c owns find_impl / counting_impl / startswith_impl
        // and bytearray delegates to them, so the core lives on PyBytes.

        // find/count take int(0..255) or bytes-like; the prefix/suffix family takes
        // bytes-like only, and the two cases have different CPython messages.
        /// <summary>
        /// Buffer extraction for arguments CPython parses as a pure buffer: bytes, bytearray,
        /// memoryview. str is deliberately excluded - b"a,b".split(",") is refused with
        /// "a bytes-like object is required, not 'str'", measured. Returns null so the caller raises
        /// the message its own format string produces.
        /// </summary>
        internal static byte[] BufferOrNull(PyObject value)
        {
            switch (value)
            {
                case PyBytes bytesValue:
                    return bytesValue.Value;
                case PyByteArray byteArrayValue:
                    return byteArrayValue.ToByteArrayCopy();
                case PyMemoryView viewValue:
                    return viewValue.Data;
                default:
                    return null;
            }
        }

        /// <summary>
        /// CPython's Py_xdigit: the nibble value of a hex digit, or -1.
        /// </summary>
        internal static int HexDigitOf(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        // find/rfind and index/rindex share one scan.

        private static ReadOnlySpan<byte> Bytes(System.Collections.Generic.IReadOnlyList<byte> data) => data switch
        {
            byte[] array => array,
            List<byte> list => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list),
            _ => new List<byte>(data).ToArray(),
        };

        // Linear in the worst case, as fastsearch's two-way is: a periodic needle must not cost n*m.
        // stringlib/transmogrify.h: replace, with an empty needle between the bytes

        private static int[] FailureTable(ReadOnlySpan<byte> needle)
        {
            var table = new int[needle.Length];
            for (int i = 1, k = 0; i < needle.Length; i++)
            {
                while (k > 0 && needle[i] != needle[k])
                    k = table[k - 1];
                if (needle[i] == needle[k])
                    k++;
                table[i] = k;
            }
            return table;
        }

        internal static int IndexOfBytes(ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle)
        {
            if (needle.Length <= 8)
                return data.IndexOf(needle);
            var table = FailureTable(needle);
            for (int i = 0, k = 0; i < data.Length; i++)
            {
                while (k > 0 && data[i] != needle[k])
                    k = table[k - 1];
                if (data[i] == needle[k] && ++k == needle.Length)
                    return i - k + 1;
            }
            return -1;
        }

        internal static int LastIndexOfBytes(ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle)
        {
            if (needle.Length <= 8)
                return data.LastIndexOf(needle);
            var reversed = needle.ToArray();
            Array.Reverse(reversed);
            var table = FailureTable(reversed);
            for (int i = data.Length - 1, k = 0; i >= 0; i--)
            {
                while (k > 0 && data[i] != reversed[k])
                    k = table[k - 1];
                if (data[i] == reversed[k] && ++k == reversed.Length)
                    return i;
            }
            return -1;
        }

        // CPython startswith_impl: bytes-like or a tuple of bytes-like.

        // removeprefix/removesuffix: METH_O, bytes-like only, returns a copy.

        /// <summary>
        /// bytes * int repetition
        /// </summary>
        public override PyObject Multiply(PyObject other)
        {
            // CPython 3.12: Objects/bytesobject.c:1230-1250 - bytes_repeat
            if (other is PyInt count)
            {
                long times = PyAllocation.IndexSized(count.Value);
                if (times < 0)
                    return PyBytes.From(new byte[0]);
                // bytes_repeat: one copy of an exact bytes is that bytes.
                if (times == 1 && ReferenceEquals(GetPyType(), PyType.BytesType))
                    return this;
                // bytes_repeat: "repeated bytes are too long" past Py_ssize_t, then _PyBytes_FromSize.
                int total = PyAllocation.ArrayLength(PyAllocation.Product(Value.Length, times, "repeated bytes are too long"));
                int countInt = total == 0 ? 0 : (int)times;
                var result = new byte[total];
                for (int i = 0; i < countInt; i++)
                {
                    Array.Copy(Value, 0, result, i * Value.Length, Value.Length);
                }
                return PyBytes.From(result);
            }
            
            throw PyTypeError.Create($"can't multiply sequence by non-int of type '{other.GetTypeName()}'");
        }

        /// <summary>
        /// bytes equality comparison (used by equals operation)
        /// </summary>
        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
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

        // _Py_HashBytes of the same bytes as a one-byte str: hash('spam') == hash(b'spam').
        // bytes_hash
        protected internal override long Hash() => PyHash.Bytes(Value);

        public override int ToHash() => System.Text.Encoding.Latin1.GetString(Value).GetHashCode();

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
        protected internal override PyObject MpSubscript(PyObject index)
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
                    return PyBytes.From(result);
                }
                else
                {
                    int count = PySlice.GetLength(start, stop, step);
                    var result = new byte[count];
                    for (int n = 0, position = start; n < count; n++, position = unchecked(position + step))
                        result[n] = Value[position];
                    return PyBytes.From(result);
                }
            }
            
            if (TryGetIndex(index) is { } byteIndex)
                return GetItem(byteIndex);
            throw PyTypeError.Create($"byte indices must be integers or slices, not {index.GetTypeName()}");
        }

        /// <summary>
        /// bytes containment check
        /// </summary>
        protected internal override PyBool ContainsItem(PyObject item) => PyBool.FromBool(BytesMethods.Contains(Value, item));

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
