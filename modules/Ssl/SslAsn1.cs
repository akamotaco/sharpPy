using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPy.Modules.Ssl
{
    /// <summary>
    /// The part of libcrypto's ASN.1 layer _ssl reads certificates through: a DER/BER TLV reader
    /// (d2i_*), OBJECT IDENTIFIER text (OBJ_obj2txt), ASN1_STRING_to_UTF8, i2a_ASN1_INTEGER and
    /// ASN1_TIME_print. Managed code only; System.Formats.Asn1 is not used so that the leniency
    /// (and the failures) are OpenSSL's, not the BCL's.
    /// </summary>
    internal readonly struct Tlv
    {
        public readonly byte[] Data;
        public readonly int Start;      // first byte of the identifier octets
        public readonly int HeaderLength;
        public readonly int Length;     // content length
        public readonly int Class;      // 0 universal, 1 application, 2 context, 3 private
        public readonly bool Constructed;
        public readonly int Tag;

        public Tlv(byte[] data, int start, int headerLength, int length, int cls, bool constructed, int tag)
        {
            Data = data; Start = start; HeaderLength = headerLength; Length = length;
            Class = cls; Constructed = constructed; Tag = tag;
        }

        public int ContentStart => Start + HeaderLength;
        public int End => Start + HeaderLength + Length;
        public int TotalLength => HeaderLength + Length;
        public ReadOnlySpan<byte> Content => new(Data, ContentStart, Length);
        public byte[] ContentBytes() => Content.ToArray();
        public byte[] Encoded() => new ReadOnlySpan<byte>(Data, Start, TotalLength).ToArray();
        public bool Is(int cls, int tag) => Class == cls && Tag == tag;

        /// <summary>The elements of a constructed value.</summary>
        public List<Tlv> Children()
        {
            var result = new List<Tlv>();
            int pos = ContentStart;
            while (pos < End)
            {
                var child = Asn1.Read(Data, pos, End);
                result.Add(child);
                pos = child.End;
            }
            return result;
        }
    }

    internal sealed class Asn1Exception : Exception
    {
        public readonly string Reason;
        public Asn1Exception(string reason) : base(reason) => Reason = reason;
    }

    internal static class Asn1
    {
        // Universal tags (V_ASN1_*)
        internal const int BOOLEAN = 1, INTEGER = 2, BIT_STRING = 3, OCTET_STRING = 4, NULL = 5, OBJECT = 6,
            ENUMERATED = 10, UTF8STRING = 12, SEQUENCE = 16, SET = 17, NUMERICSTRING = 18, PRINTABLESTRING = 19,
            T61STRING = 20, VIDEOTEXSTRING = 21, IA5STRING = 22, UTCTIME = 23, GENERALIZEDTIME = 24,
            GRAPHICSTRING = 25, VISIBLESTRING = 26, GENERALSTRING = 27, UNIVERSALSTRING = 28, BMPSTRING = 30;

        /// <summary>ASN1_get_object: one TLV at pos, which must end by limit ("not enough data", "header too long").</summary>
        internal static Tlv Read(byte[] data, int pos, int limit)
        {
            int start = pos;
            if (pos >= limit)
                throw new Asn1Exception("NOT_ENOUGH_DATA");
            int b = data[pos++];
            int cls = b >> 6;
            bool constructed = (b & 0x20) != 0;
            int tag = b & 0x1f;
            if (tag == 0x1f)
            {
                tag = 0;
                while (true)
                {
                    if (pos >= limit)
                        throw new Asn1Exception("HEADER_TOO_LONG");
                    int c = data[pos++];
                    tag = (tag << 7) | (c & 0x7f);
                    if ((c & 0x80) == 0)
                        break;
                    if (tag > 0xffffff)
                        throw new Asn1Exception("HEADER_TOO_LONG");
                }
            }
            if (pos >= limit)
                throw new Asn1Exception("HEADER_TOO_LONG");
            int l = data[pos++];
            long length;
            if (l < 0x80)
                length = l;
            else if (l == 0x80)
            {
                // The indefinite length of BER: certificates, keys and CRLs are DER here.
                throw new Asn1Exception("HEADER_TOO_LONG");
            }
            else
            {
                int n = l & 0x7f;
                if (n > 4 || pos + n > limit)
                    throw new Asn1Exception("HEADER_TOO_LONG");
                length = 0;
                for (int i = 0; i < n; i++)
                    length = (length << 8) | data[pos++];
            }
            if (pos + length > limit)
                throw new Asn1Exception("NOT_ENOUGH_DATA");
            return new Tlv(data, start, pos - start, (int)length, cls, constructed, tag);
        }

        internal static Tlv Parse(byte[] data) => Read(data, 0, data.Length);

        // ---- OBJECT IDENTIFIER ----

        /// <summary>The dotted text of an OID's content octets (OBJ_obj2txt with no_name = 1).</summary>
        internal static string OidText(ReadOnlySpan<byte> content)
        {
            var text = new StringBuilder();
            System.Numerics.BigInteger value = 0;
            bool first = true;
            for (int i = 0; i < content.Length; i++)
            {
                value = (value << 7) | (content[i] & 0x7f);
                if ((content[i] & 0x80) != 0)
                    continue;
                if (first)
                {
                    int top = value < 40 ? 0 : value < 80 ? 1 : 2;
                    text.Append(top).Append('.').Append(value - 40 * top);
                    first = false;
                }
                else
                    text.Append('.').Append(value);
                value = 0;
            }
            return text.ToString();
        }

        /// <summary>The content octets of a dotted OID (a2d_ASN1_OBJECT), or null when it is not one.</summary>
        internal static byte[] OidEncode(string text)
        {
            var parts = text.Split('.');
            if (parts.Length < 2)
                return null;
            var values = new List<System.Numerics.BigInteger>();
            foreach (var part in parts)
            {
                if (part.Length == 0)
                    return null;
                foreach (var ch in part)
                    if (ch < '0' || ch > '9')
                        return null;
                values.Add(System.Numerics.BigInteger.Parse(part));
            }
            if (values[0] > 2 || (values[0] < 2 && values[1] >= 40))
                return null;
            var output = new List<byte>();
            void Put(System.Numerics.BigInteger v)
            {
                var stack = new List<byte>();
                do
                {
                    stack.Add((byte)(int)(v & 0x7f));
                    v >>= 7;
                } while (v > 0);
                for (int i = stack.Count - 1; i >= 0; i--)
                    output.Add((byte)(stack[i] | (i > 0 ? 0x80 : 0)));
            }
            Put(values[0] * 40 + values[1]);
            for (int i = 2; i < values.Count; i++)
                Put(values[i]);
            return output.ToArray();
        }

        // ---- strings ----

        /// <summary>
        /// ASN1_STRING_to_UTF8: the string's characters by its type (tag2nbyte: one byte per character
        /// for the 8-bit types, UCS-2 for BMPString, UCS-4 for UniversalString, UTF-8 as it is), as
        /// UTF-8; null for a type it refuses or bad content.
        /// </summary>
        internal static byte[] StringToUtf8(int tag, ReadOnlySpan<byte> content)
        {
            switch (tag)
            {
                case UTF8STRING:
                    {
                        // ASN1_mbstring_copy checks the UTF-8 (traverse_string with in_utf8).
                        int i = 0;
                        while (i < content.Length)
                        {
                            int n = Utf8Length(content.Slice(i));
                            if (n <= 0)
                                return null;
                            i += n;
                        }
                        return content.ToArray();
                    }
                case NUMERICSTRING:
                case PRINTABLESTRING:
                case T61STRING:
                case IA5STRING:
                case UTCTIME:
                case GENERALIZEDTIME:
                case VISIBLESTRING:
                    {
                        var output = new List<byte>(content.Length);
                        foreach (var b in content)
                            AppendUtf8(output, b);
                        return output.ToArray();
                    }
                case BMPSTRING:
                    {
                        if ((content.Length & 1) != 0)
                            return null;
                        var output = new List<byte>(content.Length);
                        for (int i = 0; i < content.Length; i += 2)
                            AppendUtf8(output, (content[i] << 8) | content[i + 1]);
                        return output.ToArray();
                    }
                case UNIVERSALSTRING:
                    {
                        if ((content.Length & 3) != 0)
                            return null;
                        var output = new List<byte>(content.Length);
                        for (int i = 0; i < content.Length; i += 4)
                        {
                            long c = ((long)content[i] << 24) | ((long)content[i + 1] << 16) | ((long)content[i + 2] << 8) | content[i + 3];
                            if (c > 0x10ffff)
                                return null;
                            AppendUtf8(output, (int)c);
                        }
                        return output.ToArray();
                    }
                default:
                    return null;
            }
        }

        /// <summary>UTF8_getc: the length of one well-formed character, or -1.</summary>
        private static int Utf8Length(ReadOnlySpan<byte> s)
        {
            int b = s[0];
            int n, min;
            if (b < 0x80)
                return 1;
            if ((b & 0xe0) == 0xc0) { n = 2; min = 0x80; }
            else if ((b & 0xf0) == 0xe0) { n = 3; min = 0x800; }
            else if ((b & 0xf8) == 0xf0) { n = 4; min = 0x10000; }
            else return -1;
            if (s.Length < n)
                return -1;
            int value = b & (0x7f >> n);
            for (int i = 1; i < n; i++)
            {
                if ((s[i] & 0xc0) != 0x80)
                    return -1;
                value = (value << 6) | (s[i] & 0x3f);
            }
            if (value < min)
                return -1;
            return n;
        }

        /// <summary>UTF8_putc</summary>
        internal static void AppendUtf8(List<byte> output, int c)
        {
            if (c < 0x80)
                output.Add((byte)c);
            else if (c < 0x800)
            {
                output.Add((byte)(0xc0 | (c >> 6)));
                output.Add((byte)(0x80 | (c & 0x3f)));
            }
            else if (c < 0x10000)
            {
                output.Add((byte)(0xe0 | (c >> 12)));
                output.Add((byte)(0x80 | ((c >> 6) & 0x3f)));
                output.Add((byte)(0x80 | (c & 0x3f)));
            }
            else
            {
                output.Add((byte)(0xf0 | (c >> 18)));
                output.Add((byte)(0x80 | ((c >> 12) & 0x3f)));
                output.Add((byte)(0x80 | ((c >> 6) & 0x3f)));
                output.Add((byte)(0x80 | (c & 0x3f)));
            }
        }

        // ---- INTEGER ----

        /// <summary>
        /// i2a_ASN1_INTEGER of an INTEGER's content octets: "-" for a negative one, the magnitude in
        /// upper-case hexadecimal, two digits a byte ("00" for zero).
        /// </summary>
        internal static string IntegerHex(ReadOnlySpan<byte> content)
        {
            if (content.Length == 0)
                return "00";
            bool negative = (content[0] & 0x80) != 0;
            byte[] magnitude;
            if (negative)
            {
                // two's complement: the magnitude is -value (c2i_ASN1_INTEGER)
                magnitude = content.ToArray();
                int carry = 1;
                for (int i = magnitude.Length - 1; i >= 0; i--)
                {
                    int v = (~magnitude[i] & 0xff) + carry;
                    magnitude[i] = (byte)v;
                    carry = v >> 8;
                }
            }
            else
                magnitude = content.ToArray();
            int start = 0;
            while (start < magnitude.Length - 1 && magnitude[start] == 0)
                start++;
            if (magnitude.Length - start == 1 && magnitude[start] == 0)
                return "00";
            var text = new StringBuilder();
            if (negative)
                text.Append('-');
            for (int i = start; i < magnitude.Length; i++)
            {
                if (i > start && (i - start) % 35 == 0)
                    text.Append("\\\n");
                text.Append(magnitude[i].ToString("X2"));
            }
            return text.ToString();
        }

        // ---- time ----

        private static readonly string[] Months =
            { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        /// <summary>The parts of an ASN1_TIME (ossl_asn1_time_to_tm), or null when it is malformed.</summary>
        internal static (int Year, int Month, int Day, int Hour, int Minute, int Second, string Fraction, bool Gmt)? ParseTime(int tag, ReadOnlySpan<byte> content)
        {
            string s = Encoding.Latin1.GetString(content);
            int pos = 0;
            int Digits(int n)
            {
                if (pos + n > s.Length)
                    throw new FormatException();
                int v = 0;
                for (int i = 0; i < n; i++)
                {
                    char c = s[pos + i];
                    if (c < '0' || c > '9')
                        throw new FormatException();
                    v = v * 10 + (c - '0');
                }
                pos += n;
                return v;
            }
            try
            {
                int year;
                if (tag == UTCTIME)
                {
                    year = Digits(2);
                    year += year < 50 ? 2000 : 1900;
                }
                else if (tag == GENERALIZEDTIME)
                    year = Digits(4);
                else
                    return null;
                int month = Digits(2), day = Digits(2), hour = Digits(2), minute = Digits(2);
                int second = 0;
                if (pos < s.Length && s[pos] >= '0' && s[pos] <= '9')
                    second = Digits(2);
                string fraction = "";
                if (tag == GENERALIZEDTIME && pos < s.Length && s[pos] == '.')
                {
                    int f = pos;
                    pos++;
                    while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9')
                        pos++;
                    if (pos == f + 1)
                        return null;
                    fraction = s.Substring(f, pos - f);
                }
                bool gmt = pos < s.Length && s[pos] == 'Z';
                if (month < 1 || month > 12 || day < 1 || day > 31 || hour > 23 || minute > 59 || second > 60)
                    return null;
                return (year, month, day, hour, minute, second, fraction, gmt);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>ASN1_TIME_print: "Mon dd hh:mm:ss[.fff] yyyy GMT", or "Bad time value".</summary>
        internal static string TimePrint(int tag, ReadOnlySpan<byte> content)
        {
            var t = ParseTime(tag, content);
            if (t == null)
                return "Bad time value";
            var v = t.Value;
            return $"{Months[v.Month - 1]} {v.Day,2} {v.Hour:00}:{v.Minute:00}:{v.Second:00}{v.Fraction} {v.Year}{(v.Gmt ? " GMT" : "")}";
        }

        /// <summary>The time as UTC (ASN1_TIME_to_tm), for X509_cmp_time.</summary>
        internal static DateTime? TimeValue(int tag, ReadOnlySpan<byte> content)
        {
            var t = ParseTime(tag, content);
            if (t == null)
                return null;
            var v = t.Value;
            try
            {
                return new DateTime(v.Year, v.Month, v.Day, v.Hour, v.Minute, Math.Min(v.Second, 59), DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        // ---- DER writing (i2d) for the canonical name encoding ----

        internal static byte[] Encode(int tagByte, ReadOnlySpan<byte> content)
        {
            var output = new List<byte>(content.Length + 6) { (byte)tagByte };
            int n = content.Length;
            if (n < 0x80)
                output.Add((byte)n);
            else
            {
                var len = new List<byte>();
                while (n > 0)
                {
                    len.Insert(0, (byte)n);
                    n >>= 8;
                }
                output.Add((byte)(0x80 | len.Count));
                output.AddRange(len);
            }
            output.AddRange(content.ToArray());
            return output.ToArray();
        }
    }
}
