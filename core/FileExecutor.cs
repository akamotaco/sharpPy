using System;
using System.IO;

#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif

namespace SharpPy.Core
{
    /// <summary>
    /// Python 파일 및 코드 문자열 실행을 전담하는 클래스
    /// </summary>
    public class FileExecutor
    {
        /// <summary>
        /// Python 파일을 실행
        /// </summary>
        /// <param name="pythonFile">실행할 Python 파일 경로</param>
        /// <param name="showTokenize">토큰화 결과 출력 여부</param>
        /// <param name="showAst">AST 출력 여부</param>
        /// <param name="showBytecode">바이트코드 출력 여부</param>
        /// <param name="compileOnly">컴파일만 수행 (실행 안 함)</param>
        public void ExecuteFile(string pythonFile, bool showTokenize, bool showAst, bool showBytecode, bool compileOnly = false)
        {
            if (string.IsNullOrEmpty(pythonFile))
            {
                Console.WriteLine("❌ 파일 경로가 지정되지 않았습니다.");
                Environment.Exit(1);
                return;
            }

            // 기본 모드 (Python-style): 깔끔한 출력
#if DEBUG_LOG
            Console.WriteLine("🐍 SharpPy - Python Interpreter in C#");
            Console.WriteLine("=====================================\n");
            Console.WriteLine($"📄 Python 파일 {(compileOnly ? "컴파일" : "실행")}: {pythonFile}");
#endif

            try
            {
                // 파일 존재 여부 먼저 확인
                if (!IOHelper.FileExists(pythonFile))
                {
                    Console.WriteLine($"python: can't open file '{pythonFile}': [Errno 2] No such file or directory");
                    Environment.Exit(2);
                    return;
                }

                string code = IOHelper.ReadAllText(pythonFile);
                var interpreter = new IntegratedPythonInterpreter();

                if (compileOnly)
                {
                    // CPython 3.12: python -m py_compile file.py behavior
                    // Compile to bytecode but don't execute
                    interpreter.CompileOnly(code, pythonFile, showTokenize, showAst, showBytecode);
                }
                else
                {
                    interpreter.Execute(code, pythonFile, showTokenize, showAst, showBytecode);
                }
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"python: can't open file '{pythonFile}': [Errno 2] No such file or directory");
                Environment.Exit(2);
            }
            catch (DirectoryNotFoundException)
            {
                Console.WriteLine($"python: can't open file '{pythonFile}': [Errno 2] No such file or directory");
                Environment.Exit(2);
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"python: can't open file '{pythonFile}': [Errno 13] Permission denied");
                Environment.Exit(13);
            }
            catch (Exception ex)
            {
                // PyInterpreter already handles and prints Python-style errors
                // TEMPORARY: Always show C# stack trace for debugging
                Console.WriteLine("--- C# Exception Details ---");
                Console.WriteLine(ex.ToString());

                // Exit with non-zero code to indicate error
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// 코드 문자열을 직접 실행 (-c 옵션)
        /// </summary>
        /// <param name="codeString">실행할 Python 코드</param>
        public void ExecuteCodeString(string codeString)
        {
            if (string.IsNullOrEmpty(codeString))
            {
                Console.WriteLine("❌ 실행할 코드가 지정되지 않았습니다.");
                Environment.Exit(1);
                return;
            }

            try
            {
                var interpreter = new IntegratedPythonInterpreter();
                interpreter.Execute(codeString, "<string>", false, false, false);
            }
            catch (Exception ex)
            {
                // PyInterpreter already handles and prints Python-style errors
                // TEMPORARY: Always show C# stack trace for debugging
                Console.WriteLine("--- C# Exception Details ---");
                Console.WriteLine(ex.ToString());

                // Exit with non-zero code to indicate error
                Environment.Exit(1);
            }
        }
    }
}