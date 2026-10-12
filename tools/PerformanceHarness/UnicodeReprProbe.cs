using System.Security.Cryptography;
using SharpPy;

internal static class UnicodeReprProbe
{
    internal static object Run(string reference)
    {
        using var reader = new StreamReader(reference, System.Text.Encoding.UTF8);
        var examples = new List<object>();
        int mismatches = 0;
        for (int point = 0; point < 0x110000; point++)
        {
            string expected = reader.ReadLine() ?? throw new InvalidDataException("Truncated Unicode oracle");
            string value = point <= 0xffff ? new string((char)point, 1) : char.ConvertFromUtf32(point);
            string actual = new PyString(value).ToRepr().Value;
            if (actual != expected)
            {
                mismatches++;
                if (examples.Count < 32) examples.Add(new { point, expected, actual });
            }
        }
        if (reader.ReadLine() != null) throw new InvalidDataException("Extra Unicode oracle rows");
        using var source = File.OpenRead(reference);
        return new { cases = 0x110000, mismatches, examples,
            reference_sha256 = Convert.ToHexString(SHA256.HashData(source)),
            scope = "Native str repr for every code point including surrogates; excludes VM and multi-character combinations" };
    }
}
