// Modules/_json.c of CPython v3.12.15: the json speedups, scanner and encoder.
using System;

namespace SharpPy.Modules
{
    internal static class _JsonModule
    {
        // speedups_methods
        internal static readonly PyBuiltinFunction EncodeBasestringAscii =
            PyArgs.MethO("_json", "encode_basestring_ascii", args => PyEncodeBasestringAscii(args[0]));
        internal static readonly PyBuiltinFunction EncodeBasestring =
            PyArgs.MethO("_json", "encode_basestring", args => PyEncodeBasestring(args[0]));

        internal static PyModule Create()
        {
            var module = new PyModule("_json");
            module.ModuleDict["__doc__"] = PyString.From("json speedups\n");
            module.ModuleDict["encode_basestring_ascii"] = EncodeBasestringAscii;
            module.ModuleDict["encode_basestring"] = EncodeBasestring;
            module.ModuleDict["scanstring"] = new PyBuiltinFunction("scanstring", (args, kwargs) => PyScanstring(args, kwargs));
            // _json_exec
            module.ModuleDict["make_scanner"] = PyScanner.ScannerType;
            module.ModuleDict["make_encoder"] = PyEncoder.EncoderType;
            return module;
        }

        // S_CHAR and IS_WHITESPACE
        private static bool SChar(int c) => c >= ' ' && c <= '~' && c != '\\' && c != '"';
        private static bool IsWhitespace(int c) => c == ' ' || c == '\t' || c == '\n' || c == '\r';

        private const string HexDigits = "0123456789abcdef";

        /// <summary>PyUnicode_READ over one str: kind 1 and 2 as units, kind 4 as code points.</summary>
        private readonly struct Text
        {
            private readonly string _units;
            private readonly int[] _points;
            internal readonly int Length;

            internal Text(PyString str)
            {
                _points = str.Ucs4;
                _units = _points == null ? str.Ucs2 : null;
                Length = str.CodePointLength();
            }

            internal int this[int i] => _points != null ? _points[i] : _units[i];
        }

        private static Exception NotAString(PyObject pystr) =>
            PyTypeError.Create($"first argument must be a string, not {Truncate(pystr.GetTypeName(), 80)}");

        private static string Truncate(string text, int max) => text.Length > max ? text.Substring(0, max) : text;

        // ascii_escape_unichar
        private static void AsciiEscapeUnichar(int c, PyUnicodeWriter output)
        {
            switch (c)
            {
                case '\\': output.WriteUnits("\\\\"); return;
                case '"': output.WriteUnits("\\\""); return;
                case '\b': output.WriteUnits("\\b"); return;
                case '\f': output.WriteUnits("\\f"); return;
                case '\n': output.WriteUnits("\\n"); return;
                case '\r': output.WriteUnits("\\r"); return;
                case '\t': output.WriteUnits("\\t"); return;
            }
            if (c >= 0x10000)
            {
                // UTF-16 surrogate pair
                int v = 0xD800 - (0x10000 >> 10) + (c >> 10);
                WriteUEscape(output, v);
                c = 0xDC00 + (c & 0x3FF);
            }
            WriteUEscape(output, c);
        }

        private static void WriteUEscape(PyUnicodeWriter output, int c)
        {
            output.WriteUnits("\\u");
            output.WriteChar(HexDigits[(c >> 12) & 0xf]);
            output.WriteChar(HexDigits[(c >> 8) & 0xf]);
            output.WriteChar(HexDigits[(c >> 4) & 0xf]);
            output.WriteChar(HexDigits[c & 0xf]);
        }

        // ascii_escape_unicode
        private static PyString AsciiEscapeUnicode(PyString pystr)
        {
            var input = new Text(pystr);
            var output = new PyUnicodeWriter();
            output.WriteChar('"');
            for (int i = 0; i < input.Length; i++)
            {
                int c = input[i];
                if (SChar(c))
                    output.WriteChar(c);
                else
                    AsciiEscapeUnichar(c, output);
            }
            output.WriteChar('"');
            return output.Finish(fresh: true);
        }

        // escape_unicode
        private static PyString EscapeUnicode(PyString pystr)
        {
            var input = new Text(pystr);
            var output = new PyUnicodeWriter();
            output.WriteChar('"');
            for (int i = 0; i < input.Length; i++)
            {
                int c = input[i];
                switch (c)
                {
                    case '\\': output.WriteUnits("\\\\"); break;
                    case '"': output.WriteUnits("\\\""); break;
                    case '\b': output.WriteUnits("\\b"); break;
                    case '\f': output.WriteUnits("\\f"); break;
                    case '\n': output.WriteUnits("\\n"); break;
                    case '\r': output.WriteUnits("\\r"); break;
                    case '\t': output.WriteUnits("\\t"); break;
                    default:
                        if (c <= 0x1f)
                        {
                            output.WriteUnits("\\u00");
                            output.WriteChar(HexDigits[(c >> 4) & 0xf]);
                            output.WriteChar(HexDigits[c & 0xf]);
                        }
                        else
                            output.WriteChar(c);
                        break;
                }
            }
            output.WriteChar('"');
            return output.Finish(fresh: true);
        }

        // raise_errmsg: json.decoder.JSONDecodeError(msg, s, end)
        private static Exception RaiseErrmsg(string msg, PyString s, int end)
        {
            var jsonDecodeError = PyImportSystem.Import("json.decoder").GetAttribute("JSONDecodeError");
            var exc = jsonDecodeError.Call(new PyObject[] { PyString.From(msg), s, PyInt.From(end) }, null);
            return new PythonException((PyBaseException)exc);
        }

        // raise_stop_iteration
        private static Exception RaiseStopIteration(int idx) => PyStopIteration.Create(PyInt.From(idx));

        // scanstring_unicode: the string after the quote at end - 1; nextEnd is past the closing quote.
        private static PyString ScanstringUnicode(PyString pystr, long endArg, bool strict, out int nextEnd)
        {
            var buf = new Text(pystr);
            int len = buf.Length;
            if (endArg < 0 || len < endArg)
                throw PyValueError.Create("end is out of bounds");
            int end = (int)endArg;
            int begin = end - 1;
            PyUnicodeWriter writer = null;
            while (true)
            {
                // Find the end of the string or the next escape
                int c = 0;
                int next;
                for (next = end; next < len; next++)
                {
                    c = buf[next];
                    if (c == '"' || c == '\\')
                        break;
                    if (c <= 0x1f && strict)
                        throw RaiseErrmsg("Invalid control character at", pystr, next);
                }
                if (next == len)
                    c = 0;

                if (c == '"')
                {
                    // Fast path for simple case.
                    if (writer == null)
                    {
                        nextEnd = next + 1;
                        return pystr.Substring(end, next);
                    }
                }
                else if (c != '\\')
                    throw RaiseErrmsg("Unterminated string starting at", pystr, begin);

                writer ??= new PyUnicodeWriter();
                // Pick up this chunk if it's not zero length
                if (next != end)
                    writer.WriteSubstring(pystr, end, next);
                next++;
                if (c == '"')
                {
                    end = next;
                    break;
                }
                if (next == len)
                    throw RaiseErrmsg("Unterminated string starting at", pystr, begin);
                c = buf[next];
                if (c != 'u')
                {
                    // Non-unicode backslash escapes
                    end = next + 1;
                    switch (c)
                    {
                        case '"': break;
                        case '\\': break;
                        case '/': break;
                        case 'b': c = '\b'; break;
                        case 'f': c = '\f'; break;
                        case 'n': c = '\n'; break;
                        case 'r': c = '\r'; break;
                        case 't': c = '\t'; break;
                        default: c = 0; break;
                    }
                    if (c == 0)
                        throw RaiseErrmsg("Invalid \\escape", pystr, end - 2);
                }
                else
                {
                    next++;
                    end = next + 4;
                    if (end >= len)
                        throw RaiseErrmsg("Invalid \\uXXXX escape", pystr, next - 1);
                    c = HexQuad(buf, ref next, end, pystr);
                    // Surrogate pair
                    if (c >= 0xD800 && c <= 0xDBFF && end + 6 < len &&
                        buf[next++] == '\\' && buf[next++] == 'u')
                    {
                        end += 6;
                        int c2 = HexQuad(buf, ref next, end, pystr);
                        if (c2 >= 0xDC00 && c2 <= 0xDFFF)
                            c = 0x10000 + (((c & 0x3FF) << 10) | (c2 & 0x3FF));
                        else
                            end -= 6;
                    }
                }
                writer.WriteChar(c);
            }
            nextEnd = end;
            return writer.Finish();
        }

        // Decode 4 hex digits
        private static int HexQuad(Text buf, ref int next, int end, PyString pystr)
        {
            int c = 0;
            for (; next < end; next++)
            {
                int digit = buf[next];
                c <<= 4;
                if (digit >= '0' && digit <= '9')
                    c |= digit - '0';
                else if (digit >= 'a' && digit <= 'f')
                    c |= digit - 'a' + 10;
                else if (digit >= 'A' && digit <= 'F')
                    c |= digit - 'A' + 10;
                else
                    throw RaiseErrmsg("Invalid \\uXXXX escape", pystr, end - 5);
            }
            return c;
        }

        // py_scanstring: PyArg_ParseTuple(args, "On|p:scanstring")
        private static PyObject PyScanstring(PyObject[] args, PyDict kwargs)
        {
            PyArgs.NoKeywords("scanstring", kwargs);
            PyArgs.ParseTuple("scanstring", args, 2, 3);
            var pystr = args[0];
            long end = PyArgs.Ssize(args[1]);
            bool strict = args.Length < 3 || args[2].IsTrue();
            if (pystr is not PyString str)
                throw NotAString(pystr);
            var rval = ScanstringUnicode(str, end, strict, out int nextEnd);
            return PyTuple.From(new PyObject[] { rval, PyInt.From(nextEnd) });
        }

        // py_encode_basestring_ascii
        private static PyObject PyEncodeBasestringAscii(PyObject pystr) =>
            pystr is PyString str ? AsciiEscapeUnicode(str) : throw NotAString(pystr);

        // py_encode_basestring
        private static PyObject PyEncodeBasestring(PyObject pystr) =>
            pystr is PyString str ? EscapeUnicode(str) : throw NotAString(pystr);

        /// <summary>PyScannerObject: _json.Scanner, what json.scanner.c_make_scanner is.</summary>
        private sealed class PyScanner : PyObject
        {
            internal static readonly PyType ScannerType = MakeType();

            private bool _strict;
            private PyObject _objectHook, _objectPairsHook, _parseFloat, _parseInt, _parseConstant;
            private readonly PyDict _memo = new PyDict();

            public override PyType GetPyType() => _customType ?? ScannerType;
            public override bool IsCallable() => true;

            private static PyType MakeType()
            {
                var type = new PyType("Scanner", new[] { PyType.ObjectType }, "_json");
                type.TypeDict["__doc__"] = PyString.From("JSON scanner object");
                type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                    (cls, args, kwargs) => New((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
                type.TypeDict["__call__"] = new PyWrapperDescriptor("__call__", type, (self, args, kwargs) => self.Call(args, kwargs));
                // scanner_members: READONLY
                type.TypeDict["strict"] = new PyMemberDescriptor("strict", type, self => PyBool.FromBool(((PyScanner)self)._strict), readOnly: true);
                type.TypeDict["object_hook"] = new PyMemberDescriptor("object_hook", type, self => ((PyScanner)self)._objectHook ?? PyNone.Instance, readOnly: true);
                type.TypeDict["object_pairs_hook"] = new PyMemberDescriptor("object_pairs_hook", type, self => ((PyScanner)self)._objectPairsHook ?? PyNone.Instance, readOnly: true);
                type.TypeDict["parse_float"] = new PyMemberDescriptor("parse_float", type, self => ((PyScanner)self)._parseFloat ?? PyNone.Instance, readOnly: true);
                type.TypeDict["parse_int"] = new PyMemberDescriptor("parse_int", type, self => ((PyScanner)self)._parseInt ?? PyNone.Instance, readOnly: true);
                type.TypeDict["parse_constant"] = new PyMemberDescriptor("parse_constant", type, self => ((PyScanner)self)._parseConstant ?? PyNone.Instance, readOnly: true);
                return type;
            }

            // scanner_new: PyArg_ParseTupleAndKeywords(args, kwds, "O:make_scanner", {"context"})
            private static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
            {
                var ctx = PyArgs.Unpack("make_scanner", args, kwargs, new[] { "context" }, 0, 1, 1)[0];
                var s = new PyScanner();
                if (!ReferenceEquals(type, ScannerType))
                    s.SetCustomType(type);
                s._strict = ctx.GetAttribute("strict").IsTrue();
                s._objectHook = ctx.GetAttribute("object_hook");
                s._objectPairsHook = ctx.GetAttribute("object_pairs_hook");
                s._parseFloat = ctx.GetAttribute("parse_float");
                s._parseInt = ctx.GetAttribute("parse_int");
                s._parseConstant = ctx.GetAttribute("parse_constant");
                return s;
            }

            // scanner_call: PyArg_ParseTupleAndKeywords(args, kwds, "On:scan_once", {"string", "idx"})
            public override PyObject Call(PyObject[] args, PyDict kwargs)
            {
                var parsed = PyArgs.Unpack("scan_once", args, kwargs, new[] { "string", "idx" }, 0, 2, 2);
                var pystr = parsed[0];
                long idx = PyArgs.Ssize(parsed[1]);
                if (pystr is not PyString str)
                    throw NotAString(pystr);
                PyObject rval;
                int nextIdx;
                try
                {
                    rval = ScanOnce(str, new Text(str), idx, out nextIdx);
                }
                finally
                {
                    _memo.Clear();
                }
                return PyTuple.From(new PyObject[] { rval, PyInt.From(nextIdx) });
            }

            // _parse_object_unicode
            private PyObject ParseObject(PyString pystr, Text str, int idx, out int nextIdxPtr)
            {
                int endIdx = str.Length - 1;
                bool hasPairsHook = !ReferenceEquals(_objectPairsHook, PyNone.Instance);
                PyList pairs = hasPairsHook ? new PyList() : null;
                PyDict rval = hasPairsHook ? null : new PyDict();

                // skip whitespace after {
                while (idx <= endIdx && IsWhitespace(str[idx])) idx++;

                // only loop if the object is non-empty
                if (idx > endIdx || str[idx] != '}')
                {
                    while (true)
                    {
                        // read key
                        if (idx > endIdx || str[idx] != '"')
                            throw RaiseErrmsg("Expecting property name enclosed in double quotes", pystr, idx);
                        PyObject key = ScanstringUnicode(pystr, idx + 1, _strict, out int nextIdx);
                        key = _memo.SetDefault(key, key);
                        idx = nextIdx;

                        // skip whitespace between key and : delimiter, read :, skip whitespace
                        while (idx <= endIdx && IsWhitespace(str[idx])) idx++;
                        if (idx > endIdx || str[idx] != ':')
                            throw RaiseErrmsg("Expecting ':' delimiter", pystr, idx);
                        idx++;
                        while (idx <= endIdx && IsWhitespace(str[idx])) idx++;

                        // read any JSON term
                        var val = ScanOnce(pystr, str, idx, out nextIdx);
                        if (hasPairsHook)
                            pairs.Append(PyTuple.From(new[] { key, val }));
                        else
                            rval.SetItemStorage(key, val);
                        idx = nextIdx;

                        // skip whitespace before } or ,
                        while (idx <= endIdx && IsWhitespace(str[idx])) idx++;

                        // bail if the object is closed or we didn't get the , delimiter
                        if (idx <= endIdx && str[idx] == '}')
                            break;
                        if (idx > endIdx || str[idx] != ',')
                            throw RaiseErrmsg("Expecting ',' delimiter", pystr, idx);
                        idx++;

                        // skip whitespace after , delimiter
                        while (idx <= endIdx && IsWhitespace(str[idx])) idx++;
                    }
                }

                nextIdxPtr = idx + 1;

                if (hasPairsHook)
                    return _objectPairsHook.Call(new PyObject[] { pairs }, null);

                // if object_hook is not None: rval = object_hook(rval)
                if (!ReferenceEquals(_objectHook, PyNone.Instance))
                    return _objectHook.Call(new PyObject[] { rval }, null);
                return rval;
            }

            // _parse_array_unicode
            private PyObject ParseArray(PyString pystr, Text str, int idx, out int nextIdxPtr)
            {
                var rval = new PyList();
                int endIdx = str.Length - 1;

                // skip whitespace after [
                while (idx <= endIdx && IsWhitespace(str[idx])) idx++;

                // only loop if the array is non-empty
                if (idx > endIdx || str[idx] != ']')
                {
                    while (true)
                    {
                        // read any JSON term
                        var val = ScanOnce(pystr, str, idx, out int nextIdx);
                        rval.Append(val);
                        idx = nextIdx;

                        // skip whitespace between term and ,
                        while (idx <= endIdx && IsWhitespace(str[idx])) idx++;

                        // bail if the array is closed or we didn't get the , delimiter
                        if (idx <= endIdx && str[idx] == ']')
                            break;
                        if (idx > endIdx || str[idx] != ',')
                            throw RaiseErrmsg("Expecting ',' delimiter", pystr, idx);
                        idx++;

                        // skip whitespace after ,
                        while (idx <= endIdx && IsWhitespace(str[idx])) idx++;
                    }
                }

                // verify that idx < end_idx, str[idx] should be ']'
                if (idx > endIdx || str[idx] != ']')
                    throw RaiseErrmsg("Expecting value", pystr, endIdx);
                nextIdxPtr = idx + 1;
                return rval;
            }

            // _parse_constant: parse_constant("NaN" / "Infinity" / "-Infinity")
            private PyObject ParseConstant(string constant, int idx, out int nextIdxPtr)
            {
                var cstr = PyString.InternFrom(constant);
                var rval = _parseConstant.Call(new PyObject[] { cstr }, null);
                nextIdxPtr = idx + constant.Length;
                return rval;
            }

            private static bool IsDigit(int c) => c >= '0' && c <= '9';

            // _match_number_unicode
            private PyObject MatchNumber(PyString pystr, Text str, int start, out int nextIdxPtr)
            {
                int endIdx = str.Length - 1;
                int idx = start;
                bool isFloat = false;

                // read a sign if it's there, make sure it's not the end of the string
                if (str[idx] == '-')
                {
                    idx++;
                    if (idx > endIdx)
                        throw RaiseStopIteration(start);
                }

                // read as many integer digits as we find as long as it doesn't start with 0
                if (str[idx] >= '1' && str[idx] <= '9')
                {
                    idx++;
                    while (idx <= endIdx && IsDigit(str[idx])) idx++;
                }
                // if it starts with 0 we only expect one integer digit
                else if (str[idx] == '0')
                    idx++;
                // no integer digits, error
                else
                    throw RaiseStopIteration(start);

                // if the next char is '.' followed by a digit then read all float digits
                if (idx < endIdx && str[idx] == '.' && IsDigit(str[idx + 1]))
                {
                    isFloat = true;
                    idx += 2;
                    while (idx <= endIdx && IsDigit(str[idx])) idx++;
                }

                // if the next char is 'e' or 'E' then maybe read the exponent (or backtrack)
                if (idx < endIdx && (str[idx] == 'e' || str[idx] == 'E'))
                {
                    int eStart = idx;
                    idx++;

                    // read an exponent sign if present
                    if (idx < endIdx && (str[idx] == '-' || str[idx] == '+')) idx++;

                    // read all digits
                    while (idx <= endIdx && IsDigit(str[idx])) idx++;

                    // if we got a digit, then parse as float. if not, backtrack
                    if (IsDigit(str[idx - 1]))
                        isFloat = true;
                    else
                        idx = eStart;
                }

                PyObject customFunc;
                if (isFloat && !ReferenceEquals(_parseFloat, PyType.FloatType))
                    customFunc = _parseFloat;
                else if (!isFloat && !ReferenceEquals(_parseInt, PyType.IntType))
                    customFunc = _parseInt;
                else
                    customFunc = null;

                // the section we determined to be a number: ASCII, so its units are its characters
                PyObject rval;
                if (customFunc != null)
                    rval = customFunc.Call(new PyObject[] { pystr.Substring(start, idx) }, null);
                else
                {
                    var chars = new char[idx - start];
                    for (int i = 0; i < chars.Length; i++)
                        chars[i] = (char)str[i + start];
                    var numstr = new string(chars);
                    // PyFloat_FromString or PyLong_FromString(buf, NULL, 10)
                    rval = isFloat ? PyFloat.FromObject(PyString.From(numstr)) : PyInt.FromString(numstr, 10, unicode: false);
                }
                nextIdxPtr = idx;
                return rval;
            }

            // scan_once_unicode
            private PyObject ScanOnce(PyString pystr, Text str, long idxArg, out int nextIdxPtr)
            {
                int length = str.Length;
                if (idxArg < 0)
                    throw PyValueError.Create("idx cannot be negative");
                if (idxArg >= length)
                    throw RaiseStopIteration((int)Math.Min(idxArg, int.MaxValue));
                int idx = (int)idxArg;

                switch (str[idx])
                {
                    case '"':
                        // string
                        return ScanstringUnicode(pystr, idx + 1, _strict, out nextIdxPtr);
                    case '{':
                    {
                        // object
                        var recursion = PyCRecursion.EnterState(" while decoding a JSON object from a unicode string");
                        try
                        {
                            return ParseObject(pystr, str, idx + 1, out nextIdxPtr);
                        }
                        finally
                        {
                            recursion.Depth--;
                        }
                    }
                    case '[':
                    {
                        // array
                        var recursion = PyCRecursion.EnterState(" while decoding a JSON array from a unicode string");
                        try
                        {
                            return ParseArray(pystr, str, idx + 1, out nextIdxPtr);
                        }
                        finally
                        {
                            recursion.Depth--;
                        }
                    }
                    case 'n':
                        // null
                        if (idx + 3 < length && str[idx + 1] == 'u' && str[idx + 2] == 'l' && str[idx + 3] == 'l')
                        {
                            nextIdxPtr = idx + 4;
                            return PyNone.Instance;
                        }
                        break;
                    case 't':
                        // true
                        if (idx + 3 < length && str[idx + 1] == 'r' && str[idx + 2] == 'u' && str[idx + 3] == 'e')
                        {
                            nextIdxPtr = idx + 4;
                            return PyBool.True;
                        }
                        break;
                    case 'f':
                        // false
                        if (idx + 4 < length && str[idx + 1] == 'a' && str[idx + 2] == 'l' && str[idx + 3] == 's' && str[idx + 4] == 'e')
                        {
                            nextIdxPtr = idx + 5;
                            return PyBool.False;
                        }
                        break;
                    case 'N':
                        // NaN
                        if (idx + 2 < length && str[idx + 1] == 'a' && str[idx + 2] == 'N')
                            return ParseConstant("NaN", idx, out nextIdxPtr);
                        break;
                    case 'I':
                        // Infinity
                        if (idx + 7 < length && Matches(str, idx + 1, "nfinity"))
                            return ParseConstant("Infinity", idx, out nextIdxPtr);
                        break;
                    case '-':
                        // -Infinity
                        if (idx + 8 < length && Matches(str, idx + 1, "Infinity"))
                            return ParseConstant("-Infinity", idx, out nextIdxPtr);
                        break;
                }
                // Didn't find a string, object, array, or named constant. Look for a number.
                return MatchNumber(pystr, str, idx, out nextIdxPtr);
            }

            private static bool Matches(Text str, int at, string expected)
            {
                for (int i = 0; i < expected.Length; i++)
                    if (str[at + i] != expected[i])
                        return false;
                return true;
            }
        }

        /// <summary>PyEncoderObject: _json.Encoder, what json.encoder.c_make_encoder is.</summary>
        private sealed class PyEncoder : PyObject
        {
            internal static readonly PyType EncoderType = MakeType();

            private PyObject _markers, _defaultfn, _encoder, _indent;
            private PyString _keySeparator, _itemSeparator;
            private bool _sortKeys, _skipkeys, _allowNan;
            // fast_encode: encode_basestring_ascii or encode_basestring itself
            private Func<PyString, PyString> _fastEncode;

            public override PyType GetPyType() => _customType ?? EncoderType;
            public override bool IsCallable() => true;

            private static PyType MakeType()
            {
                var type = new PyType("Encoder", new[] { PyType.ObjectType }, "_json");
                type.TypeDict["__doc__"] = PyString.From("_iterencode(obj, _current_indent_level) -> iterable");
                type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                    (cls, args, kwargs) => New((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
                type.TypeDict["__call__"] = new PyWrapperDescriptor("__call__", type, (self, args, kwargs) => self.Call(args, kwargs));
                // encoder_members: READONLY
                type.TypeDict["markers"] = new PyMemberDescriptor("markers", type, self => ((PyEncoder)self)._markers ?? PyNone.Instance, readOnly: true);
                type.TypeDict["default"] = new PyMemberDescriptor("default", type, self => ((PyEncoder)self)._defaultfn ?? PyNone.Instance, readOnly: true);
                type.TypeDict["encoder"] = new PyMemberDescriptor("encoder", type, self => ((PyEncoder)self)._encoder ?? PyNone.Instance, readOnly: true);
                type.TypeDict["indent"] = new PyMemberDescriptor("indent", type, self => ((PyEncoder)self)._indent ?? PyNone.Instance, readOnly: true);
                type.TypeDict["key_separator"] = new PyMemberDescriptor("key_separator", type, self => (PyObject)((PyEncoder)self)._keySeparator ?? PyNone.Instance, readOnly: true);
                type.TypeDict["item_separator"] = new PyMemberDescriptor("item_separator", type, self => (PyObject)((PyEncoder)self)._itemSeparator ?? PyNone.Instance, readOnly: true);
                type.TypeDict["sort_keys"] = new PyMemberDescriptor("sort_keys", type, self => PyBool.FromBool(((PyEncoder)self)._sortKeys), readOnly: true);
                type.TypeDict["skipkeys"] = new PyMemberDescriptor("skipkeys", type, self => PyBool.FromBool(((PyEncoder)self)._skipkeys), readOnly: true);
                return type;
            }

            private static readonly string[] Kwlist =
                { "markers", "default", "encoder", "indent", "key_separator", "item_separator", "sort_keys", "skipkeys", "allow_nan" };

            // encoder_new: PyArg_ParseTupleAndKeywords(args, kwds, "OOOOUUppp:make_encoder", kwlist, ...)
            private static PyObject New(PyType type, PyObject[] args, PyDict kwargs)
            {
                var v = PyArgs.Unpack("make_encoder", args, kwargs, Kwlist, 0, 9, 9);
                var markers = v[0];
                for (int i = 4; i <= 5; i++)
                    if (v[i] is not PyString)
                        throw PyTypeError.Create($"make_encoder() argument {i + 1} must be str, not {v[i].GetTypeName()}");
                bool sortKeys = v[6].IsTrue(), skipkeys = v[7].IsTrue(), allowNan = v[8].IsTrue();
                if (!ReferenceEquals(markers, PyNone.Instance) && markers is not PyDict)
                    throw PyTypeError.Create($"make_encoder() argument 1 must be dict or None, not {Truncate(markers.GetTypeName(), 200)}");

                var s = new PyEncoder();
                if (!ReferenceEquals(type, EncoderType))
                    s.SetCustomType(type);
                s._markers = markers;
                s._defaultfn = v[1];
                s._encoder = v[2];
                s._indent = v[3];
                s._keySeparator = (PyString)v[4];
                s._itemSeparator = (PyString)v[5];
                s._sortKeys = sortKeys;
                s._skipkeys = skipkeys;
                s._allowNan = allowNan;
                if (ReferenceEquals(s._encoder, EncodeBasestringAscii))
                    s._fastEncode = AsciiEscapeUnicode;
                else if (ReferenceEquals(s._encoder, EncodeBasestring))
                    s._fastEncode = EscapeUnicode;
                return s;
            }

            // encoder_call: PyArg_ParseTupleAndKeywords(args, kwds, "On:_iterencode", {"obj", "_current_indent_level"})
            public override PyObject Call(PyObject[] args, PyDict kwargs)
            {
                var parsed = PyArgs.Unpack("_iterencode", args, kwargs, new[] { "obj", "_current_indent_level" }, 0, 2, 2);
                long indentLevel = PyArgs.Ssize(parsed[1]);
                var writer = new PyUnicodeWriter();
                ListencodeObj(writer, parsed[0], indentLevel);
                return PyTuple.From(new PyObject[] { writer.Finish() });
            }

            // _encoded_const
            private static PyString EncodedConst(PyObject obj)
            {
                if (ReferenceEquals(obj, PyNone.Instance))
                    return PyString.InternFrom("null");
                if (ReferenceEquals(obj, PyBool.True))
                    return PyString.InternFrom("true");
                if (ReferenceEquals(obj, PyBool.False))
                    return PyString.InternFrom("false");
                throw PyValueError.Create("not a const");
            }

            // encoder_encode_float: PyFloat_Type.tp_repr when it is finite
            private PyString EncodeFloat(PyFloat obj)
            {
                double i = obj.Value;
                if (!double.IsFinite(i))
                {
                    if (!_allowNan)
                        throw PyValueError.Create(PyErrFormat.FromFormat("Out of range float values are not JSON compliant: %R", obj).Value);
                    if (i > 0)
                        return PyString.From("Infinity");
                    if (i < 0)
                        return PyString.From("-Infinity");
                    return PyString.From("NaN");
                }
                return obj.Repr();
            }

            // encoder_encode_string
            private PyString EncodeString(PyString obj)
            {
                if (_fastEncode != null)
                    return _fastEncode(obj);
                var encoded = _encoder.Call(new PyObject[] { obj }, null);
                return encoded as PyString ??
                    throw PyTypeError.Create($"encoder() must return a string, not {Truncate(encoded.GetTypeName(), 80)}");
            }

            // The marker key: PyLong_FromVoidPtr(obj), the object's id.
            private static PyInt Ident(PyObject obj) => PyInt.From(PyObjectId.Of(obj));

            private PyInt EnterMarker(PyObject obj)
            {
                if (ReferenceEquals(_markers, PyNone.Instance))
                    return null;
                var markers = (PyDict)_markers;
                var ident = Ident(obj);
                if (markers.ContainsKey(ident))
                    throw PyValueError.Create("Circular reference detected");
                markers.SetItemStorage(ident, obj);
                return ident;
            }

            private void LeaveMarker(PyInt ident)
            {
                if (ident != null)
                    ((PyDict)_markers).DelItem(ident);
            }

            // encoder_listencode_obj
            private void ListencodeObj(PyUnicodeWriter writer, PyObject obj, long indentLevel)
            {
                if (ReferenceEquals(obj, PyNone.Instance))
                    writer.WriteUnits("null");
                else if (ReferenceEquals(obj, PyBool.True))
                    writer.WriteUnits("true");
                else if (ReferenceEquals(obj, PyBool.False))
                    writer.WriteUnits("false");
                else if (obj is PyString str)
                    writer.WriteStr(EncodeString(str));
                else if (obj is PyInt integer)
                    // PyLong_Type.tp_repr
                    writer.WriteStr(integer.Repr());
                else if (obj is PyFloat number)
                    writer.WriteStr(EncodeFloat(number));
                else if (obj is PyList || obj is PyTuple)
                {
                    var recursion = PyCRecursion.EnterState(" while encoding a JSON object");
                    try
                    {
                        ListencodeList(writer, obj, indentLevel);
                    }
                    finally
                    {
                        recursion.Depth--;
                    }
                }
                else if (obj is PyDict dict)
                {
                    var recursion = PyCRecursion.EnterState(" while encoding a JSON object");
                    try
                    {
                        ListencodeDict(writer, dict, indentLevel);
                    }
                    finally
                    {
                        recursion.Depth--;
                    }
                }
                else
                {
                    var ident = EnterMarker(obj);
                    var newobj = _defaultfn.Call(new[] { obj }, null);
                    var recursion = PyCRecursion.EnterState(" while encoding a JSON object");
                    try
                    {
                        ListencodeObj(writer, newobj, indentLevel);
                    }
                    finally
                    {
                        recursion.Depth--;
                    }
                    LeaveMarker(ident);
                }
            }

            // encoder_encode_key_value
            private void EncodeKeyValue(PyUnicodeWriter writer, ref bool first, PyObject key, PyObject value, long indentLevel)
            {
                PyString keystr;
                if (key is PyString text)
                    keystr = text;
                else if (key is PyFloat number)
                    keystr = EncodeFloat(number);
                else if (ReferenceEquals(key, PyBool.True) || ReferenceEquals(key, PyBool.False) || ReferenceEquals(key, PyNone.Instance))
                    // This must come before the PyLong_Check because True and False are also 1 and 0.
                    keystr = EncodedConst(key);
                else if (key is PyInt integer)
                    keystr = integer.Repr();
                else if (_skipkeys)
                    return;
                else
                    throw PyTypeError.Create($"keys must be str, int, float, bool or None, not {Truncate(key.GetTypeName(), 100)}");

                if (first)
                    first = false;
                else
                    writer.WriteStr(_itemSeparator);

                writer.WriteStr(EncodeString(keystr));
                writer.WriteStr(_keySeparator);
                ListencodeObj(writer, value, indentLevel);
            }

            // encoder_listencode_dict
            private void ListencodeDict(PyUnicodeWriter writer, PyDict dct, long indentLevel)
            {
                bool first = true;

                if (dct.Length() == 0) // Fast path
                {
                    writer.WriteUnits("{}");
                    return;
                }

                var ident = EnterMarker(dct);
                writer.WriteChar('{');

                // the indent of the C encoder is never used: json.encoder takes the Python one then
                if (!ReferenceEquals(_indent, PyNone.Instance))
                    indentLevel += 1;

                if (_sortKeys || !dct.IsExact)
                {
                    var items = PyDict.MappingItems(dct);
                    if (_sortKeys)
                        items.Sort();
                    for (int i = 0; i < items.RawCount(); i++)
                    {
                        var item = items.RawGet(i);
                        if (item is not PyTuple pair || pair.Length() != 2)
                            throw PyValueError.Create("items must return 2-tuples");
                        EncodeKeyValue(writer, ref first, pair.Items[0], pair.Items[1], indentLevel);
                    }
                }
                else
                {
                    // PyDict_Next: the table as it is, a change while it runs included
                    int pos = 0;
                    while (dct.Next(ref pos, out var key, out var value))
                        EncodeKeyValue(writer, ref first, key, value, indentLevel);
                }

                LeaveMarker(ident);
                writer.WriteChar('}');
            }

            // encoder_listencode_list
            private void ListencodeList(PyUnicodeWriter writer, PyObject seq, long indentLevel)
            {
                // PySequence_Fast: a list is itself, so PySequence_Fast_GET_SIZE follows a list
                // that changes while it is encoded; a tuple is itself; anything else a new list.
                var list = seq as PyList;
                var items = list == null ? PyList.SequenceFast(seq, "_iterencode_list needs a sequence") : null;
                int Size() => list != null ? list.RawCount() : items.Length;
                if (Size() == 0)
                {
                    writer.WriteUnits("[]");
                    return;
                }

                var ident = EnterMarker(seq);
                writer.WriteChar('[');
                if (!ReferenceEquals(_indent, PyNone.Instance))
                    indentLevel += 1;
                for (int i = 0; i < Size(); i++)
                {
                    if (i != 0)
                        writer.WriteStr(_itemSeparator);
                    ListencodeObj(writer, list != null ? list.RawGet(i) : items[i], indentLevel);
                }
                LeaveMarker(ident);
                writer.WriteChar(']');
            }
        }
    }
}
