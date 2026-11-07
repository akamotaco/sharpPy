using System.Globalization;

namespace SharpPy;

// CPython 3.12 Python/formatter_unicode.c: parse, convert, then group/pad.
// CLR supplies the correctly rounded binary64-to-decimal conversion.
internal static class PyFloatFormatter
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    internal static string Format(double value, string spec)
    {
        spec ??= "";
        var format = FormatSpec.Parse("float", spec, '\0', '>');
        if (!(format.Type is '\0' or 'e' or 'E' or 'f' or 'F' or 'g' or 'G' or 'n' or '%'))
            throw FormatSpec.Unknown(format.Type, "float");
        return Render(value, format);
    }

    /// <summary>format_float_internal, the number and its padding.</summary>
    internal static string Render(double value, FormatSpec format)
    {
        if (format.Precision > int.MaxValue) throw PyValueError.Create("precision too big");
        PyAllocation.StringLength(format.Width);
        string fill = format.Fill;
        char align = format.Align, sign = format.Sign == '\0' ? '-' : format.Sign, group = format.Thousands;
        char type = format.TypeChar;
        bool alternate = format.Alternate, zero = format.NoNegativeZero;
        int width = (int)System.Math.Max(0, format.Width), precision = (int)format.Precision;
        if (type == '%') value *= 100;
        bool negative = !double.IsNaN(value) && BitConverter.DoubleToInt64Bits(value) < 0;
        bool special = !double.IsFinite(value);
        string number;
        if (special)
        {
            bool upper = type == 'E' || type == 'F' || type == 'G';
            number = double.IsNaN(value) ? (upper ? "NAN" : "nan") : (upper ? "INF" : "inf");
        }
        else
        {
            number = DoubleToString(Math.Abs(value), type, precision, alternate);
            if (zero && negative)
            {
                bool nonzero = false;
                foreach (char c in number)
                {
                    if (c == 'e' || c == 'E') break;
                    if (c >= '1' && c <= '9') { nonzero = true; break; }
                }
                if (!nonzero) negative = false;
            }
        }
        if (type == '%') number += "%";
        string prefix = negative ? "-" : sign == '+' ? "+" : sign == ' ' ? " " : "";
        if (!special && group != '\0')
        {
            int count = 0;
            while (count < number.Length && char.IsAsciiDigit(number[count])) count++;
            int digits = count;
            if (fill == "0" && align == '=')
            {
                int wanted = Math.Max(0, width - prefix.Length - (number.Length - count));
                digits = Math.Max(digits, wanted - Math.Max(0, wanted - 1) / 4);
            }
            int groupedLength = checked(digits + (digits - 1) / 3);
            number = string.Create(groupedLength + number.Length - count,
                (number, count, groupedLength, group), static (output, state) => {
                    state.number.AsSpan(state.count).CopyTo(output.Slice(state.groupedLength));
                    for (int dst = state.groupedLength - 1, src = state.count - 1, n = 0; dst >= 0; n++)
                    {
                        if (n != 0 && n % 3 == 0) output[dst--] = state.group;
                        output[dst--] = src >= 0 ? state.number[src--] : '0';
                    }
                });
        }
        int padding = Math.Max(0, width - prefix.Length - number.Length);
        if (padding == 0) return prefix + number;
        return string.Create(checked(prefix.Length + number.Length + padding * fill.Length),
            (prefix, number, padding, fill, align), static (output, state) => {
                int before = state.align == '>' ? state.padding : state.align == '^' ? state.padding / 2 : 0;
                int middle = state.align == '=' ? state.padding : 0;
                int pos = 0;
                for (int j = 0; j < before; j++) { state.fill.AsSpan().CopyTo(output.Slice(pos)); pos += state.fill.Length; }
                state.prefix.AsSpan().CopyTo(output.Slice(pos)); pos += state.prefix.Length;
                for (int j = 0; j < middle; j++) { state.fill.AsSpan().CopyTo(output.Slice(pos)); pos += state.fill.Length; }
                state.number.AsSpan().CopyTo(output.Slice(pos)); pos += state.number.Length;
                while (pos < output.Length) { state.fill.AsSpan().CopyTo(output.Slice(pos)); pos += state.fill.Length; }
            });
    }

    /// <summary>PyOS_double_to_string for a finite, non-negative value, the sign left to the caller.</summary>
    internal static string DoubleToString(double magnitude, char type, int precision, bool alternate)
    {
        string number;
        if (type == 'f' || type == 'F' || type == '%')
            number = magnitude.ToString("F" + (precision < 0 ? 6 : precision), Invariant);
        else if (type == 'e' || type == 'E')
            number = NormalizeExponent(magnitude.ToString(type + (precision < 0 ? 6 : precision).ToString(Invariant), Invariant));
        else
            number = General(magnitude, precision, type, alternate);
        if (alternate && !number.Contains('.'))
        {
            int exponent = ExponentIndex(number);
            number = exponent < 0 ? number + "." : number.Insert(exponent, ".");
        }
        return number;
    }

    private static int ExponentIndex(string number)
    {
        int index = number.IndexOf('E');
        return index < 0 ? number.IndexOf('e') : index;
    }

    private static string NormalizeExponent(string number)
    {
        int index = ExponentIndex(number);
        return index >= 0 && number.Length - index == 5 && number[index + 2] == '0'
            ? number.Remove(index + 2, 1) : number;
    }

    private static string General(double value, int precision, char type, bool alternate)
    {
        bool shortest = (type == '\0' || type == 'r') && precision < 0;
        int significant = precision < 0 ? 6 : Math.Max(1, precision);
        if (type != '\0' && type != 'r' && !alternate)
            return NormalizeExponent(value.ToString((type == 'G' ? "G" : "g") + significant, Invariant));
        string converted = shortest ? value.ToString("R", Invariant)
            : value.ToString("E" + (significant - 1), Invariant);
        int e = ExponentIndex(converted), point = converted.IndexOf('.');
        int end = e < 0 ? converted.Length : e;
        int exponent = e < 0 ? 0 : int.Parse(converted.AsSpan(e + 1), Invariant);
        int decimalPoint = point < 0 ? end : point;
        string digits = converted.Substring(0, end).Replace(".", "");
        int first = 0;
        while (value != 0 && first < digits.Length - 1 && digits[first] == '0') first++;
        exponent += decimalPoint - first - 1;
        digits = digits.Substring(first);
        if (!alternate || shortest) digits = digits.TrimEnd('0');
        if (digits.Length == 0) { digits = "0"; exponent = 0; }
        int upper = shortest ? 16 : type == '\0' || type == 'r' ? significant - 1 : significant;
        if (exponent < -4 || exponent >= upper)
        {
            string mantissa = digits.Length == 1 ? digits : digits.Insert(1, ".");
            return mantissa + (type == 'G' ? "E" : "e") + (exponent < 0 ? "-" : "+")
                + Math.Abs(exponent).ToString("D2", Invariant);
        }
        int position = exponent + 1;
        string result = position <= 0 ? "0." + new string('0', -position) + digits
            : position >= digits.Length ? digits + new string('0', position - digits.Length)
            : digits.Insert(position, ".");
        if (type == '\0' && !result.Contains('.')) result += ".0";
        return result;
    }
}
