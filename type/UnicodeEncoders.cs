using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// The encoders of Objects/unicodeobject.c (CPython 3.12.15) that PyUnicode_AsEncodedString
    /// takes without the registry: unicode_encode_ucs1 (ascii, latin-1), unicode_encode_utf8 over
    /// stringlib's utf8_encoder, _PyUnicode_EncodeUTF16 and _PyUnicode_EncodeUTF32. Positions are
    /// code points (PyUnicode_READ); a lone surrogate is its own code point.
    /// </summary>
    internal static class UnicodeEncoders
    {
        // _Py_error_handler
        private enum Handler { Strict, SurrogateEscape, Replace, Ignore, BackslashReplace, SurrogatePass, XmlCharRefReplace, Other }

        // _Py_GetErrorHandler
        private static Handler GetErrorHandler(string errors) => errors switch
        {
            null or "strict" => Handler.Strict,
            "surrogateescape" => Handler.SurrogateEscape,
            "replace" => Handler.Replace,
            "ignore" => Handler.Ignore,
            "backslashreplace" => Handler.BackslashReplace,
            "surrogatepass" => Handler.SurrogatePass,
            "xmlcharrefreplace" => Handler.XmlCharRefReplace,
            _ => Handler.Other,
        };

        private static bool IsSurrogate(int ch) => ch >= 0xD800 && ch <= 0xDFFF;

        private static bool IsAscii(string text)
        {
            foreach (char c in text)
                if (c >= 0x80) return false;
            return true;
        }

        private const string Hexdigits = "0123456789abcdef";

        // backslashreplace: \xNN, \uNNNN or \UNNNNNNNN for each of data[collstart:collend].
        private static void BackslashReplace(List<byte> str, int[] data, int collstart, int collend)
        {
            for (int i = collstart; i < collend; ++i)
            {
                int ch = data[i];
                str.Add((byte)'\\');
                if (ch >= 0x00010000)
                {
                    str.Add((byte)'U');
                    for (int shift = 28; shift >= 8; shift -= 4)
                        str.Add((byte)Hexdigits[(ch >> shift) & 0xf]);
                }
                else if (ch >= 0x100)
                {
                    str.Add((byte)'u');
                    str.Add((byte)Hexdigits[(ch >> 12) & 0xf]);
                    str.Add((byte)Hexdigits[(ch >> 8) & 0xf]);
                }
                else
                    str.Add((byte)'x');
                str.Add((byte)Hexdigits[(ch >> 4) & 0xf]);
                str.Add((byte)Hexdigits[ch & 0xf]);
            }
        }

        // xmlcharrefreplace: &#N; for each of data[collstart:collend].
        private static void XmlCharRefReplace(List<byte> str, int[] data, int collstart, int collend)
        {
            for (int i = collstart; i < collend; ++i)
                foreach (char c in $"&#{data[i]};")
                    str.Add((byte)c);
        }

        private static PythonException EncodeException(string encoding, PyString unicode, int start, int end, string reason) =>
            PyUnicodeEncodeError.Failure(encoding, unicode, start, end, reason);

        /// <summary>unicode_encode_ucs1: limit is 128 (ascii) or 256 (latin-1).</summary>
        internal static byte[] EncodeUcs1(PyString unicode, string errors, int limit)
        {
            string encoding = limit == 256 ? "latin-1" : "ascii";
            string reason = limit == 256 ? "ordinal not in range(256)" : "ordinal not in range(128)";
            var data = unicode.ToCodePoints();
            int size = data.Length;
            var str = new List<byte>(size);
            Handler? errorHandler = null;
            int pos = 0;
            while (pos < size)
            {
                int ch = data[pos];
                // can we encode this?
                if (ch < limit)
                {
                    str.Add((byte)ch);
                    ++pos;
                    continue;
                }
                // startpos for collecting unencodable chars
                int collstart = pos;
                int collend = collstart + 1;
                // find all unecodable characters
                while (collend < size && data[collend] >= limit)
                    ++collend;

                errorHandler ??= GetErrorHandler(errors);
                switch (errorHandler)
                {
                    case Handler.Strict:
                        throw EncodeException(encoding, unicode, collstart, collend, reason);
                    case Handler.Replace:
                        for (int k = collstart; k < collend; k++)
                            str.Add((byte)'?');
                        pos = collend;
                        continue;
                    case Handler.Ignore:
                        pos = collend;
                        continue;
                    case Handler.BackslashReplace:
                        BackslashReplace(str, data, collstart, collend);
                        pos = collend;
                        continue;
                    case Handler.XmlCharRefReplace:
                        XmlCharRefReplace(str, data, collstart, collend);
                        pos = collend;
                        continue;
                    case Handler.SurrogateEscape:
                        {
                            int i;
                            for (i = collstart; i < collend; ++i)
                            {
                                ch = data[i];
                                if (ch < 0xdc80 || 0xdcff < ch)
                                    break;  // Not a UTF-8b surrogate
                                str.Add((byte)(ch - 0xdc00));
                                ++pos;
                            }
                            if (i >= collend)
                                continue;
                            collstart = pos;
                            goto default;
                        }
                    default:
                        {
                            var rep = PyCodecErrors.CallEncode(errors, encoding, reason, unicode, collstart, collend, size, out int newpos);
                            if (rep is PyBytes bytes)
                                // Directly copy bytes result to output.
                                str.AddRange(bytes.Value);
                            else
                            {
                                var text = ((PyString)rep).Value;
                                // Not all characters are smaller than limit
                                foreach (char c in text)
                                    if (c >= limit)
                                        throw EncodeException(encoding, unicode, collstart, collend, reason);
                                foreach (char c in text)
                                    str.Add((byte)c);
                            }
                            pos = newpos;
                            continue;
                        }
                }
            }
            return str.ToArray();
        }

        /// <summary>unicode_encode_utf8: stringlib's utf8_encoder; only surrogates fail.</summary>
        internal static byte[] EncodeUtf8(PyString unicode, string errors)
        {
            var data = unicode.ToCodePoints();
            int size = data.Length;
            var p = new List<byte>(data.Length * 2);
            Handler? errorHandler = null;
            for (int i = 0; i < size;)
            {
                int ch = data[i++];
                if (ch < 0x80)
                {
                    // Encode ASCII
                    p.Add((byte)ch);
                }
                else if (ch < 0x0800)
                {
                    // Encode Latin-1
                    p.Add((byte)(0xc0 | (ch >> 6)));
                    p.Add((byte)(0x80 | (ch & 0x3f)));
                }
                else if (IsSurrogate(ch))
                {
                    errorHandler ??= GetErrorHandler(errors);
                    int startpos = i - 1;
                    int endpos = startpos + 1;
                    while (endpos < size && IsSurrogate(data[endpos]))
                        endpos++;

                    switch (errorHandler)
                    {
                        case Handler.Replace:
                            for (int k = startpos; k < endpos; k++)
                                p.Add((byte)'?');
                            i += endpos - startpos - 1;
                            continue;
                        case Handler.Ignore:
                            i += endpos - startpos - 1;
                            continue;
                        case Handler.SurrogatePass:
                            for (int k = startpos; k < endpos; k++)
                            {
                                ch = data[k];
                                p.Add((byte)(0xe0 | (ch >> 12)));
                                p.Add((byte)(0x80 | ((ch >> 6) & 0x3f)));
                                p.Add((byte)(0x80 | (ch & 0x3f)));
                            }
                            i += endpos - startpos - 1;
                            continue;
                        case Handler.BackslashReplace:
                            BackslashReplace(p, data, startpos, endpos);
                            i += endpos - startpos - 1;
                            continue;
                        case Handler.XmlCharRefReplace:
                            XmlCharRefReplace(p, data, startpos, endpos);
                            i += endpos - startpos - 1;
                            continue;
                        case Handler.SurrogateEscape:
                            {
                                int k;
                                for (k = startpos; k < endpos; k++)
                                {
                                    ch = data[k];
                                    if (!(0xDC80 <= ch && ch <= 0xDCFF))
                                        break;
                                    p.Add((byte)(ch & 0xff));
                                }
                                if (k >= endpos)
                                {
                                    i += endpos - startpos - 1;
                                    continue;
                                }
                                startpos = k;
                                goto default;
                            }
                        default:
                            {
                                var rep = PyCodecErrors.CallEncode(errors, "utf-8", "surrogates not allowed", unicode, startpos, endpos, size, out int newpos);
                                if (rep is PyBytes bytes)
                                    p.AddRange(bytes.Value);
                                else
                                {
                                    var text = ((PyString)rep).Value;
                                    if (!IsAscii(text))
                                        throw EncodeException("utf-8", unicode, startpos, endpos, "surrogates not allowed");
                                    foreach (char c in text)
                                        p.Add((byte)c);
                                }
                                i = newpos;
                                continue;
                            }
                    }
                }
                else if (ch < 0x10000)
                {
                    p.Add((byte)(0xe0 | (ch >> 12)));
                    p.Add((byte)(0x80 | ((ch >> 6) & 0x3f)));
                    p.Add((byte)(0x80 | (ch & 0x3f)));
                }
                else
                {
                    // Encode UCS4 Unicode ordinals
                    p.Add((byte)(0xf0 | (ch >> 18)));
                    p.Add((byte)(0x80 | ((ch >> 12) & 0x3f)));
                    p.Add((byte)(0x80 | ((ch >> 6) & 0x3f)));
                    p.Add((byte)(0x80 | (ch & 0x3f)));
                }
            }
            return p.ToArray();
        }

        private static void Unit(List<byte> o, int unit, int width, bool bigEndian)
        {
            for (int k = 0; k < width; k++)
                o.Add((byte)(unit >> (bigEndian ? 8 * (width - 1 - k) : 8 * k)));
        }

        /// <summary>
        /// _PyUnicode_EncodeUTF16 (width 2) and _PyUnicode_EncodeUTF32 (width 4): byteorder 0 writes
        /// a BOM and native (little-endian) order, -1 little, 1 big. A surrogate goes to the error
        /// handler, always through unicode_encode_call_errorhandler; its bytes must fill whole
        /// units and its str must be ASCII.
        /// </summary>
        internal static byte[] EncodeUtf16Or32(PyString str, string errors, int byteorder, int width)
        {
            bool bigEndian = byteorder > 0;
            var data = str.ToCodePoints();
            int len = data.Length;
            var o = new List<byte>((len + 1) * width);
            if (byteorder == 0)
                Unit(o, 0xFEFF, width, bigEndian);
            if (len == 0)
                return o.ToArray();
            string bits = width == 2 ? "16" : "32";
            string encoding = byteorder < 0 ? $"utf-{bits}-le" : byteorder > 0 ? $"utf-{bits}-be" : $"utf-{bits}";

            int pos = 0;
            while (pos < len)
            {
                // ucs*lib_utf16_encode / utf32_encode: up to the first surrogate.
                while (pos < len && !IsSurrogate(data[pos]))
                {
                    int ch = data[pos++];
                    if (width == 2 && ch >= 0x10000)
                    {
                        Unit(o, 0xD800 - (0x10000 >> 10) + (ch >> 10), 2, bigEndian);
                        Unit(o, 0xDC00 + (ch & 0x3FF), 2, bigEndian);
                    }
                    else
                        Unit(o, ch, width, bigEndian);
                }
                if (pos == len)
                    break;

                var rep = PyCodecErrors.CallEncode(errors, encoding, "surrogates not allowed", str, pos, pos + 1, len, out int newpos);
                if (rep is PyBytes bytes)
                {
                    if (bytes.Value.Length % width != 0)
                        throw EncodeException(encoding, str, pos, pos + 1, "surrogates not allowed");
                    o.AddRange(bytes.Value);
                }
                else
                {
                    var text = ((PyString)rep).Value;
                    if (!IsAscii(text))
                        throw EncodeException(encoding, str, pos, pos + 1, "surrogates not allowed");
                    foreach (char c in text)
                        Unit(o, c, width, bigEndian);
                }
                pos = newpos;
            }
            return o.ToArray();
        }
    }
}
