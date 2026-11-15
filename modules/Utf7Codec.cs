using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// The UTF-7 codec of Objects/unicodeobject.c (CPython 3.12.15): PyUnicode_DecodeUTF7Stateful
    /// and _PyUnicode_EncodeUTF7, RFC 2152, encoding conservatively and decoding liberally.
    /// </summary>
    internal static class Utf7Codec
    {
        // IS_BASE64: is c a base-64 character?
        private static bool IsBase64(int c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/';

        // FROM_BASE64: given that c is a base-64 character, its base-64 value.
        private static int FromBase64(int c) =>
            c >= 'A' && c <= 'Z' ? c - 'A'
            : c >= 'a' && c <= 'z' ? c - 'a' + 26
            : c >= '0' && c <= '9' ? c - '0' + 52
            : c == '+' ? 62 : 63;

        // TO_BASE64: the base-64 character of the bottom 6 bits of n.
        private static byte ToBase64(ulong n) =>
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"u8[(int)(n & 0x3f)];

        // DECODE_DIRECT: the only ASCII byte not decoding to itself is the + which begins base64.
        private static bool DecodeDirect(int c) => c <= 127 && c != '+';

        // utf7_category: 0 Set D, 1 Set O, 2 whitespace, 3 special (must be base64 encoded).
        private static readonly byte[] Utf7Category =
        {
            3, 3, 3, 3, 3, 3, 3, 3, 3, 2, 2, 3, 3, 2, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            2, 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 3, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 0,
            1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 3, 1, 1, 1,
            1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 3, 3,
        };

        // ENCODE_DIRECT: whether this character is encoded as itself.
        private static bool EncodeDirect(int c, bool directO, bool directWS) =>
            c < 128 && c > 0 &&
            (Utf7Category[c] == 0 || (directWS && Utf7Category[c] == 2) || (directO && Utf7Category[c] == 1));

        private static void WriteChar(StringBuilder writer, int ch)
        {
            if (ch < 0x10000) writer.Append((char)ch);
            else writer.Append(char.ConvertFromUtf32(ch));
        }

        /// <summary>
        /// PyUnicode_DecodeUTF7Stateful. The only state kept is the read position, so a call that
        /// ends in a shift sequence backs off the read position and the output to its start.
        /// consumed is null for a final decode.
        /// </summary>
        internal static string Decode(byte[] s, string errors, int[] consumed)
        {
            int size = s.Length;
            if (size == 0)
            {
                if (consumed != null) consumed[0] = 0;
                return "";
            }
            var writer = new StringBuilder(size);
            string errmsg;
            bool inShift = false;
            int shiftOutStart = 0;
            int startinpos = 0;
            int endinpos;
            int base64bits = 0;
            ulong base64buffer = 0;
            int surrogate = 0;
            int e = size;
            int p = 0;

            // restart: the unterminated-shift handler resumes the loop below the end of string.
            for (;;)
            {
                while (p < e)
                {
                    int ch = s[p];

                    if (inShift)
                    {
                        // in a base-64 section
                        if (IsBase64(ch))
                        {
                            // consume a base-64 character
                            base64buffer = (base64buffer << 6) | (uint)FromBase64(ch);
                            base64bits += 6;
                            p++;
                            if (base64bits >= 16)
                            {
                                // we have enough bits for a UTF-16 value
                                int outCh = (int)(base64buffer >> (base64bits - 16));
                                base64bits -= 16;
                                base64buffer &= (1UL << base64bits) - 1;  // clear high bits
                                if (surrogate != 0)
                                {
                                    // expecting a second surrogate
                                    if (outCh >= 0xDC00 && outCh <= 0xDFFF)
                                    {
                                        WriteChar(writer, 0x10000 + ((surrogate - 0xD800) << 10) + (outCh - 0xDC00));
                                        surrogate = 0;
                                        continue;
                                    }
                                    WriteChar(writer, surrogate);
                                    surrogate = 0;
                                }
                                if (outCh >= 0xD800 && outCh <= 0xDBFF)
                                    surrogate = outCh;  // first surrogate
                                else
                                    WriteChar(writer, outCh);
                            }
                            continue;
                        }
                        // now leaving a base-64 section
                        inShift = false;
                        if (base64bits > 0)
                        {
                            // left-over bits
                            if (base64bits >= 6)
                            {
                                // We've seen at least one base-64 character
                                p++;
                                errmsg = "partial character in shift sequence";
                                goto utf7Error;
                            }
                            // Some bits remain; they should be zero
                            if (base64buffer != 0)
                            {
                                p++;
                                errmsg = "non-zero padding bits in shift sequence";
                                goto utf7Error;
                            }
                        }
                        if (surrogate != 0 && DecodeDirect(ch))
                            WriteChar(writer, surrogate);
                        surrogate = 0;
                        // '-' is absorbed; other terminating characters are preserved
                        if (ch == '-')
                            p++;
                        continue;
                    }
                    if (ch == '+')
                    {
                        startinpos = p;
                        p++;  // consume '+'
                        if (p < e && s[p] == '-')
                        {
                            // '+-' encodes '+'
                            p++;
                            writer.Append('+');
                        }
                        else if (p < e && !IsBase64(s[p]))
                        {
                            p++;
                            errmsg = "ill-formed sequence";
                            goto utf7Error;
                        }
                        else
                        {
                            // begin base64-encoded section
                            inShift = true;
                            surrogate = 0;
                            shiftOutStart = writer.Length;
                            base64bits = 0;
                            base64buffer = 0;
                        }
                        continue;
                    }
                    if (DecodeDirect(ch))
                    {
                        // character decodes as itself
                        p++;
                        writer.Append((char)ch);
                        continue;
                    }
                    startinpos = p;
                    p++;
                    errmsg = "unexpected special character";

                utf7Error:
                    endinpos = p;
                    p = PyCodecErrors.CallDecode(errors, "utf7", errmsg, ref s, startinpos, endinpos, writer);
                    e = s.Length;
                }

                // end of string
                if (inShift && consumed == null)
                {
                    // in shift sequence, no more to follow: an inconsistent state is an error
                    inShift = false;
                    if (surrogate != 0 || base64bits >= 6 || (base64bits > 0 && base64buffer != 0))
                    {
                        endinpos = size;
                        p = PyCodecErrors.CallDecode(errors, "utf7", "unterminated shift sequence", ref s, startinpos, endinpos, writer);
                        e = s.Length;
                        if (p < e)
                            continue;
                    }
                }
                break;
            }

            // return state
            if (consumed != null)
            {
                if (inShift)
                {
                    consumed[0] = startinpos;
                    writer.Length = shiftOutStart;  // back off output
                }
                else
                    consumed[0] = p;
            }
            return writer.ToString();
        }

        /// <summary>_PyUnicode_EncodeUTF7</summary>
        internal static byte[] Encode(string str, bool base64SetO, bool base64WhiteSpace)
        {
            int len = str.Length;
            if (len == 0)
                return System.Array.Empty<byte>();
            // Every UTF-16 unit is at most 8 bytes, as for the code points CPython counts.
            var v = new byte[PyAllocation.ArrayLength((long)len * 8)];
            int o = 0;
            bool inShift = false;
            int base64bits = 0;
            ulong base64buffer = 0;
            for (int i = 0; i < len; ++i)
            {
                int ch = str[i];
                if (char.IsHighSurrogate(str[i]) && i + 1 < len && char.IsLowSurrogate(str[i + 1]))
                    ch = char.ConvertToUtf32(str[i], str[++i]);

                if (inShift)
                {
                    if (EncodeDirect(ch, !base64SetO, !base64WhiteSpace))
                    {
                        // shifting out
                        if (base64bits != 0)
                        {
                            // output remaining bits
                            v[o++] = ToBase64(base64buffer << (6 - base64bits));
                            base64buffer = 0;
                            base64bits = 0;
                        }
                        inShift = false;
                        // Characters not in the BASE64 set implicitly unshift the sequence so no
                        // '-' is required, except if the character is itself a '-'
                        if (IsBase64(ch) || ch == '-')
                            v[o++] = (byte)'-';
                        v[o++] = (byte)ch;
                        continue;
                    }
                }
                else
                {
                    // not in a shift sequence
                    if (ch == '+')
                    {
                        v[o++] = (byte)'+';
                        v[o++] = (byte)'-';
                        continue;
                    }
                    if (EncodeDirect(ch, !base64SetO, !base64WhiteSpace))
                    {
                        v[o++] = (byte)ch;
                        continue;
                    }
                    v[o++] = (byte)'+';
                    inShift = true;
                }

                // encode_char
                if (ch >= 0x10000)
                {
                    // code first surrogate
                    base64bits += 16;
                    base64buffer = (base64buffer << 16) | (uint)(0xD800 - (0x10000 >> 10) + (ch >> 10));
                    while (base64bits >= 6)
                    {
                        v[o++] = ToBase64(base64buffer >> (base64bits - 6));
                        base64bits -= 6;
                    }
                    // prepare second surrogate
                    ch = 0xDC00 + (ch & 0x3FF);
                }
                base64bits += 16;
                base64buffer = (base64buffer << 16) | (uint)ch;
                while (base64bits >= 6)
                {
                    v[o++] = ToBase64(base64buffer >> (base64bits - 6));
                    base64bits -= 6;
                }
            }
            if (base64bits != 0)
                v[o++] = ToBase64(base64buffer << (6 - base64bits));
            if (inShift)
                v[o++] = (byte)'-';
            System.Array.Resize(ref v, o);
            return v;
        }
    }
}
