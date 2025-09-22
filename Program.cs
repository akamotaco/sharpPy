using System;
using SharpPy.Core;
using SharpPy.Tools;
using SharpPy.Modules;

namespace SharpPy
{
    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                // 명령줄 옵션 파싱
                var parser = new CommandLineParser();
                var (parsedArgs, pythonFile) = parser.Parse(args);

                // 플래그 처리
                ConfigureOptions(parsedArgs);

                // PEG parser 활성화 처리
                if (parsedArgs.ContainsKey("--use-peg-parser"))
                {
                    PyParserBridge.SetPegParserEnabled(true);
                    Console.WriteLine("🔄 PEG parser enabled");
                }

                // 각 모드로 위임
                if (parsedArgs.ContainsKey("--dis"))
                {
                    new BytecodeDisassembler().RunDirectDisassembly(pythonFile);
                }
                else if (parsedArgs.ContainsKey("--tokens"))
                {
                    new TokenDebugger().OutputTokens(pythonFile);
                }
                else if (parsedArgs.ContainsKey("--compare-parsers"))
                {
                    RunParserComparison(pythonFile);
                }
                else if (parsedArgs.ContainsKey("-c"))
                {
                    new FileExecutor().ExecuteCodeString(parsedArgs["-c"]);
                }
                else if (parsedArgs.ContainsKey("-m"))
                {
                    SharpPyConfig.ShowBytecode = parsedArgs["-m"] == "dis";
                    new ModuleRunner().RunModule(args);
                }
                else if (!string.IsNullOrEmpty(pythonFile))
                {
                    new FileExecutor().ExecuteFile(pythonFile);
                }
                else if (parsedArgs.ContainsKey("command"))
                {
                    HandleSpecialCommands(parsedArgs["command"]);
                }
                else if (parsedArgs.ContainsKey("help"))
                {
                    new HelpDisplay().ShowHelp();
                }
                else
                {
                    // 기본 모드: REPL 실행
                    new SharpPyRepl().Start();
                }
            }
            finally
            {
                // CPython 3.12 compatibility: Check for unawaited coroutines before exit
                CoroutineTracker.CheckForUnawaitedCoroutines();
            }
        }


        /// <summary>
        /// 설정 옵션들을 구성
        /// </summary>
        private static void ConfigureOptions(System.Collections.Generic.Dictionary<string, string> parsedArgs)
        {
            if (parsedArgs.ContainsKey("--no-optimize"))
            {
                SharpPyConfig.DisableOptimizer = true;
            }
        }

        /// <summary>
        /// 파서 비교 실행
        /// </summary>
        private static void RunParserComparison(string pythonFile)
        {
            Console.WriteLine("🔍 Parser Comparison Mode");
            Console.WriteLine(new string('=', 60));

            if (string.IsNullOrEmpty(pythonFile))
            {
                // 기본 테스트 파일들 비교
                var testFiles = new[]
                {
                    "test_parser_comparison_simple.py",
                    "test_parser_comparison_complex.py",
                    "test_run_parser_comparison.py"
                };

                ParserComparisonTool.RunBatchComparison(testFiles);
            }
            else
            {
                // 지정된 파일 비교
                var result = ParserComparisonTool.CompareFile(pythonFile);
                Console.WriteLine($"\n🎯 Comparison complete for {pythonFile}");

                if (result.ASTMatches)
                {
                    Console.WriteLine("✅ Parsers produce identical ASTs!");
                }
                else if (result.BothSucceeded)
                {
                    Console.WriteLine($"⚠️  Parsers succeeded but ASTs differ ({result.Differences.Count} differences)");
                }
                else
                {
                    Console.WriteLine("❌ One or both parsers failed");
                }
            }
        }

        /// <summary>
        /// 특별한 명령어들 처리 (demo, test-iteration 등)
        /// </summary>
        private static void HandleSpecialCommands(string command)
        {
            var demoRunner = new DemoRunner();
            
            switch (command)
            {
                case "demo":
                    demoRunner.RunAllDemos();
                    break;
                    
                case "test-iteration":
                    demoRunner.RunIterationTest();
                    break;
                    
                case "test-try-except":
                    demoRunner.RunTryExceptTest();
                    break;
                    
                default:
                    Console.WriteLine($"❌ 알 수 없는 명령어: {command}");
                    new HelpDisplay().ShowHelp();
                    break;
            }
        }
    }
}
