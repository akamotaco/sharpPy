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
        /// <summary>
        /// Main parsing entry point - uses auto-generated CPython 3.12 compatible tokenizer and parser
        /// </summary>
        public static List<GeneratedTokenInfo> LexerSource(string source, bool generateExtraTokens = false)
        {
#if DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] PyParserRuntime.LexerSource START");
            Console.WriteLine($"[DEBUG] GeneratedParserBridge: Using auto-generated CPython 3.12 tokenizer + parser");
#endif

#if DEBUG_AST_LOG
            Console.WriteLine("[DEBUG] Creating tokenizer...");
#endif
            // Use generated tokenizer
            // CPython 3.12: generateExtraTokens = true for tokenize module (--tokens flag)
            // CPython 3.12: generateExtraTokens = false for parser (default)
            var tokenizer = new Tokenizer(source, generateExtraTokens);
#if DEBUG_AST_LOG
            Console.WriteLine("[DEBUG] Calling tokenizer.Tokenize()...");
#endif
            var generatedTokens = tokenizer.Tokenize();
#if DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] Tokenize returned {generatedTokens.Count} tokens");
#endif
            return generatedTokens;
        }
        
        public static GeneratedMod ParseToAsdl(List<GeneratedTokenInfo> generatedTokens, string source, string filename) =>
            new PyParser(generatedTokens, filename, source).ParseFile();
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