using System;
using System.Collections.Generic;
using System.Reflection;
using SharpPy.Generated;
// using SharpPy.Tokenizer.Generated; // Now using SharpPy.Generated
using SharpPy.Tools;

namespace SharpPy.Generated
{
    /// <summary>
    /// CPython 3.12 compatible generated parser bridge
    /// Uses auto-generated tokenizer and parser from Grammar/python.gram
    /// </summary>
    public static partial class PyParserRuntime
    {
        /// <summary>The tokens of a whole source, as the tokenize module sees them. For --tokens.</summary>
        public static List<GeneratedTokenInfo> LexerSource(string source, bool generateExtraTokens = false) =>
            new Tokenizer(source, extraTokens: generateExtraTokens).Tokenize();

        public static GeneratedMod ParseToAsdl(string source, string filename) =>
            new PyParser(new Tokenizer(source, filename: filename), filename, source).ParseFile();
    }

    public static class GeneratedParserConfig
    {
        /// <summary>
        /// Initialize generated parser for production use
        /// </summary>
        public static void Initialize()
        {
            Console.WriteLine("[INFO] Using auto-generated CPython 3.12 compatible parser and tokenizer");
            Console.WriteLine("[INFO] Generated from Grammar/python.gram and Grammar/Tokens");
        }
    }
}