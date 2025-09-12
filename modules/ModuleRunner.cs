using System;
using System.IO;

namespace SharpPy.Modules
{
    /// <summary>
    /// -m 옵션을 통한 모듈 실행을 전담하는 클래스
    /// </summary>
    public class ModuleRunner
    {
        /// <summary>
        /// -m 옵션으로 지정된 모듈을 실행
        /// </summary>
        /// <param name="args">전체 명령줄 인수 (모듈명 포함)</param>
        public void RunModule(string[] args)
        {
            // args: ["-m", "module_name", ...additional_args]
            if (args.Length < 2)
            {
                Console.WriteLine("사용법: dotnet run -m <module_name> [args...]");
                Environment.Exit(1);
                return;
            }

            string moduleName = args[1];
            string[] moduleArgs = args.Length > 2 ? args[2..] : new string[0];

            try
            {
                switch (moduleName)
                {
                    case "dis":
                        RunDisModule(moduleArgs);
                        break;
                        
                    default:
                        Console.WriteLine($"❌ 모듈을 찾을 수 없습니다: {moduleName}");
                        Console.WriteLine("지원되는 모듈:");
                        Console.WriteLine("  dis - 바이트코드 디스어셈블러");
                        Environment.Exit(1);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 모듈 실행 오류: {ex.Message}");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// dis 모듈 실행 (python -m dis 호환)
        /// </summary>
        /// <param name="args">dis 모듈에 전달할 인수</param>
        private void RunDisModule(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("사용법: dotnet run -m dis <python_file>");
                Console.WriteLine("예제: dotnet run -m dis test.py");
                Environment.Exit(1);
                return;
            }

            string pythonFile = args[0];
            
            if (!File.Exists(pythonFile))
            {
                Console.WriteLine($"❌ 파일을 찾을 수 없습니다: {pythonFile}");
                Environment.Exit(1);
                return;
            }

            // BytecodeDisassembler를 사용하여 디스어셈블리 실행
            var disassembler = new SharpPy.Tools.BytecodeDisassembler();
            disassembler.RunDirectDisassembly(pythonFile);
        }
    }
}