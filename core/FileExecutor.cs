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
                    // The launched script's directory takes sys.path[0]; the
                    // embedding Execute API leaves its host's search path alone.
                    var sys = PyImportSystem.Import("sys");
                    if (sys.ModuleDict["path"] is PyList searchPath)
                    {
                        var directory = IOHelper.GetDirectoryName(IOHelper.GetFullPath(pythonFile));
                        if (searchPath.Length() == 0) searchPath.Append(new PyString(directory));
                        else searchPath.SetItem(new PyInt(0), new PyString(directory));
                    }
                    interpreter.Execute(code, pythonFile, showTokenize, showAst, showBytecode);
                }
                // CPython runs pending __del__ hooks while it tears the interpreter down, after the
                // program's own output and without changing the exit status. The order it follows is
                // visible in Python/pylifecycle.c: finalize_modules (:1573), delete_special (:1590),
                // _PyGC_CollectNoFail (:1609), finalize_modules_clear_weaklist (:1630), then a
                // second _PyGC_CollectNoFail (:1649). Shutdown is the backstop, not the mechanism:
                // objects dropped mid-run are delivered at the VM's safe points and by
                // gc.collect() (core/PyFinalizer.cs); what is left alive at the end is finalized
                // here, in module-namespace order - see docs/lifetime_and_gc_policy.md.
                PyFinalizer.Shutdown();
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
                // Measured: every uncaught Python exception printed a second block,
                // "--- C# Exception Details ---" followed by the CLR exception text, on stdout.
                // The interpreter has already reported a Python-level exception in CPython's own
                // format by the time control reaches here, so that dump was pure divergence (and
                // was marked TEMPORARY). A failure nobody reported is an interpreter bug: it still
                // exits 1, but the CLR detail goes to stderr instead of stdout.
                if (!(ex is PythonException))
                    Console.Error.WriteLine(ex.ToString());
                PyFinalizer.Shutdown();
                Environment.Exit(ExitStatusOf(ex));
            }
        }

        /// <summary>
        /// 코드 문자열을 직접 실행 (-c 옵션)
        /// </summary>
        /// <param name="codeString">실행할 Python 코드</param>
        public void ExecuteCodeString(string codeString)
        {
            // `python -c ""` runs the empty program and exits 0 (measured on the pinned reference).
            // Only a missing operand is an error, and that case never reaches here: the parser does
            // not set CodeString without one.
            if (codeString == null)
            {
                Console.WriteLine("❌ 실행할 코드가 지정되지 않았습니다.");
                Environment.Exit(1);
                return;
            }

            try
            {
                var interpreter = new IntegratedPythonInterpreter();
                interpreter.Execute(codeString, "<string>", false, false, false);
                PyFinalizer.Shutdown();
            }
            catch (Exception ex)
            {
                // Measured: every uncaught Python exception printed a second block,
                // "--- C# Exception Details ---" followed by the CLR exception text, on stdout.
                // The interpreter has already reported a Python-level exception in CPython's own
                // format by the time control reaches here, so that dump was pure divergence (and
                // was marked TEMPORARY). A failure nobody reported is an interpreter bug: it still
                // exits 1, but the CLR detail goes to stderr instead of stdout.
                if (!(ex is PythonException))
                    Console.Error.WriteLine(ex.ToString());
                PyFinalizer.Shutdown();
                Environment.Exit(ExitStatusOf(ex));
            }
        }

        /// <summary>
        /// The status the process ends with. SystemExit is not an error: CPython converts it where
        /// the exception reaches the top of the interpreter (_Py_HandleSystemExit,
        /// Python/pythonrun.c:708, reached from _PyErr_PrintEx at :786) and prints no traceback for
        /// it - which is why runtime/PyInterpreter.cs:236 re-throws it without printing.
        /// None means 0, an integer means that value truncated to C int, anything else is written to
        /// stderr with a trailing newline and means 1 (:735-:762). Status is decided before
        /// PyFinalizer.Shutdown() runs, so a sys.exit() raised inside an atexit callback cannot
        /// change it: that callback's exception is reported and dropped.
        /// </summary>
        private static int ExitStatusOf(Exception ex)
        {
            if (!(ex is PythonException pythonError) || !(pythonError.PyException is PySystemExit exit))
                return 1;
            int status = exit.HandleStatus(out var toWrite, out var tooLarge);
            if (toWrite != null)
            {
                Console.Error.Write(toWrite.ToStr().Value);
                Console.Error.WriteLine();
            }
            else if (tooLarge)
            {
                // PyLong_AsLong returns -1 and leaves OverflowError raised (:743).
                Console.Error.WriteLine("OverflowError: Python int too large to convert to C long");
            }
            return status;
        }
    }
}
