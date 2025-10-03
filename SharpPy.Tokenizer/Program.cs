using System;
using System.IO;

namespace SharpPy.Tokenizer
{
    class Program
    {
        static int Main(string[] args)
        {
            // Parse command line arguments
            string tokensFilePath = "Grammar/Tokens";
            string outputPath = "Generated/PyTokenizer.cs";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--tokens" && i + 1 < args.Length)
                {
                    tokensFilePath = args[i + 1];
                    i++;
                }
                else if (args[i] == "--output" && i + 1 < args.Length)
                {
                    outputPath = args[i + 1];
                    i++;
                }
            }

            try
            {
                Console.WriteLine($"[TOKENIZER] Reading tokens from {tokensFilePath}");
                var tokens = TokensReader.ReadTokens(tokensFilePath);
                Console.WriteLine($"[TOKENIZER] Loaded {tokens.Count} tokens");

                Console.WriteLine($"[TOKENIZER] Generating tokenizer to {outputPath}");
                var generator = new CSharpTokenizerGenerator(tokens);
                var code = generator.GenerateTokenizer();

                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                File.WriteAllText(outputPath, code);
                Console.WriteLine($"[TOKENIZER] Successfully generated {outputPath}");

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TOKENIZER] ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }
    }
}
