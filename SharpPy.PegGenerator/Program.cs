using System;
using System.IO;
using SharpPy.PegGenerator.Grammar;
using SharpPy.PegGenerator.CodeGenerator;

namespace SharpPy.PegGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("SharpPy PEG Parser Generator");
                Console.WriteLine("===========================");

                // Default paths relative to the main SharpPy project
                var grammarPath = Path.Combine("..", "Grammar", "python.gram");
                var tokensPath = Path.Combine("..", "Grammar", "Tokens");
                var parserOutputPath = Path.Combine("..", "runtime", "Generated", "PyParser.cs");
                var tokenizerOutputPath = Path.Combine("..", "runtime", "Generated", "PyTokenizer.cs");

                // Parse command line arguments
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--grammar":
                            if (i + 1 < args.Length)
                                grammarPath = args[++i];
                            break;
                        case "--tokens":
                            if (i + 1 < args.Length)
                                tokensPath = args[++i];
                            break;
                        case "--parser-output":
                            if (i + 1 < args.Length)
                                parserOutputPath = args[++i];
                            break;
                        case "--tokenizer-output":
                            if (i + 1 < args.Length)
                                tokenizerOutputPath = args[++i];
                            break;
                        case "--help":
                            ShowHelp();
                            return;
                    }
                }

                // Validate input files
                if (!File.Exists(grammarPath))
                {
                    Console.WriteLine($"Error: Grammar file not found: {grammarPath}");
                    return;
                }

                if (!File.Exists(tokensPath))
                {
                    Console.WriteLine($"Error: Tokens file not found: {tokensPath}");
                    return;
                }

                Console.WriteLine($"Reading grammar from: {grammarPath}");
                Console.WriteLine($"Reading tokens from: {tokensPath}");
                Console.WriteLine($"Parser output will be written to: {parserOutputPath}");
                Console.WriteLine($"Tokenizer output will be written to: {tokenizerOutputPath}");
                Console.WriteLine();

                // Read and parse tokens
                Console.WriteLine("Reading tokens...");
                var tokens = TokensReader.ReadTokens(tokensPath);
                Console.WriteLine($"Found {tokens.Count} tokens");

                // Read and parse grammar
                Console.WriteLine("Reading grammar...");
                var grammarContent = File.ReadAllText(grammarPath);

                Console.WriteLine("Tokenizing grammar...");
                var lexer = new GrammarLexer(grammarContent);
                var grammarTokens = lexer.Tokenize();
                Console.WriteLine($"Generated {grammarTokens.Count} grammar tokens");

                Console.WriteLine("Parsing grammar...");
                var parser = new GrammarParser(grammarTokens);
                var grammar = parser.ParseGrammar();
                Console.WriteLine($"Parsed {grammar.Rules.Count} rules");

                // Generate C# parser code
                Console.WriteLine("Generating C# parser code...");
                var parserGenerator = new CSharpCodeGenerator(grammar, tokens);
                var generatedParserCode = parserGenerator.GenerateParser();

                // Generate C# tokenizer code
                Console.WriteLine("Generating C# tokenizer code...");
                var tokenizerGenerator = new CSharpTokenizerGenerator(tokens);
                var generatedTokenizerCode = tokenizerGenerator.GenerateTokenizer();

                // Ensure output directories exist
                var parserOutputDir = Path.GetDirectoryName(parserOutputPath);
                if (!string.IsNullOrEmpty(parserOutputDir) && !Directory.Exists(parserOutputDir))
                {
                    Directory.CreateDirectory(parserOutputDir);
                    Console.WriteLine($"Created parser output directory: {parserOutputDir}");
                }

                var tokenizerOutputDir = Path.GetDirectoryName(tokenizerOutputPath);
                if (!string.IsNullOrEmpty(tokenizerOutputDir) && !Directory.Exists(tokenizerOutputDir))
                {
                    Directory.CreateDirectory(tokenizerOutputDir);
                    Console.WriteLine($"Created tokenizer output directory: {tokenizerOutputDir}");
                }

                // Write outputs
                File.WriteAllText(parserOutputPath, generatedParserCode);
                Console.WriteLine($"Generated parser written to: {parserOutputPath}");
                Console.WriteLine($"Generated parser code size: {generatedParserCode.Length} characters");

                File.WriteAllText(tokenizerOutputPath, generatedTokenizerCode);
                Console.WriteLine($"Generated tokenizer written to: {tokenizerOutputPath}");
                Console.WriteLine($"Generated tokenizer code size: {generatedTokenizerCode.Length} characters");

                Console.WriteLine();
                Console.WriteLine("✅ Parser and tokenizer generation completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Stack trace:");
                Console.WriteLine(ex.StackTrace);
                Environment.Exit(1);
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: SharpPy.PegGenerator [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --grammar <path>           Path to python.gram file (default: ../Grammar/python.gram)");
            Console.WriteLine("  --tokens <path>            Path to Tokens file (default: ../Grammar/Tokens)");
            Console.WriteLine("  --parser-output <path>     Parser output C# file path (default: ../runtime/Generated/PyParser.cs)");
            Console.WriteLine("  --tokenizer-output <path>  Tokenizer output C# file path (default: ../runtime/Generated/PyTokenizer.cs)");
            Console.WriteLine("  --help                     Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  SharpPy.PegGenerator");
            Console.WriteLine("  SharpPy.PegGenerator --grammar custom.gram --parser-output parser.cs");
            Console.WriteLine();
        }
    }
}
