using SharpPy.PegGenerator.Readers;
using SharpPy.PegGenerator.Extractors;

namespace SharpPy.PegGenerator;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine("   SharpPy NEW PEG Parser Generator");
            Console.WriteLine("   python_cs.gram → PyParser.cs + AstTypes.cs");
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine();

            // Default paths
            var grammarPath = Path.Combine("..", "Grammar", "python_cs.gram");
            var asdlPath = Path.Combine("..", "Grammar", "Python.asdl");
            var outputAst = Path.Combine("..", "Generated", "AstTypes.cs");
            var outputParser = Path.Combine("..", "Generated", "PyParser.cs");

            // Parse command line arguments
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--grammar":
                        if (i + 1 < args.Length)
                            grammarPath = args[++i];
                        break;
                    case "--asdl":
                        if (i + 1 < args.Length)
                            asdlPath = args[++i];
                        break;
                    case "--output-ast":
                        if (i + 1 < args.Length)
                            outputAst = args[++i];
                        break;
                    case "--output-parser":
                        if (i + 1 < args.Length)
                            outputParser = args[++i];
                        break;
                    case "--help":
                        ShowHelp();
                        return;
                }
            }

            // Validate input files
            if (!File.Exists(grammarPath))
            {
                Console.WriteLine($"❌ Error: Grammar file not found: {grammarPath}");
                return;
            }

            if (!File.Exists(asdlPath))
            {
                Console.WriteLine($"❌ Error: ASDL file not found: {asdlPath}");
                return;
            }

            Console.WriteLine($"Input files:");
            Console.WriteLine($"  Grammar: {grammarPath}");
            Console.WriteLine($"  ASDL:    {asdlPath}");
            Console.WriteLine($"Output files:");
            Console.WriteLine($"  AST:     {outputAst}");
            Console.WriteLine($"  Parser:  {outputParser}");
            Console.WriteLine();

            // ============================================================
            // Phase 1: Read python_cs.gram
            // ============================================================
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine("Phase 1: Reading python_cs.gram (Token-based)");
            Console.WriteLine("═══════════════════════════════════════════════════════");
            var grammarReader = new GrammarReaderV2();
            var pegRules = grammarReader.ReadGrammar(grammarPath);
            Console.WriteLine($"✓ Parsed {pegRules.Length} grammar rules");
            Console.WriteLine();

            // ============================================================
            // Phase 2: Extract and validate keywords
            // ============================================================
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine("Phase 2: Extracting keywords");
            Console.WriteLine("═══════════════════════════════════════════════════════");
            var keywordExtractor = new KeywordExtractor();
            var (hardKeywords, softKeywords) = keywordExtractor.ExtractKeywords(pegRules);

            Console.WriteLine($"✓ Extracted HARD keywords ({hardKeywords.Count}):");
            Console.WriteLine($"  {string.Join(", ", hardKeywords.OrderBy(k => k))}");
            Console.WriteLine($"✓ Extracted SOFT keywords ({softKeywords.Count}):");
            Console.WriteLine($"  {string.Join(", ", softKeywords.OrderBy(k => k))}");
            Console.WriteLine();

            // ============================================================
            // Phase 3: Read Python.asdl
            // ============================================================
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine("Phase 3: Reading Python.asdl");
            Console.WriteLine("═══════════════════════════════════════════════════════");
            var asdlReader = new AsdlReader();
            var asdlModule = asdlReader.ReadAsdl(asdlPath);
            Console.WriteLine($"✓ Module: {asdlModule.Name}");
            Console.WriteLine($"✓ Types: {asdlModule.Types.Count}");
            Console.WriteLine();

            // ============================================================
            // Phase 4: Generate AstTypes.cs
            // ============================================================
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine("Phase 4: Generating AstTypes.cs");
            Console.WriteLine("═══════════════════════════════════════════════════════");
            var astGenerator = new Generators.AstGenerator();
            var astCode = astGenerator.Generate(asdlModule);

            // Ensure output directory exists
            var astOutputDir = Path.GetDirectoryName(outputAst);
            if (!string.IsNullOrEmpty(astOutputDir) && !Directory.Exists(astOutputDir))
            {
                Directory.CreateDirectory(astOutputDir);
            }

            File.WriteAllText(outputAst, astCode);
            Console.WriteLine($"✓ Generated {outputAst}");
            Console.WriteLine($"  Lines: {astCode.Split('\n').Length}");
            Console.WriteLine();

            // ============================================================
            // Phase 5: Generate PyParser.cs
            // ============================================================
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine("Phase 5: Generating PyParser.cs");
            Console.WriteLine("═══════════════════════════════════════════════════════");
            var parserGenerator = new Generators.ParserGenerator();
            var parserCode = parserGenerator.Generate(pegRules, hardKeywords, softKeywords, grammarReader.Trailer);

            // Ensure output directory exists
            var parserOutputDir = Path.GetDirectoryName(outputParser);
            if (!string.IsNullOrEmpty(parserOutputDir) && !Directory.Exists(parserOutputDir))
            {
                Directory.CreateDirectory(parserOutputDir);
            }

            File.WriteAllText(outputParser, parserCode);
            Console.WriteLine($"✓ Generated {outputParser}");
            Console.WriteLine($"  Lines: {parserCode.Split('\n').Length}");
            Console.WriteLine($"  Rules: {pegRules.Length}");
            Console.WriteLine();

            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine("✓ Successfully completed ALL Phases (1-5)!");
            Console.WriteLine($"  Grammar rules parsed: {pegRules.Length}");
            Console.WriteLine($"  Keywords extracted: {hardKeywords.Count} HARD + {softKeywords.Count} SOFT");
            Console.WriteLine($"  AST types generated: {asdlModule.Types.Count} types");
            Console.WriteLine($"  Generated files:");
            Console.WriteLine($"    - {outputAst} ({astCode.Split('\n').Length} lines)");
            Console.WriteLine($"    - {outputParser} ({parserCode.Split('\n').Length} lines)");
            Console.WriteLine("═══════════════════════════════════════════════════════");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine($"❌ Error: {ex.Message}");
            Console.WriteLine("═══════════════════════════════════════════════════════");
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
        Console.WriteLine("  --grammar <path>       Path to python_cs.gram (default: ../Grammar/python_cs.gram)");
        Console.WriteLine("  --asdl <path>          Path to Python.asdl (default: ../Grammar/Python.asdl)");
        Console.WriteLine("  --output-ast <path>    AST output path (default: ../Generated/AstTypes.cs)");
        Console.WriteLine("  --output-parser <path> Parser output path (default: ../Generated/PyParser.cs)");
        Console.WriteLine("  --help                 Show this help message");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  SharpPy.PegGenerator");
        Console.WriteLine("  SharpPy.PegGenerator --grammar custom.gram --output-parser parser.cs");
        Console.WriteLine();
    }
}
