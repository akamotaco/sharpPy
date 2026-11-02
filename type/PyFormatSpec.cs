using System.Collections.Generic;
using System.Globalization;

namespace SharpPy;

/// <summary>
/// InternalFormatSpec and parse_internal_render_format_spec (Python/formatter_unicode.c): the one
/// reading of a format specification, for str, int, float and complex alike.
/// </summary>
internal sealed class FormatSpec
{
    internal string Fill = " ";
    internal char Align;
    internal char Sign;
    internal bool NoNegativeZero;
    internal bool Alternate;
    internal long Width = -1;
    internal char Thousands;
    internal long Precision = -1;
    internal int Type;

    /// <summary>The type as a char, or a value no presentation uses.</summary>
    internal char TypeChar => Type >= 0 && Type <= char.MaxValue ? (char)Type : '￿';

    internal FormatSpec Copy() => (FormatSpec)MemberwiseClone();

    private static bool IsAlignment(int c) => c is '<' or '>' or '=' or '^';

    private static List<int> CodePoints(string text)
    {
        var points = new List<int>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsSurrogatePair(text, i))
            {
                points.Add(char.ConvertToUtf32(text, i));
                i++;
            }
            else
            {
                points.Add(text[i]);
            }
        }
        return points;
    }

    private static string Text(int point) =>
        point > char.MaxValue ? char.ConvertFromUtf32(point) : new string((char)point, 1);

    // get_integer: any decimal digit counts.
    private static long ReadInteger(List<int> spec, ref int position, out int consumed)
    {
        long value = 0;
        consumed = 0;
        for (; position < spec.Count; position++, consumed++)
        {
            int point = spec[position];
            int digit = CharUnicodeInfo.GetDecimalDigitValue(Text(point), 0);
            if (digit < 0)
                break;
            if (value > (long.MaxValue - digit) / 10)
                throw PyValueError.Create("Too many decimal digits in format string");
            value = value * 10 + digit;
        }
        return value;
    }

    private static string Presentation(int type) =>
        type > 32 && type < 128 ? ((char)type).ToString() : "\\x" + type.ToString("x");

    // unknown_presentation_type
    internal static System.Exception Unknown(int type, string typeName) =>
        PyValueError.Create($"Unknown format code '{Presentation(type)}' for object of type '{typeName}'");

    internal static FormatSpec Parse(string typeName, string text, char defaultType, char defaultAlign)
    {
        var spec = CodePoints(text);
        var format = new FormatSpec { Align = defaultAlign, Type = defaultType };
        int position = 0, end = spec.Count;
        bool alignSpecified = false, fillSpecified = false;

        if (end - position >= 2 && IsAlignment(spec[position + 1]))
        {
            format.Align = (char)spec[position + 1];
            format.Fill = Text(spec[position]);
            fillSpecified = alignSpecified = true;
            position += 2;
        }
        else if (end - position >= 1 && IsAlignment(spec[position]))
        {
            format.Align = (char)spec[position];
            alignSpecified = true;
            position++;
        }

        if (position < end && spec[position] is ' ' or '+' or '-' && position < end)
            format.Sign = (char)spec[position++];
        if (position < end && spec[position] == 'z')
        {
            format.NoNegativeZero = true;
            position++;
        }
        if (position < end && spec[position] == '#')
        {
            format.Alternate = true;
            position++;
        }
        if (!fillSpecified && position < end && spec[position] == '0')
        {
            format.Fill = "0";
            if (!alignSpecified && defaultAlign == '>')
                format.Align = '=';
            position++;
        }

        long width = ReadInteger(spec, ref position, out int consumed);
        format.Width = consumed == 0 ? -1 : width;

        if (position < end && spec[position] == ',')
        {
            format.Thousands = ',';
            position++;
        }
        if (position < end && spec[position] == '_')
        {
            if (format.Thousands != '\0')
                throw PyValueError.Create("Cannot specify both ',' and '_'.");
            format.Thousands = '_';
            position++;
        }
        if (position < end && spec[position] == ',' && format.Thousands == '_')
            throw PyValueError.Create("Cannot specify both ',' and '_'.");

        if (position < end && spec[position] == '.')
        {
            position++;
            format.Precision = ReadInteger(spec, ref position, out consumed);
            if (consumed == 0)
                throw PyValueError.Create("Format specifier missing precision");
        }

        if (end - position > 1)
            throw PyValueError.Create($"Invalid format specifier '{text}' for object of type '{typeName}'");
        if (end - position == 1)
            format.Type = spec[position];

        if (format.Thousands != '\0')
        {
            bool allowed = format.Type is 'd' or 'e' or 'f' or 'g' or 'E' or 'G' or '%' or 'F' or '\0' ||
                (format.Type is 'b' or 'o' or 'x' or 'X' && format.Thousands == '_');
            if (!allowed)
                throw PyValueError.Create($"Cannot specify '{format.Thousands}' with '{Presentation(format.Type)}'.");
        }
        return format;
    }
}
