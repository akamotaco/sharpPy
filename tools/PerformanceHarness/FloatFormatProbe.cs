using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SharpPy;

internal static class FloatFormatProbe
{
    internal static object Run(string path)
    {
        int cases = 0, mismatches = 0;
        var examples = new List<object>();
        foreach (string line in File.ReadLines(path))
        {
            using var record = JsonDocument.Parse(line);
            var item = record.RootElement;
            string bits = item.GetProperty("bits").GetString()!;
            string spec = item.GetProperty("spec").GetString()!;
            string expected = item.GetProperty("expected").GetString()!;
            double value = BitConverter.Int64BitsToDouble(unchecked((long)ulong.Parse(bits, NumberStyles.HexNumber)));
            string actual = PyFloat.FormatFloat(value, spec);
            cases++;
            if (actual == expected) continue;
            mismatches++;
            if (examples.Count < 20) examples.Add(new { bits, spec, expected, actual });
        }
        using var input = File.OpenRead(path);
        return new { cases, mismatches, examples,
            reference_sha256 = Convert.ToHexString(SHA256.HashData(input)) };
    }
}
