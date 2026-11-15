using System;

namespace SharpPy
{
    /// <summary>
    /// Modules/binascii.c (CPython 3.12.15): the module state's Error (a ValueError) and
    /// Incomplete (an Exception), made by binascii_exec.
    /// </summary>
    internal static class PyBinasciiError
    {
        internal static readonly PyType ErrorType = PyExceptionSlots.NewException("binascii", "Error", PyType.ValueErrorType);
        internal static readonly PyType IncompleteType = PyExceptionSlots.NewException("binascii", "Incomplete", PyType.ExceptionType);
        internal static PythonException Create(string message) => PyExceptionSlots.Raise(ErrorType, message);
    }

    /// <summary>
    /// Modules/binascii.c: each binascii_*_impl in its order there, over the clinic's converters
    /// (clinic/binascii.c.h). The CRC-32 is internal_crc32, the build without USE_ZLIB_CRC32;
    /// zlib.crc32 is the same function (zlib's crc32_z).
    /// </summary>
    internal static class PyBinascii
    {
        private static readonly byte[] TableA2bBase64 = MakeA2bBase64();

        // table_a2b_base64: 0..63 for the alphabet, PAD ('=') is 0, everything else 0xff.
        private static byte[] MakeA2bBase64()
        {
            var table = new byte[256];
            Array.Fill(table, (byte)0xff);
            for (int i = 0; i < 64; i++)
                table[TableB2aBase64[i]] = (byte)i;
            table['='] = 0;
            return table;
        }

        private const byte BASE64_PAD = (byte)'=';

        // BASE64_MAXBIN: ((PY_SSIZE_T_MAX - 3) / 2)
        private const long BASE64_MAXBIN = (long.MaxValue - 3) / 2;

        private static ReadOnlySpan<byte> TableB2aBase64 => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"u8;

        private const int MAXLINESIZE = 76;

        // crctab_hqx: the CRC-CCITT (polynomial 0x1021) of each byte value.
        private static readonly ushort[] CrctabHqx = MakeCrcHqx();

        private static ushort[] MakeCrcHqx()
        {
            var table = new ushort[256];
            for (int n = 0; n < 256; n++)
            {
                int c = n << 8;
                for (int bit = 0; bit < 8; bit++)
                    c = (c & 0x8000) != 0 ? (c << 1) ^ 0x1021 : c << 1;
                table[n] = (ushort)c;
            }
            return table;
        }

        // crc_32_tab: the CRC-32 (polynomial 0xedb88320) of each byte value.
        private static readonly uint[] Crc32Tab = MakeCrc32();

        private static uint[] MakeCrc32()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int bit = 0; bit < 8; bit++)
                    c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : c >> 1;
                table[n] = c;
            }
            return table;
        }

        // Py_buffer
        private static byte[] Buffer(PyObject obj) => BytesMethods.RequireBuffer(obj);

        // ascii_buffer_converter: an ASCII str, or any buffer.
        private static byte[] AsciiBuffer(PyObject arg)
        {
            if (arg is PyString text)
            {
                var s = text.Value;
                var data = new byte[s.Length];
                for (int i = 0; i < s.Length; i++)
                {
                    if (s[i] > 0x7F)
                        throw PyValueError.Create("string argument should contain only ASCII characters");
                    data[i] = (byte)s[i];
                }
                return data;
            }
            // A refused PyObject_GetBuffer is this TypeError, whatever it raised.
            byte[] buffer;
            try { buffer = BytesMethods.SimpleBuffer(arg); }
            catch (PythonException e) when (e.PyException is PyBufferError) { buffer = null; }
            return buffer
                ?? throw PyTypeError.Create($"argument should be bytes, buffer or ASCII string, not '{arg.GetPyType().FullName}'");
        }

        // _PyLong_DigitValue: the value of a digit character in bases up to 36, 37 for the rest.
        private static int DigitValue(byte c) =>
            c >= '0' && c <= '9' ? c - '0'
            : c >= 'a' && c <= 'z' ? c - 'a' + 10
            : c >= 'A' && c <= 'Z' ? c - 'A' + 10
            : 37;

        private static PyObject Bytes(byte[] buf, int len)
        {
            if (len != buf.Length)
                Array.Resize(ref buf, len);
            return PyBytes.From(buf);
        }

        // binascii_a2b_uu_impl
        internal static PyObject A2bUu(byte[] asciiData)
        {
            int leftbits = 0;
            uint leftchar = 0;
            int pos = 0;
            long asciiLen = asciiData.Length;

            // First byte: binary data length (in bytes). An empty buffer's is its NUL terminator.
            int binLen = ((asciiLen > 0 ? asciiData[pos] : 0) - ' ') & 0x3f;
            pos++;
            asciiLen--;

            var binData = new byte[binLen];
            int o = 0;
            for (; binLen > 0; asciiLen--, pos++)
            {
                // XXX is it really best to add NULs if there's no more data
                int thisCh = asciiLen > 0 ? asciiData[pos] : 0;
                if (thisCh == '\n' || thisCh == '\r' || asciiLen <= 0)
                {
                    // Whitespace. Assume some spaces got eaten at end-of-line.
                    thisCh = 0;
                }
                else
                {
                    // The 64 in stead of the expected 63 is because there are a few uuencodes
                    // out there that use '`' as zero instead of space.
                    if (thisCh < ' ' || thisCh > ' ' + 64)
                        throw PyBinasciiError.Create("Illegal char");
                    thisCh = (thisCh - ' ') & 0x3f;
                }
                leftchar = (leftchar << 6) | (uint)thisCh;
                leftbits += 6;
                if (leftbits >= 8)
                {
                    leftbits -= 8;
                    binData[o++] = (byte)((leftchar >> leftbits) & 0xff);
                    leftchar &= (1u << leftbits) - 1;
                    binLen--;
                }
            }
            // Finally, check that if there's anything left on the line that it's whitespace only.
            while (asciiLen-- > 0)
            {
                int thisCh = asciiData[pos++];
                // Extra '`' may be written as padding in some cases
                if (thisCh != ' ' && thisCh != ' ' + 64 && thisCh != '\n' && thisCh != '\r')
                    throw PyBinasciiError.Create("Trailing garbage");
            }
            return PyBytes.From(binData);
        }

        // binascii_b2a_uu_impl
        internal static PyObject B2aUu(byte[] binData, bool backtick)
        {
            int leftbits = 0;
            uint leftchar = 0;
            int binLen = binData.Length;
            if (binLen > 45)
                // The 45 is a limit that appears in all uuencode's
                throw PyBinasciiError.Create("At most 45 bytes at once");

            // We're lazy and allocate to much (fixed up later)
            var asciiData = new byte[2 + (binLen + 2) / 3 * 4];
            int o = 0;
            // Store the length
            if (backtick && binLen == 0)
                asciiData[o++] = (byte)'`';
            else
                asciiData[o++] = (byte)(' ' + binLen);

            for (int i = 0; binLen > 0 || leftbits != 0; binLen--, i++)
            {
                // Shift the data (or padding) into our buffer
                if (binLen > 0)
                    leftchar = (leftchar << 8) | binData[i];
                else
                    leftchar <<= 8;
                leftbits += 8;

                // See if there are 6-bit groups ready
                while (leftbits >= 6)
                {
                    int thisCh = (int)((leftchar >> (leftbits - 6)) & 0x3f);
                    leftbits -= 6;
                    if (backtick && thisCh == 0)
                        asciiData[o++] = (byte)'`';
                    else
                        asciiData[o++] = (byte)(thisCh + ' ');
                }
            }
            asciiData[o++] = (byte)'\n';  // Append a courtesy newline
            return Bytes(asciiData, o);
        }

        // binascii_a2b_base64_impl
        internal static PyObject A2bBase64(byte[] asciiData, bool strictMode)
        {
            int asciiLen = asciiData.Length;
            bool paddingStarted = false;

            // Allocate the buffer
            var binData = new byte[(asciiLen + 3) / 4 * 3];  // Upper bound, corrected later
            int o = 0;

            if (strictMode && asciiLen > 0 && asciiData[0] == '=')
                throw PyBinasciiError.Create("Leading padding not allowed");

            int quadPos = 0;
            int leftchar = 0;
            int pads = 0;
            for (int i = 0; i < asciiLen; i++)
            {
                int thisCh = asciiData[i];

                // Check for pad sequences and ignore the invalid ones.
                if (thisCh == BASE64_PAD)
                {
                    paddingStarted = true;

                    if (strictMode && quadPos == 0)
                        throw PyBinasciiError.Create("Excess padding not allowed");
                    if (quadPos >= 2 && quadPos + ++pads >= 4)
                    {
                        // A pad sequence means we should not parse more input. In strict mode,
                        // an error should raise if there's excess data after the padding.
                        if (strictMode && i + 1 < asciiLen)
                            throw PyBinasciiError.Create("Excess data after padding");
                        return Bytes(binData, o);
                    }
                    continue;
                }

                thisCh = TableA2bBase64[thisCh];
                if (thisCh >= 64)
                {
                    if (strictMode)
                        throw PyBinasciiError.Create("Only base64 data is allowed");
                    continue;
                }

                // Characters that are not '=', in the middle of the padding, are not allowed
                if (strictMode && paddingStarted)
                    throw PyBinasciiError.Create("Discontinuous padding not allowed");
                pads = 0;

                switch (quadPos)
                {
                    case 0:
                        quadPos = 1;
                        leftchar = thisCh;
                        break;
                    case 1:
                        quadPos = 2;
                        binData[o++] = (byte)((leftchar << 2) | (thisCh >> 4));
                        leftchar = thisCh & 0x0f;
                        break;
                    case 2:
                        quadPos = 3;
                        binData[o++] = (byte)((leftchar << 4) | (thisCh >> 2));
                        leftchar = thisCh & 0x03;
                        break;
                    case 3:
                        quadPos = 0;
                        binData[o++] = (byte)((leftchar << 6) | thisCh);
                        leftchar = 0;
                        break;
                }
            }

            if (quadPos != 0)
            {
                if (quadPos == 1)
                    // There is exactly one extra valid, non-padding, base64 character. This is
                    // an invalid length, as no possible input could encode into such a string.
                    throw PyBinasciiError.Create("Invalid base64-encoded string: " +
                        $"number of data characters ({o / 3 * 4 + 1}) cannot be 1 more than a multiple of 4");
                throw PyBinasciiError.Create("Incorrect padding");
            }
            return Bytes(binData, o);
        }

        // binascii_b2a_base64_impl
        internal static PyObject B2aBase64(byte[] binData, bool newline)
        {
            int leftbits = 0;
            uint leftchar = 0;
            long binLen = binData.Length;
            if (binLen > BASE64_MAXBIN)
                throw PyBinasciiError.Create("Too much data for base64 line");

            // Exact here: 4 per 3, rounded up, and the newline.
            var asciiData = new byte[(binLen + 2) / 3 * 4 + (newline ? 1 : 0)];
            int o = 0;
            var table = TableB2aBase64;
            foreach (byte b in binData)
            {
                // Shift the data into our buffer
                leftchar = (leftchar << 8) | b;
                leftbits += 8;

                // See if there are 6-bit groups ready
                while (leftbits >= 6)
                {
                    int thisCh = (int)((leftchar >> (leftbits - 6)) & 0x3f);
                    leftbits -= 6;
                    asciiData[o++] = table[thisCh];
                }
            }
            if (leftbits == 2)
            {
                asciiData[o++] = table[(int)((leftchar & 3) << 4)];
                asciiData[o++] = BASE64_PAD;
                asciiData[o++] = BASE64_PAD;
            }
            else if (leftbits == 4)
            {
                asciiData[o++] = table[(int)((leftchar & 0xf) << 2)];
                asciiData[o++] = BASE64_PAD;
            }
            if (newline)
                asciiData[o++] = (byte)'\n';  // Append a courtesy newline
            return Bytes(asciiData, o);
        }

        // binascii_crc_hqx_impl
        internal static uint CrcHqx(ReadOnlySpan<byte> binData, uint crc)
        {
            crc &= 0xffff;
            foreach (byte b in binData)
                crc = ((crc << 8) & 0xff00) ^ CrctabHqx[(crc >> 8) ^ b];
            return crc;
        }

        /// <summary>internal_crc32: what binascii.crc32 and zlib.crc32 both compute (crc32_z).</summary>
        internal static uint Crc32(ReadOnlySpan<byte> binData, uint crc)
        {
            crc = ~crc;
            foreach (byte b in binData)
                crc = Crc32Tab[(crc ^ b) & 0xff] ^ (crc >> 8);
            return ~crc;
        }

        // binascii_b2a_hex_impl and binascii_hexlify_impl: _Py_strhex_bytes_with_sep.
        private static PyObject B2aHex(byte[] data, PyObject sep, PyObject bytesPerSep)
        {
            var text = (PyString)BytesMethods.Hex(data, sep, bytesPerSep);
            var s = text.Value;
            var result = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
                result[i] = (byte)s[i];
            return PyBytes.From(result);
        }

        // binascii_a2b_hex_impl
        internal static PyObject A2bHex(byte[] argbuf)
        {
            int arglen = argbuf.Length;
            // XXX What should we do about strings with an odd length? For now, raise.
            if (arglen % 2 != 0)
                throw PyBinasciiError.Create("Odd-length string");

            var retbuf = new byte[arglen / 2];
            for (int i = 0, j = 0; i < arglen; i += 2)
            {
                int top = DigitValue(argbuf[i]);
                int bot = DigitValue(argbuf[i + 1]);
                if (top >= 16 || bot >= 16)
                    throw PyBinasciiError.Create("Non-hexadecimal digit found");
                retbuf[j++] = (byte)((top << 4) + bot);
            }
            return PyBytes.From(retbuf);
        }

        private static bool IsHex(byte c) =>
            (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f') || (c >= '0' && c <= '9');

        // binascii_a2b_qp_impl
        internal static PyObject A2bQp(byte[] asciiData, bool header)
        {
            int datalen = asciiData.Length;
            // We allocate the output same size as input, this is overkill.
            var odata = new byte[datalen];
            int i = 0, o = 0;
            while (i < datalen)
            {
                if (asciiData[i] == '=')
                {
                    i++;
                    if (i >= datalen) break;
                    // Soft line breaks
                    if (asciiData[i] == '\n' || asciiData[i] == '\r')
                    {
                        if (asciiData[i] != '\n')
                            while (i < datalen && asciiData[i] != '\n') i++;
                        if (i < datalen) i++;
                    }
                    else if (asciiData[i] == '=')
                    {
                        // broken case from broken python qp
                        odata[o++] = (byte)'=';
                        i++;
                    }
                    else if (i + 1 < datalen && IsHex(asciiData[i]) && IsHex(asciiData[i + 1]))
                    {
                        // hexval
                        int ch = DigitValue(asciiData[i]) << 4;
                        i++;
                        ch |= DigitValue(asciiData[i]);
                        i++;
                        odata[o++] = (byte)ch;
                    }
                    else
                        odata[o++] = (byte)'=';
                }
                else if (header && asciiData[i] == '_')
                {
                    odata[o++] = (byte)' ';
                    i++;
                }
                else
                {
                    odata[o] = asciiData[i];
                    i++;
                    o++;
                }
            }
            return Bytes(odata, o);
        }

        // to_hex
        private static void ToHex(byte ch, byte[] s, int at)
        {
            const string digits = "0123456789ABCDEF";
            s[at + 1] = (byte)digits[ch % 16];
            s[at] = (byte)digits[ch / 16 % 16];
        }

        // The first test of binascii_b2a_qp_impl's two passes: whether databuf[in] is encoded.
        private static bool QpEncoded(byte[] databuf, int i, uint linelen, bool quotetabs, bool istext, bool header)
        {
            int datalen = databuf.Length;
            byte c = databuf[i];
            return c > 126 ||
                c == '=' ||
                (header && c == '_') ||
                (c == '.' && linelen == 0 &&
                 (i + 1 == datalen || databuf[i + 1] == '\n' || databuf[i + 1] == '\r' || databuf[i + 1] == 0)) ||
                (!istext && (c == '\r' || c == '\n')) ||
                ((c == '\t' || c == ' ') && i + 1 == datalen) ||
                (c < 33 && c != '\r' && c != '\n' && (quotetabs || (c != '\t' && c != ' ')));
        }

        private static bool QpLineEnd(byte[] databuf, int i, bool istext) =>
            istext && (databuf[i] == '\n' || (i + 1 < databuf.Length && databuf[i] == '\r' && databuf[i + 1] == '\n'));

        // binascii_b2a_qp_impl
        internal static PyObject B2aQp(byte[] databuf, bool quotetabs, bool istext, bool header)
        {
            int datalen = databuf.Length;
            uint linelen = 0;
            bool crlf = false;

            // See if this string is using CRLF line ends
            int p = Array.IndexOf(databuf, (byte)'\n');
            if (p > 0 && databuf[p - 1] == '\r')
                crlf = true;

            // First, scan to see how many characters need to be encoded
            long odatalen = 0;
            int i = 0;
            while (i < datalen)
            {
                long delta = 0;
                if (QpEncoded(databuf, i, linelen, quotetabs, istext, header))
                {
                    if (linelen + 3 >= MAXLINESIZE)
                    {
                        linelen = 0;
                        delta += crlf ? 3 : 2;
                    }
                    linelen += 3;
                    delta += 3;
                    i++;
                }
                else if (QpLineEnd(databuf, i, istext))
                {
                    linelen = 0;
                    // Protect against whitespace on end of line
                    if (i != 0 && (databuf[i - 1] == ' ' || databuf[i - 1] == '\t'))
                        delta += 2;
                    delta += crlf ? 2 : 1;
                    i += databuf[i] == '\r' ? 2 : 1;
                }
                else
                {
                    if (i + 1 != datalen && databuf[i + 1] != '\n' && linelen + 1 >= MAXLINESIZE)
                    {
                        linelen = 0;
                        delta += crlf ? 3 : 2;
                    }
                    linelen++;
                    delta++;
                    i++;
                }
                odatalen += delta;
            }
            if (odatalen > Array.MaxLength)
                throw PyMemoryError.Create();

            var odata = new byte[odatalen];
            int o = 0;
            i = 0;
            linelen = 0;
            while (i < datalen)
            {
                if (QpEncoded(databuf, i, linelen, quotetabs, istext, header))
                {
                    if (linelen + 3 >= MAXLINESIZE)
                    {
                        odata[o++] = (byte)'=';
                        if (crlf) odata[o++] = (byte)'\r';
                        odata[o++] = (byte)'\n';
                        linelen = 0;
                    }
                    odata[o++] = (byte)'=';
                    ToHex(databuf[i], odata, o);
                    o += 2;
                    i++;
                    linelen += 3;
                }
                else if (QpLineEnd(databuf, i, istext))
                {
                    linelen = 0;
                    // Protect against whitespace on end of line
                    if (o != 0 && (odata[o - 1] == ' ' || odata[o - 1] == '\t'))
                    {
                        byte ch = odata[o - 1];
                        odata[o - 1] = (byte)'=';
                        ToHex(ch, odata, o);
                        o += 2;
                    }
                    if (crlf) odata[o++] = (byte)'\r';
                    odata[o++] = (byte)'\n';
                    i += databuf[i] == '\r' ? 2 : 1;
                }
                else
                {
                    if (i + 1 != datalen && databuf[i + 1] != '\n' && linelen + 1 >= MAXLINESIZE)
                    {
                        odata[o++] = (byte)'=';
                        if (crlf) odata[o++] = (byte)'\r';
                        odata[o++] = (byte)'\n';
                        linelen = 0;
                    }
                    linelen++;
                    if (header && databuf[i] == ' ')
                    {
                        odata[o++] = (byte)'_';
                        i++;
                    }
                    else
                        odata[o++] = databuf[i++];
                }
            }
            return Bytes(odata, o);
        }

        private static bool Bool(PyObject value, bool fallback) => value == null ? fallback : value.IsTrue();

        // binascii_module_methods, then binascii_exec's Error and Incomplete.
        public static PyModule CreateBinasciiModule()
        {
            var module = new PyModule("binascii");
            var d = module.ModuleDict;
            d["a2b_uu"] = PyArgs.MethO("binascii", "a2b_uu", a => A2bUu(AsciiBuffer(a[0])));
            d["b2a_uu"] = new PyBuiltinFunction("b2a_uu", (a, kw) =>
            {
                var p = PyArgs.Unpack("b2a_uu", a, kw, new[] { "", "backtick" }, 1, 1, 1);
                var data = Buffer(p[0]);
                return B2aUu(data, Bool(p[1], false));
            });
            d["a2b_base64"] = new PyBuiltinFunction("a2b_base64", (a, kw) =>
            {
                var p = PyArgs.Unpack("a2b_base64", a, kw, new[] { "", "strict_mode" }, 1, 1, 1);
                var data = AsciiBuffer(p[0]);
                return A2bBase64(data, Bool(p[1], false));
            });
            d["b2a_base64"] = new PyBuiltinFunction("b2a_base64", (a, kw) =>
            {
                var p = PyArgs.Unpack("b2a_base64", a, kw, new[] { "", "newline" }, 1, 1, 1);
                var data = Buffer(p[0]);
                return B2aBase64(data, Bool(p[1], true));
            });
            d["a2b_hex"] = PyArgs.MethO("binascii", "a2b_hex", a => A2bHex(AsciiBuffer(a[0])));
            foreach (var name in new[] { "b2a_hex", "hexlify" })
                d[name] = new PyBuiltinFunction(name, (a, kw) =>
                {
                    var p = PyArgs.Unpack(name, a, kw, new[] { "data", "sep", "bytes_per_sep" }, 0, 1, 3);
                    var data = Buffer(p[0]);
                    if (p[2] != null) PyArgs.Int(p[2]);
                    return B2aHex(data, p[1], p[2]);
                });
            d["unhexlify"] = PyArgs.MethO("binascii", "unhexlify", a => A2bHex(AsciiBuffer(a[0])));
            d["crc_hqx"] = PyArgs.FastCall("binascii", "crc_hqx", 2, 2, a =>
            {
                var data = Buffer(a[0]);
                return PyInt.From((long)CrcHqx(data, PyArgs.UnsignedIntMask(a[1])));
            });
            d["crc32"] = PyArgs.FastCall("binascii", "crc32", 1, 2, a =>
            {
                var data = Buffer(a[0]);
                return PyInt.From((long)Crc32(data, a.Length > 1 ? PyArgs.UnsignedIntMask(a[1]) : 0));
            });
            d["a2b_qp"] = new PyBuiltinFunction("a2b_qp", (a, kw) =>
            {
                var p = PyArgs.Unpack("a2b_qp", a, kw, new[] { "data", "header" }, 0, 1, 2);
                var data = AsciiBuffer(p[0]);
                return A2bQp(data, Bool(p[1], false));
            });
            d["b2a_qp"] = new PyBuiltinFunction("b2a_qp", (a, kw) =>
            {
                var p = PyArgs.Unpack("b2a_qp", a, kw, new[] { "data", "quotetabs", "istext", "header" }, 0, 1, 4);
                var data = Buffer(p[0]);
                return B2aQp(data, Bool(p[1], false), Bool(p[2], true), Bool(p[3], false));
            });
            d["Error"] = PyBinasciiError.ErrorType;
            d["Incomplete"] = PyBinasciiError.IncompleteType;
            return module;
        }
    }
}
