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
        private PyObject CallDeclared(string dunder, PyObject[] args)
        {
            // LookupUserSpecial stops at the first non-PyClass MRO frame, so an exact tuple costs
            // one GetPyType plus one type test and never reaches the call below.
            var declared = LookupUserSpecial(dunder);
            return declared == null ? null : BindSpecialMethod(declared).Call(args, null);
        }

        private PyObject LookupDeclaredIterator()
        {
            // Lookup only: the dunder must be invoked by InvokeIteratorMethod, not before it.
            return LookupUserSpecial("__iter__");
        }


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
                && errors != "surrogateescape" && errors != "backslashreplace" && errors != "surrogatepass")
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

                // PyCodec_SurrogatePassErrors: the three bytes of a surrogate are that surrogate.
                if (errors == "surrogatepass" && b0 == 0xED && i + 2 < n && data[i + 1] >= 0xA0 && data[i + 1] <= 0xBF &&
                    (data[i + 2] & 0xC0) == 0x80)
                {
                    sb.Append((char)(0xD000 | (data[i + 1] & 0x3F) << 6 | (data[i + 2] & 0x3F)));
                    i += 3;
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
            if (errors == "strict" || errors == "surrogatepass")
            {
                throw PyUnicodeDecodeError.Failure("utf-8", data, start, range && end > start ? end + 1 : start + 1, reason);
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
                    throw PyUnicodeDecodeError.Failure("ascii", data, i, i + 1, "ordinal not in range(128)");
                }
                if (errors == "replace") sb.Append('\uFFFD');
                else if (errors == "surrogateescape") sb.Append((char)(0xDC00 + b));
                else if (errors == "backslashreplace") sb.Append($"\\x{b:x2}");
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
                    if (!(requested is PyType))
                        throw PyTypeError.Create(
                            $"bytes.__new__(X): X is not a type object ({requested.GetTypeName()})");
                    return PyBuiltinFunction.CallBytes(rest, kwargs);
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
                    return new PyBytes(decoded.ToArray());
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

                    return new PyBytes(table);
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
            var declared = LookupDeclaredIterator();
            if (declared != null)
                return InvokeIteratorMethod(declared);

            var items = new PyObject[Value.Length];
            for (int i = 0; i < Value.Length; i++)
            {
                items[i] = new PyInt(Value[i]);
            }
            return new PyListIterator(new PyList(items), "bytes_iterator", this);
        }
        
        public override PyString ToRepr()
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
            return new PyString(sb.Append(quote).ToString());
        }
        
        public override int Length()
        {
            var declaredLength = CallDeclared("__len__", System.Array.Empty<PyObject>());
            if (declaredLength != null)
                return ToLengthResult(declaredLength);
            return Value.Length;
        }
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
            var declaredAdd = CallDeclared("__add__", new[] { other });
            if (declaredAdd != null)
                return declaredAdd;

            return BytesMethods.Concat(this, other);
        }

        /// <summary>
        /// CPython 3.12: bytes % params - Objects/bytesobject.c bytes_format.
        /// Positional (tuple or single) and named (dict with bytes keys) forms.
        /// </summary>
        public override PyObject Modulo(PyObject other) => new PyBytes(Format(Value, other));

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
                if (other is not PyBytes and not PyByteArray && other.LookupSpecialMethodUncached("__getitem__") != null || other is PyDict)
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

                int StarValue()
                {
                    var given = NextArgument();
                    if (given is not (PyInt or PyBool or PyIntSubclass))
                        throw PyTypeError.Create("* wants int");
                    return given.ToInt();
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
                    var key = new PyBytes(keyBytes.ToArray());
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
                int width = 0;
                if (i < fmt.Length && fmt[i] == (byte)'*')
                {
                    i++;
                    width = StarValue();
                    if (width < 0)
                    {
                        leftAlign = true;
                        width = -width;
                    }
                }
                else
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
                    if (i < fmt.Length && fmt[i] == (byte)'*')
                    {
                        i++;
                        precision = Math.Max(0, StarValue());
                    }
                    else
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
                PyObject arg = NextArgument();

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
                        if (precision >= 0 && text.Length > precision)
                            text = text.Substring(0, precision);
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
        /// hex() is the one bytes method whose separator is parsed as "s#", which accepts str as well
        /// as a buffer: b"\x01\xab".hex(":") works while split(",") does not.
        /// </summary>
        internal static byte[] StringOrBufferOrNull(PyObject value)
        {
            if (value is PyString strValue)
                return System.Text.Encoding.Latin1.GetBytes(strValue.Value);
            return BufferOrNull(value);
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
            if (start <= end)
            {
                var range = Bytes(data).Slice(start, end - start);
                int found = backward ? LastIndexOfBytes(range, sub) : IndexOfBytes(range, sub);
                if (found >= 0)
                    return new PyInt(found + start);
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
            if (start <= end)
            {
                var range = Bytes(data).Slice(start, end - start);
                while (true)
                {
                    int found = IndexOfBytes(range, sub);
                    if (found < 0)
                        break;
                    count++;
                    range = range.Slice(found + sub.Length);
                }
            }
            return new PyInt(count);
        }

        private static ReadOnlySpan<byte> Bytes(System.Collections.Generic.IReadOnlyList<byte> data) => data switch
        {
            byte[] array => array,
            List<byte> list => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list),
            _ => new List<byte>(data).ToArray(),
        };

        // Linear in the worst case, as fastsearch's two-way is: a periodic needle must not cost n*m.
        // stringlib/transmogrify.h: replace, with an empty needle between the bytes
        internal static byte[] ReplaceBytes(byte[] data, byte[] old, byte[] replacement, int maxCount)
        {
            var result = new List<byte>(data.Length);
            int replaced = 0;
            if (old.Length == 0)
            {
                for (int i = 0; i <= data.Length; i++)
                {
                    if (maxCount < 0 || replaced < maxCount)
                    {
                        result.AddRange(replacement);
                        replaced++;
                    }
                    if (i < data.Length)
                        result.Add(data[i]);
                }
                return result.ToArray();
            }
            int position = 0;
            while (maxCount < 0 || replaced < maxCount)
            {
                int found = IndexOfBytes(data.AsSpan(position), old);
                if (found < 0)
                    break;
                result.AddRange(data.AsSpan(position, found).ToArray());
                result.AddRange(replacement);
                position += found + old.Length;
                replaced++;
            }
            result.AddRange(data.AsSpan(position).ToArray());
            return result.ToArray();
        }

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
                // bytes_repeat: one copy of an exact bytes is that bytes.
                if (count.Value == 1 && ReferenceEquals(GetPyType(), PyType.BytesType))
                    return this;
                if (count.Value * Value.Length > int.MaxValue)
                    throw PyMemoryError.Create();

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
        public override PyBool Contains(PyObject item) => PyBool.FromBool(BytesMethods.Contains(Value, item));

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
