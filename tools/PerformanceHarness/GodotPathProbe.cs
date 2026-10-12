#if GODOT
using System;

namespace SharpPy;

// Compile this source into the Godot validation project alongside SharpPy.
public static class GodotPathProbe
{
    public static int Run()
    {
        var cases = new (string[] Parts, string Expected)[] {
            (new[] { "res://", "Lib", "operator.py" }, "res://Lib/operator.py"),
            (new[] { "user://", "cache", "value.py" }, "user://cache/value.py"),
            (new[] { "res://Lib//", "operator.py" }, "res://Lib/operator.py"),
            (new[] { "C:/project", "Lib", "module.py" }, "C:/project/Lib/module.py"),
            (new[] { "res://", "GODOT_RES_TEMP.py" }, "res://GODOT_RES_TEMP.py"),
        };
        foreach (var test in cases)
            if (Godot_IO.Helper.CombinePath(test.Parts) != test.Expected)
                throw new InvalidOperationException("Godot resource path mismatch: " + test.Expected);
        return cases.Length;
    }
}
#endif
