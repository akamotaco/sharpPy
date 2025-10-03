using System;
using System.IO;

namespace SharpPy.PegGenerator.Grammar
{
    /// <summary>
    /// Loads and parses grammar files
    /// </summary>
    public class GrammarLoader
    {
        /// <summary>
        /// Load grammar from file path
        /// </summary>
        public Grammar LoadGrammar(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Grammar file not found: {filePath}");
            }

            var content = File.ReadAllText(filePath);

            // Tokenize the grammar
            var lexer = new GrammarLexer(content);
            var tokens = lexer.Tokenize();

            // Parse the grammar
            var parser = new GrammarParser(tokens);
            return parser.ParseGrammar();
        }
    }
}