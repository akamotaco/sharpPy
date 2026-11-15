using System.Text;

namespace SharpPy
{
    /// <summary>
    /// The decoders of Objects/unicodeobject.c (CPython 3.12.15) that PyUnicode_Decode takes without
    /// the registry: unicode_decode_utf8 over stringlib's utf8_decode, PyUnicode_DecodeUTF16Stateful
    /// over utf16_decode, PyUnicode_DecodeUTF32Stateful, PyUnicode_DecodeASCII and
    /// PyUnicode_DecodeLatin1. consumed is null for a final decode; a byteorder of 0 reads a BOM.
    /// </summary>
    internal static class UnicodeDecoders
    {
        // _Py_error_handler, as the decoders' fast paths know them.
        private enum Handler { Strict, SurrogateEscape, Replace, Ignore, Other }

        // _Py_GetErrorHandler
        private static Handler GetErrorHandler(string errors) => errors switch
        {
            null or "strict" => Handler.Strict,
            "surrogateescape" => Handler.SurrogateEscape,
            "replace" => Handler.Replace,
            "ignore" => Handler.Ignore,
            _ => Handler.Other,
        };

        private static bool IsContinuationByte(int ch) => ch >= 0x80 && ch < 0xC0;

        /// <summary>
        /// stringlib utf8_decode (ucs4lib): decodes from s[q] while it can, then answers 0 at the end
        /// or in an incomplete sequence, 1 for an invalid start byte, 2, 3 or 4 for an invalid
        /// continuation byte at offset 1, 2 or 3.
        /// </summary>
        private static int Utf8Decode(byte[] s, ref int q, int end, PyUnicodeWriter p)
        {
            while (q < end)
            {
                int ch = s[q];
                if (ch < 0x80)
                {
                    q++;
                    p.WriteChar(ch);
                    continue;
                }
                if (ch < 0xE0)
                {
                    // \xC2\x80-\xDF\xBF -- 0080-07FF
                    if (ch < 0xC2)
                        // invalid sequence: \x80-\xBF continuation byte, \xC0-\xC1 fake 0000-007F
                        return 1;
                    if (end - q < 2)
                        // unexpected end of data: the caller will decide whether it's an error
                        break;
                    int ch2 = s[q + 1];
                    if (!IsContinuationByte(ch2))
                        return 2;
                    ch = (ch << 6) + ch2 - ((0xC0 << 6) + 0x80);
                    q += 2;
                    p.WriteChar(ch);
                    continue;
                }
                if (ch < 0xF0)
                {
                    // \xE0\xA0\x80-\xEF\xBF\xBF -- 0800-FFFF
                    if (end - q < 3)
                    {
                        if (end - q < 2)
                            break;
                        int c2 = s[q + 1];
                        if (!IsContinuationByte(c2) || (c2 < 0xA0 ? ch == 0xE0 : ch == 0xED))
                            return 2;
                        break;
                    }
                    int ch2 = s[q + 1], ch3 = s[q + 2];
                    if (!IsContinuationByte(ch2))
                        return 2;
                    if (ch == 0xE0)
                    {
                        if (ch2 < 0xA0)
                            // invalid sequence \xE0\x80\x80-\xE0\x9F\xBF -- fake 0000-0800
                            return 2;
                    }
                    else if (ch == 0xED && ch2 >= 0xA0)
                        // surrogates in range D800-DFFF are not valid UTF-8
                        return 2;
                    if (!IsContinuationByte(ch3))
                        return 3;
                    ch = (ch << 12) + (ch2 << 6) + ch3 - ((0xE0 << 12) + (0x80 << 6) + 0x80);
                    q += 3;
                    p.WriteChar(ch);
                    continue;
                }
                if (ch < 0xF5)
                {
                    // \xF0\x90\x80\x80-\xF4\x8F\xBF\xBF -- 10000-10FFFF
                    if (end - q < 4)
                    {
                        if (end - q < 2)
                            break;
                        int c2 = s[q + 1];
                        if (!IsContinuationByte(c2) || (c2 < 0x90 ? ch == 0xF0 : ch == 0xF4))
                            return 2;
                        if (end - q < 3)
                            break;
                        if (!IsContinuationByte(s[q + 2]))
                            return 3;
                        break;
                    }
                    int ch2 = s[q + 1], ch3 = s[q + 2], ch4 = s[q + 3];
                    if (!IsContinuationByte(ch2))
                        return 2;
                    if (ch == 0xF0)
                    {
                        if (ch2 < 0x90)
                            // invalid sequence \xF0\x80\x80\x80-\xF0\x8F\xBF\xBF -- fake 0000-FFFF
                            return 2;
                    }
                    else if (ch == 0xF4 && ch2 >= 0x90)
                        // invalid sequence \xF4\x90\x80\x80- -- 110000- overflow
                        return 2;
                    if (!IsContinuationByte(ch3))
                        return 3;
                    if (!IsContinuationByte(ch4))
                        return 4;
                    ch = (ch << 18) + (ch2 << 12) + (ch3 << 6) + ch4 - ((0xF0 << 18) + (0x80 << 12) + (0x80 << 6) + 0x80);
                    q += 4;
                    p.WriteChar(ch);
                    continue;
                }
                return 1;
            }
            return 0;
        }

        /// <summary>unicode_decode_utf8 (PyUnicode_DecodeUTF8Stateful)</summary>
        internal static PyString DecodeUtf8(byte[] s, string errors, int[] consumed)
        {
            int size = s.Length;
            if (size == 0)
            {
                if (consumed != null)
                    consumed[0] = 0;
                return PyString.Empty;
            }
            var writer = new PyUnicodeWriter();
            int end = size;
            int q = 0;
            Handler? errorHandler = null;
            while (q < end)
            {
                int ch = Utf8Decode(s, ref q, end, writer);
                string errmsg;
                int startinpos, endinpos;
                switch (ch)
                {
                    case 0:
                        if (q == end || consumed != null)
                            goto End;
                        errmsg = "unexpected end of data";
                        startinpos = q;
                        endinpos = end;
                        break;
                    case 1:
                        errmsg = "invalid start byte";
                        startinpos = q;
                        endinpos = startinpos + 1;
                        break;
                    default:
                        if (ch == 2 && consumed != null && s[q] == 0xED && end - q == 2 && s[q + 1] >= 0xA0 && s[q + 1] <= 0xBF)
                            // Truncated surrogate code in range D800-DFFF
                            goto End;
                        errmsg = "invalid continuation byte";
                        startinpos = q;
                        endinpos = startinpos + ch - 1;
                        break;
                }

                errorHandler ??= GetErrorHandler(errors);
                switch (errorHandler)
                {
                    case Handler.Ignore:
                        q += endinpos - startinpos;
                        break;
                    case Handler.Replace:
                        writer.WriteChar(0xFFFD);
                        q += endinpos - startinpos;
                        break;
                    case Handler.SurrogateEscape:
                        for (int i = startinpos; i < endinpos; i++)
                            writer.WriteChar(s[i] + 0xdc00);
                        q += endinpos - startinpos;
                        break;
                    default:
                        q = PyCodecErrors.CallDecode(errors, "utf-8", errmsg, ref s, startinpos, endinpos, writer);
                        end = s.Length;
                        break;
                }
            }
        End:
            if (consumed != null)
                consumed[0] = q;
            return writer.Finish();
        }

        /// <summary>
        /// PyUnicode_DecodeUTF16Stateful: byteorder -1 little, 1 big, 0 native (little) after an
        /// optional BOM, which sets it.
        /// </summary>
        internal static PyString DecodeUtf16(byte[] s, string errors, ref int byteorder, int[] consumed)
        {
            int size = s.Length;
            int q = 0, e = size;
            int bo = byteorder;
            // Check for BOM marks (U+FEFF) in the input and adjust current byte order setting
            // accordingly. In native mode, the leading BOM mark is skipped.
            if (bo == 0 && size >= 2)
            {
                int bom = (s[1] << 8) | s[0];
                if (bom == 0xFEFF)
                {
                    q += 2;
                    bo = -1;
                }
                else if (bom == 0xFFFE)
                {
                    q += 2;
                    bo = 1;
                }
                byteorder = bo;
            }
            if (q == e)
            {
                if (consumed != null)
                    consumed[0] = size;
                return PyString.Empty;
            }
            bool little = bo <= 0;
            string encoding = little ? "utf-16-le" : "utf-16-be";
            int Unit(int at) => little ? (s[at + 1] << 8) | s[at] : (s[at] << 8) | s[at + 1];
            var writer = new PyUnicodeWriter();

            while (true)
            {
                // stringlib utf16_decode: 0 at the end, 1 unexpected end, 2 illegal encoding,
                // 3 illegal surrogate.
                int ch = 0;
                if (e - q >= 2)
                {
                    int last = e - 1;
                    while (q < last)
                    {
                        int c = Unit(q);
                        q += 2;
                        if (c < 0xD800 || c > 0xDFFF)
                        {
                            writer.WriteChar(c);
                            continue;
                        }
                        // UTF-16 code pair:
                        if (c >= 0xDC00)
                        {
                            ch = 2;
                            break;
                        }
                        if (q >= last)
                        {
                            ch = 1;
                            break;
                        }
                        int c2 = Unit(q);
                        q += 2;
                        if (c2 < 0xDC00 || c2 > 0xDFFF)
                        {
                            ch = 3;
                            break;
                        }
                        // Py_UNICODE_JOIN_SURROGATES
                        writer.WriteChar(0x10000 + (((c & 0x3FF) << 10) | (c2 & 0x3FF)));
                    }
                }
                string errmsg;
                int startinpos, endinpos;
                switch (ch)
                {
                    case 0:
                        // remaining byte at the end? (size should be even)
                        if (q == e || consumed != null)
                            goto End;
                        errmsg = "truncated data";
                        startinpos = q;
                        endinpos = e;
                        break;
                    case 1:
                        q -= 2;
                        if (consumed != null)
                            goto End;
                        errmsg = "unexpected end of data";
                        startinpos = q;
                        endinpos = e;
                        break;
                    case 2:
                        errmsg = "illegal encoding";
                        startinpos = q - 2;
                        endinpos = startinpos + 2;
                        break;
                    default:
                        errmsg = "illegal UTF-16 surrogate";
                        startinpos = q - 4;
                        endinpos = startinpos + 2;
                        break;
                }
                // The remaining input chars are ignored if the callback chooses to skip the input
                q = PyCodecErrors.CallDecode(errors, encoding, errmsg, ref s, startinpos, endinpos, writer);
                e = s.Length;
            }
        End:
            if (consumed != null)
                consumed[0] = q;
            return writer.Finish();
        }

        /// <summary>PyUnicode_DecodeUTF32Stateful, with the byte orders of DecodeUtf16.</summary>
        internal static PyString DecodeUtf32(byte[] s, string errors, ref int byteorder, int[] consumed)
        {
            int size = s.Length;
            int q = 0, e = size;
            int bo = byteorder;
            if (bo == 0 && size >= 4)
            {
                uint bom = ((uint)s[3] << 24) | ((uint)s[2] << 16) | ((uint)s[1] << 8) | s[0];
                if (bom == 0x0000FEFF)
                {
                    bo = -1;
                    q += 4;
                }
                else if (bom == 0xFFFE0000)
                {
                    bo = 1;
                    q += 4;
                }
                byteorder = bo;
            }
            if (q == e)
            {
                if (consumed != null)
                    consumed[0] = size;
                return PyString.Empty;
            }
            bool le = bo <= 0;
            string encoding = le ? "utf-32-le" : "utf-32-be";
            var writer = new PyUnicodeWriter();

            while (true)
            {
                long ch = 0;
                string errmsg;
                int startinpos, endinpos;
                if (e - q >= 4)
                {
                    int last = e - 4;
                    do
                    {
                        ch = le
                            ? ((long)s[q + 3] << 24) | ((long)s[q + 2] << 16) | ((long)s[q + 1] << 8) | s[q]
                            : ((long)s[q] << 24) | ((long)s[q + 1] << 16) | ((long)s[q + 2] << 8) | s[q + 3];
                        if (ch > 0x10FFFF || (ch >= 0xD800 && ch <= 0xDFFF))
                            break;
                        writer.WriteChar((int)ch);
                        q += 4;
                    } while (q <= last);
                }
                if (ch >= 0xD800 && ch <= 0xDFFF)
                {
                    errmsg = "code point in surrogate code point range(0xd800, 0xe000)";
                    startinpos = q;
                    endinpos = startinpos + 4;
                }
                else if (ch <= 0x10FFFF)
                {
                    if (q == e || consumed != null)
                        break;
                    // remaining bytes at the end? (size should be divisible by 4)
                    errmsg = "truncated data";
                    startinpos = q;
                    endinpos = e;
                }
                else
                {
                    errmsg = "code point not in range(0x110000)";
                    startinpos = q;
                    endinpos = startinpos + 4;
                }
                // The remaining input chars are ignored if the callback chooses to skip the input
                q = PyCodecErrors.CallDecode(errors, encoding, errmsg, ref s, startinpos, endinpos, writer);
                e = s.Length;
            }
            if (consumed != null)
                consumed[0] = q;
            return writer.Finish();
        }

        /// <summary>PyUnicode_DecodeASCII</summary>
        internal static PyString DecodeAscii(byte[] s, string errors)
        {
            var writer = new PyUnicodeWriter();
            int e = s.Length;
            int q = 0;
            Handler? errorHandler = null;
            while (q < e)
            {
                int c = s[q];
                if (c < 128)
                {
                    writer.WriteChar(c);
                    ++q;
                    continue;
                }
                // byte outsize range 0x00..0x7f: call the error handler
                errorHandler ??= GetErrorHandler(errors);
                switch (errorHandler)
                {
                    case Handler.Replace:
                    case Handler.SurrogateEscape:
                        // Fast-path: the error handler only writes one character
                        writer.WriteChar(errorHandler == Handler.Replace ? 0xFFFD : c + 0xdc00);
                        ++q;
                        break;
                    case Handler.Ignore:
                        ++q;
                        break;
                    default:
                        q = PyCodecErrors.CallDecode(errors, "ascii", "ordinal not in range(128)", ref s, q, q + 1, writer);
                        e = s.Length;
                        break;
                }
            }
            return writer.Finish();
        }

        /// <summary>PyUnicode_DecodeLatin1: latin-1 is equivalent to the first 256 ordinals.</summary>
        internal static PyString DecodeLatin1(byte[] s)
        {
            var chars = new char[s.Length];
            for (int i = 0; i < s.Length; i++)
                chars[i] = (char)s[i];
            return PyString.FromUnits(new string(chars));
        }
    }
}
