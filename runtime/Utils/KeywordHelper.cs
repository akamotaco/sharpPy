using System.Collections.Generic;

namespace SharpPy.Utils
{
    /// <summary>
    /// Helper for keyword checking - extracted from compile.cs to avoid duplication
    /// CPython 3.12 keywords
    /// </summary>
    public static class KeywordHelper
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "False", "None", "True", "and", "as", "assert", "async", "await",
            "break", "class", "continue", "def", "del", "elif", "else", "except",
            "finally", "for", "from", "global", "if", "import", "in", "is",
            "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try",
            "while", "with", "yield", "match", "case"
        };

        public static bool IsKeywordLexeme(string name) => Keywords.Contains(name);
    }
}