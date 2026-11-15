using System.Collections.Generic;
using System.Text;

namespace SharpPy
{
    /// <summary>
    /// The escape codecs of CPython 3.12.15, which the parser's string literals and _codecs share:
    /// _PyUnicode_DecodeUnicodeEscapeInternal2 and _PyUnicode_DecodeUnicodeEscapeStateful,
    /// _PyUnicode_DecodeRawUnicodeEscapeStateful, PyUnicode_AsUnicodeEscapeString and
    /// PyUnicode_AsRawUnicodeEscapeString (Objects/unicodeobject.c), and _PyBytes_DecodeEscape2
    /// and PyBytes_DecodeEscape (Objects/bytesobject.c). consumed is null for a final decode.
    /// </summary>
    internal static class EscapeCodecs
    {
        private const int MAX_UNICODE = 0x10FFFF;
        private const string Hexdigits = "0123456789abcdef";

        // WRITE_CHAR
        private static void WriteChar(StringBuilder writer, int ch)
        {
            if (ch < 0x10000) writer.Append((char)ch);
            else writer.Append(char.ConvertFromUtf32(ch));
        }

        private static int HexValue(int c) =>
            c >= '0' && c <= '9' ? c - '0'
            : c >= 'a' && c <= 'f' ? c - ('a' - 10)
            : c >= 'A' && c <= 'F' ? c - ('A' - 10)
            : -1;

        /// <summary>
        /// _PyUnicode_DecodeUnicodeEscapeInternal2: the first invalid escape is reported as its
        /// character (an octal value above 0o377 is the value) and its position in s, which is -1
        /// once an error handler has replaced the input.
        /// </summary>
        internal static string DecodeUnicodeEscapeInternal2(byte[] s, string errors, int[] consumed,
            out int firstInvalidEscapeChar, out int firstInvalidEscapePos)
        {
            var initialStarts = s;
            // so we can remember if we've seen an invalid escape char or not
            firstInvalidEscapeChar = -1;
            firstInvalidEscapePos = -1;

            if (s.Length == 0)
            {
                if (consumed != null)
                    consumed[0] = 0;
                return "";
            }
            var writer = new StringBuilder(s.Length);
            int end = s.Length;
            int p = 0;
            while (p < end)
            {
                int c = s[p++];
                int ch;
                int count;
                string message;

                // Non-escape characters are interpreted as Unicode ordinals
                if (c != '\\')
                {
                    WriteChar(writer, c);
                    continue;
                }

                int startinpos = p - 1;
                // \ - Escapes
                if (p >= end)
                {
                    message = "\\ at end of string";
                    goto incomplete;
                }
                c = s[p++];

                switch (c)
                {
                    case '\n': continue;
                    case '\\': writer.Append('\\'); continue;
                    case '\'': writer.Append('\''); continue;
                    case '"': writer.Append('"'); continue;
                    case 'b': writer.Append('\b'); continue;
                    case 'f': writer.Append('\f'); continue;
                    case 't': writer.Append('\t'); continue;
                    case 'n': writer.Append('\n'); continue;
                    case 'r': writer.Append('\r'); continue;
                    case 'v': writer.Append('\v'); continue;
                    case 'a': writer.Append('\a'); continue;

                    // \OOO (octal) escapes
                    case >= '0' and <= '7':
                        ch = c - '0';
                        if (p < end && '0' <= s[p] && s[p] <= '7')
                        {
                            ch = (ch << 3) + s[p++] - '0';
                            if (p < end && '0' <= s[p] && s[p] <= '7')
                                ch = (ch << 3) + s[p++] - '0';
                        }
                        if (ch > 255 && firstInvalidEscapeChar == -1)
                        {
                            firstInvalidEscapeChar = ch;
                            if (ReferenceEquals(s, initialStarts))
                                // Back up 3 chars, since we've already incremented s.
                                firstInvalidEscapePos = p - 3;
                        }
                        WriteChar(writer, ch);
                        continue;

                    // hex escapes
                    case 'x':
                        count = 2;
                        message = "truncated \\xXX escape";
                        goto hexescape;
                    case 'u':
                        count = 4;
                        message = "truncated \\uXXXX escape";
                        goto hexescape;
                    case 'U':
                        count = 8;
                        message = "truncated \\UXXXXXXXX escape";
                    hexescape:
                        {
                            long value = 0;
                            for (; count != 0; ++p, --count)
                            {
                                if (p >= end)
                                    goto incomplete;
                                int digit = HexValue(s[p]);
                                if (digit < 0)
                                    goto error;
                                value = (value << 4) + digit;
                            }
                            // when we get here, ch is a 32-bit unicode character
                            if (value > MAX_UNICODE)
                            {
                                message = "illegal Unicode character";
                                goto error;
                            }
                            WriteChar(writer, (int)value);
                            continue;
                        }

                    // \N{name}
                    case 'N':
                        message = "malformed \\N character escape";
                        if (p >= end)
                            goto incomplete;
                        if (s[p] == '{')
                        {
                            int start = ++p;
                            // look for the closing brace
                            while (p < end && s[p] != '}')
                                p++;
                            if (p >= end)
                                goto incomplete;
                            int namelen = p - start;
                            if (namelen != 0)
                            {
                                // found a name. look it up in the unicode database
                                p++;
                                int named = UnicodeNames.Lookup(Encoding.Latin1.GetString(s, start, namelen));
                                if (named >= 0)
                                {
                                    WriteChar(writer, named);
                                    continue;
                                }
                                message = "unknown Unicode character name";
                            }
                        }
                        goto error;

                    default:
                        if (firstInvalidEscapeChar == -1)
                        {
                            firstInvalidEscapeChar = c;
                            if (ReferenceEquals(s, initialStarts))
                                // Back up one char, since we've already incremented s.
                                firstInvalidEscapePos = p - 1;
                        }
                        writer.Append('\\');
                        WriteChar(writer, c);
                        continue;
                }

            incomplete:
                if (consumed != null)
                {
                    consumed[0] = startinpos;
                    break;
                }
            error:
                int endinpos = p;
                p = PyCodecErrors.CallDecode(errors, "unicodeescape", message, ref s, startinpos, endinpos, writer);
                end = s.Length;
            }
            return writer.ToString();
        }

        // The DeprecationWarning of _PyUnicode_DecodeUnicodeEscapeStateful and PyBytes_DecodeEscape.
        private static void WarnInvalidEscape(int firstInvalidEscapeChar)
        {
            if (firstInvalidEscapeChar == -1)
                return;
            string message = firstInvalidEscapeChar > 0xff
                ? $"invalid octal escape sequence '\\{System.Convert.ToString(firstInvalidEscapeChar, 8)}'"
                : $"invalid escape sequence '\\{(char)firstInvalidEscapeChar}'";
            PyWarnings.Warn(PyType.DeprecationWarningType, message, 1);
        }

        /// <summary>_PyUnicode_DecodeUnicodeEscapeStateful</summary>
        internal static string DecodeUnicodeEscapeStateful(byte[] s, string errors, int[] consumed)
        {
            string result = DecodeUnicodeEscapeInternal2(s, errors, consumed, out int firstInvalidEscapeChar, out _);
            WarnInvalidEscape(firstInvalidEscapeChar);
            return result;
        }

        /// <summary>_PyUnicode_DecodeRawUnicodeEscapeStateful</summary>
        internal static string DecodeRawUnicodeEscapeStateful(byte[] s, string errors, int[] consumed)
        {
            if (s.Length == 0)
            {
                if (consumed != null)
                    consumed[0] = 0;
                return "";
            }
            var writer = new StringBuilder(s.Length);
            int end = s.Length;
            int p = 0;
            while (p < end)
            {
                int c = s[p++];
                int count;
                string message;

                // Non-escape characters are interpreted as Unicode ordinals
                if (c != '\\' || (p >= end && consumed == null))
                {
                    WriteChar(writer, c);
                    continue;
                }

                int startinpos = p - 1;
                // \ - Escapes
                if (p >= end)
                {
                    message = "\\ at end of string";
                    goto incomplete;
                }

                c = s[p++];
                if (c == 'u')
                {
                    count = 4;
                    message = "truncated \\uXXXX escape";
                }
                else if (c == 'U')
                {
                    count = 8;
                    message = "truncated \\UXXXXXXXX escape";
                }
                else
                {
                    writer.Append('\\');
                    WriteChar(writer, c);
                    continue;
                }

                {
                    // \uHHHH with 4 hex digits, \U00HHHHHH with 8
                    long ch = 0;
                    for (; count != 0; ++p, --count)
                    {
                        if (p >= end)
                            goto incomplete;
                        int digit = HexValue(s[p]);
                        if (digit < 0)
                            goto error;
                        ch = (ch << 4) + digit;
                    }
                    if (ch > MAX_UNICODE)
                    {
                        message = "\\Uxxxxxxxx out of range";
                        goto error;
                    }
                    WriteChar(writer, (int)ch);
                    continue;
                }

            incomplete:
                if (consumed != null)
                {
                    consumed[0] = startinpos;
                    break;
                }
            error:
                int endinpos = p;
                p = PyCodecErrors.CallDecode(errors, "rawunicodeescape", message, ref s, startinpos, endinpos, writer);
                end = s.Length;
            }
            return writer.ToString();
        }

        /// <summary>PyUnicode_AsUnicodeEscapeString</summary>
        internal static byte[] AsUnicodeEscapeString(string unicode)
        {
            var p = new List<byte>(unicode.Length);
            foreach (int ch in UnicodeEncoders.CodePoints(unicode))
            {
                // U+0000-U+00ff range
                if (ch < 0x100)
                {
                    if (ch >= ' ' && ch < 127)
                    {
                        if (ch != '\\')
                            // Copy printable US ASCII as-is
                            p.Add((byte)ch);
                        else
                        {
                            // Escape backslashes
                            p.Add((byte)'\\');
                            p.Add((byte)'\\');
                        }
                    }
                    // Map special whitespace to '\t', \n', '\r'
                    else if (ch == '\t') { p.Add((byte)'\\'); p.Add((byte)'t'); }
                    else if (ch == '\n') { p.Add((byte)'\\'); p.Add((byte)'n'); }
                    else if (ch == '\r') { p.Add((byte)'\\'); p.Add((byte)'r'); }
                    // Map non-printable US ASCII and 8-bit characters to '\xHH'
                    else
                    {
                        p.Add((byte)'\\');
                        p.Add((byte)'x');
                        p.Add((byte)Hexdigits[(ch >> 4) & 0xF]);
                        p.Add((byte)Hexdigits[ch & 0xF]);
                    }
                }
                // U+0100-U+ffff range: Map 16-bit characters to '\uHHHH'
                else if (ch < 0x10000)
                    AppendHex(p, 'u', ch, 4);
                // U+010000-U+10ffff range: Map 21-bit characters to '\U00HHHHHH'
                else
                    AppendHex(p, 'U', ch, 8);
            }
            return p.ToArray();
        }

        private static void AppendHex(List<byte> p, char kind, int ch, int digits)
        {
            p.Add((byte)'\\');
            p.Add((byte)kind);
            for (int shift = 4 * (digits - 1); shift >= 0; shift -= 4)
                p.Add((byte)Hexdigits[(ch >> shift) & 0xF]);
        }

        /// <summary>PyUnicode_AsRawUnicodeEscapeString</summary>
        internal static byte[] AsRawUnicodeEscapeString(string unicode)
        {
            var p = new List<byte>(unicode.Length);
            foreach (int ch in UnicodeEncoders.CodePoints(unicode))
            {
                // U+0000-U+00ff range: Copy 8-bit characters as-is
                if (ch < 0x100)
                    p.Add((byte)ch);
                // U+0100-U+ffff range: Map 16-bit characters to '\uHHHH'
                else if (ch < 0x10000)
                    AppendHex(p, 'u', ch, 4);
                // U+010000-U+10ffff range: Map 32-bit characters to '\U00HHHHHH'
                else
                    AppendHex(p, 'U', ch, 8);
            }
            return p.ToArray();
        }

        private static bool IsXDigit(int c) => HexValue(c) >= 0;

        /// <summary>_PyBytes_DecodeEscape2: errors is strict, replace or ignore.</summary>
        internal static byte[] DecodeEscape2(byte[] s, string errors, out int firstInvalidEscapeChar, out int firstInvalidEscapePos)
        {
            var p = new List<byte>(s.Length);
            firstInvalidEscapeChar = -1;
            firstInvalidEscapePos = -1;
            int end = s.Length;
            int i = 0;
            while (i < end)
            {
                if (s[i] != '\\')
                {
                    p.Add(s[i++]);
                    continue;
                }

                i++;
                if (i == end)
                    throw PyValueError.Create("Trailing \\ in string");

                switch (s[i++])
                {
                    case (byte)'\n': break;
                    case (byte)'\\': p.Add((byte)'\\'); break;
                    case (byte)'\'': p.Add((byte)'\''); break;
                    case (byte)'"': p.Add((byte)'"'); break;
                    case (byte)'b': p.Add((byte)'\b'); break;
                    case (byte)'f': p.Add(0x0c); break;
                    case (byte)'t': p.Add((byte)'\t'); break;
                    case (byte)'n': p.Add((byte)'\n'); break;
                    case (byte)'r': p.Add((byte)'\r'); break;
                    case (byte)'v': p.Add(0x0b); break;
                    case (byte)'a': p.Add(0x07); break;
                    case >= (byte)'0' and <= (byte)'7':
                        {
                            int c = s[i - 1] - '0';
                            if (i < end && '0' <= s[i] && s[i] <= '7')
                            {
                                c = (c << 3) + s[i++] - '0';
                                if (i < end && '0' <= s[i] && s[i] <= '7')
                                    c = (c << 3) + s[i++] - '0';
                            }
                            if (c > 255 && firstInvalidEscapeChar == -1)
                            {
                                firstInvalidEscapeChar = c;
                                // Back up 3 chars, since we've already incremented s.
                                firstInvalidEscapePos = i - 3;
                            }
                            p.Add((byte)c);
                            break;
                        }
                    case (byte)'x':
                        if (i + 1 < end)
                        {
                            int digit1 = HexValue(s[i]), digit2 = HexValue(s[i + 1]);
                            if (digit1 >= 0 && digit2 >= 0)
                            {
                                p.Add((byte)((digit1 << 4) + digit2));
                                i += 2;
                                break;
                            }
                        }
                        // invalid hexadecimal digits
                        if (errors == null || errors == "strict")
                            throw PyValueError.Create($"invalid \\x escape at position {i - 2}");
                        if (errors == "replace")
                            p.Add((byte)'?');
                        else if (errors != "ignore")
                            throw PyValueError.Create($"decoding error; unknown error handling code: {errors}");
                        // skip \x
                        if (i < end && IsXDigit(s[i]))
                            i++;  // and a hexdigit
                        break;
                    default:
                        if (firstInvalidEscapeChar == -1)
                        {
                            firstInvalidEscapeChar = s[i - 1];
                            // Back up one char, since we've already incremented s.
                            firstInvalidEscapePos = i - 1;
                        }
                        p.Add((byte)'\\');
                        i--;
                        break;
                }
            }
            return p.ToArray();
        }

        /// <summary>PyBytes_DecodeEscape</summary>
        internal static byte[] DecodeEscape(byte[] s, string errors)
        {
            var result = DecodeEscape2(s, errors, out int firstInvalidEscapeChar, out _);
            WarnInvalidEscape(firstInvalidEscapeChar);
            return result;
        }
    }
}
