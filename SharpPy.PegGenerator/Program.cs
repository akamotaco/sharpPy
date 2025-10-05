using System;
using System.IO;
using SharpPy.PegGenerator.Grammar;
using SharpPy.PegGenerator.CodeGenerator;
using SharpPy.PegGenerator.Asdl;

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
                var grammarPath = Path.Combine("..", "Grammar", "python_cs.gram");
                var tokensPath = Path.Combine("..", "Grammar", "Tokens");
                var asdlPath = Path.Combine("..", "Grammar", "Python.asdl");
                var parserOutputPath = Path.Combine("..", "Generated", "PyParser.cs");
                var astTypesOutputPath = Path.Combine("..", "Generated", "GeneratedAstTypes.cs");
                var parserBaseOutputPath = Path.Combine("..", "Generated", "PyParserBase.cs");

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
                        case "--asdl":
                            if (i + 1 < args.Length)
                                asdlPath = args[++i];
                            break;
                        case "--ast-output":
                            if (i + 1 < args.Length)
                                astTypesOutputPath = args[++i];
                            break;
                        case "--parser-base-output":
                            if (i + 1 < args.Length)
                                parserBaseOutputPath = args[++i];
                            break;
                        case "--test-simple":
                            // Test with simple grammar
                            grammarPath = Path.Combine("..", "Grammar", "test_simple.gram");
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

                if (!File.Exists(asdlPath))
                {
                    Console.WriteLine($"Error: ASDL file not found: {asdlPath}");
                    return;
                }

                Console.WriteLine($"Reading ASDL from: {asdlPath}");
                Console.WriteLine($"Reading grammar from: {grammarPath}");
                Console.WriteLine($"Reading tokens from: {tokensPath}");
                Console.WriteLine($"AST types output will be written to: {astTypesOutputPath}");
                Console.WriteLine($"Parser base output will be written to: {parserBaseOutputPath}");
                Console.WriteLine($"Parser output will be written to: {parserOutputPath}");
                Console.WriteLine();

                // ============================================================
                // Phase 1: Generate AST Types from ASDL (CPython: asdl_c.py)
                // ============================================================
                Console.WriteLine("=== Phase 1: Generating AST Types from ASDL ===");
                Console.WriteLine("Reading ASDL...");
                var asdlContent = File.ReadAllText(asdlPath);

                Console.WriteLine("Parsing ASDL...");
                var asdlParser = new AsdlParser();
                var asdlModule = asdlParser.Parse(asdlContent);
                Console.WriteLine($"Parsed ASDL module '{asdlModule.Name}' with {asdlModule.Types.Count} types");

                Console.WriteLine("Generating C# AST types...");
                var astCodeGenerator = new AsdlCodeGenerator();
                var generatedAstCode = astCodeGenerator.GenerateAstTypes(asdlModule);

                // Ensure output directory exists
                var astOutputDir = Path.GetDirectoryName(astTypesOutputPath);
                if (!string.IsNullOrEmpty(astOutputDir) && !Directory.Exists(astOutputDir))
                {
                    Directory.CreateDirectory(astOutputDir);
                    Console.WriteLine($"Created AST output directory: {astOutputDir}");
                }

                // Write AST types output
                File.WriteAllText(astTypesOutputPath, generatedAstCode);
                Console.WriteLine($"Generated AST types written to: {astTypesOutputPath}");
                Console.WriteLine($"Generated AST code size: {generatedAstCode.Length} characters");
                Console.WriteLine();

                // ============================================================
                // Phase 1.5: Generate PyParserBase from ASDL (CPython: Python-ast.c)
                // ============================================================
                Console.WriteLine("=== Phase 1.5: Generating PyParserBase from ASDL ===");
                Console.WriteLine("Generating PyParserBase with _PyAST_* helpers...");
                var parserBaseGenerator = new PyParserBaseGenerator();
                var generatedParserBase = parserBaseGenerator.GenerateParserBase(asdlModule);

                // Ensure output directory exists for PyParserBase
                var parserBaseOutputDir = Path.GetDirectoryName(parserBaseOutputPath);
                if (!string.IsNullOrEmpty(parserBaseOutputDir) && !Directory.Exists(parserBaseOutputDir))
                {
                    Directory.CreateDirectory(parserBaseOutputDir);
                    Console.WriteLine($"Created parser base output directory: {parserBaseOutputDir}");
                }

                // Write PyParserBase output
                File.WriteAllText(parserBaseOutputPath, generatedParserBase);
                Console.WriteLine($"Generated PyParserBase written to: {parserBaseOutputPath}");
                Console.WriteLine($"Generated PyParserBase code size: {generatedParserBase.Length} characters");
                Console.WriteLine();

                // ============================================================
                // Phase 2: Generate Parser from Grammar (CPython: pegen.c)
                // ============================================================
                Console.WriteLine("=== Phase 2: Generating Parser from Grammar ===");

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

                // Ensure output directory exists
                var parserOutputDir = Path.GetDirectoryName(parserOutputPath);
                if (!string.IsNullOrEmpty(parserOutputDir) && !Directory.Exists(parserOutputDir))
                {
                    Directory.CreateDirectory(parserOutputDir);
                    Console.WriteLine($"Created parser output directory: {parserOutputDir}");
                }

                // Write output
                File.WriteAllText(parserOutputPath, generatedParserCode);
                Console.WriteLine($"Generated parser written to: {parserOutputPath}");
                Console.WriteLine($"Generated parser code size: {generatedParserCode.Length} characters");

                Console.WriteLine();
                Console.WriteLine("✅ AST types and parser generation completed successfully!");
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
            Console.WriteLine("  --asdl <path>           Path to Python.asdl file (default: ../Grammar/Python.asdl)");
            Console.WriteLine("  --grammar <path>        Path to python.gram file (default: ../Grammar/python.gram)");
            Console.WriteLine("  --tokens <path>         Path to Tokens file (default: ../Grammar/Tokens)");
            Console.WriteLine("  --ast-output <path>     AST types output C# file path (default: ../Generated/GeneratedAstTypes.cs)");
            Console.WriteLine("  --parser-output <path>  Parser output C# file path (default: ../Generated/PyParser.cs)");
            Console.WriteLine("  --help                  Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  SharpPy.PegGenerator");
            Console.WriteLine("  SharpPy.PegGenerator --grammar custom.gram --parser-output parser.cs");
            Console.WriteLine();
        }
    }
}
