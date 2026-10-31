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
                // The status and the message come first, then teardown, exactly in the order
                // handle_system_exit (:786) -> Py_Exit (:777) uses.
                int exitStatus = ReportExitState(ex);
                PyFinalizer.Shutdown();
                Environment.Exit(exitStatus);
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
                int exitStatus = ReportExitState(ex);
                PyFinalizer.Shutdown();
                Environment.Exit(exitStatus);
            }
        }

        /// <summary>
        /// The message and the status, produced where the exception reaches the top of the
        /// interpreter. _PyErr_PrintEx calls handle_system_exit first (:786) and that function both
        /// writes the message and fixes the status; only then does Py_Exit (:777) finalize. So the
        /// status cannot depend on anything an atexit callback does afterwards, and the message goes
        /// to the sys.stderr of this moment (:747-:761), not to the CLR console: reassigning
        /// sys.stderr moves it (probes/l23_exit_message.py).
        /// Nothing here may escape as a CLR exception - a code whose __str__ raises still has to reach
        /// shutdown with status 1, which is what PyErr_Clear (:753) buys in CPython.
        /// </summary>
        private static int ReportExitState(Exception ex)
        {
            if (!(ex is PythonException pythonError) || !PySystemExit.Matches(pythonError.PyException))
                return 1;
            var error = pythonError.PyException;

            // :732 PyObject_GetAttr(exc, "code"); a failed lookup falls through to the writer.
            PyObject code;
            try { code = error.GetAttribute("code"); }
            catch (System.Exception) { code = null; }

            int status = PySystemExit.StatusOfCode(code, out var toWrite, out var tooLarge);
            try
            {
                if (toWrite != null)
                {
                    // PyFile_WriteObject then PySys_WriteStderr("\n") - two writes on the same
                    // stream (:755, :761).
                    WriteOnStderr(toWrite.ToStr().Value);
                    WriteOnStderr("\n");
                }
                else if (tooLarge)
                {
                    // PyLong_AsLong returns -1 and leaves OverflowError raised (:743).
                    WriteOnStderr("OverflowError: Python int too large to convert to C long\n");
                }
            }
            catch (PythonException reporting)
            {
                // The message could not be produced or written. Report it and keep the status.
                PyUnraisable.Report(reporting, error);
                status = 1;
            }
            catch (System.Exception reporting)
            {
                Console.Error.WriteLine(reporting.Message);
                status = 1;
            }
            return status;
        }

        /// <summary>
        /// PyFile_WriteObject / PySys_WriteStderr (:754-:761): the stream named sys.stderr right now.
        /// "\n" travels on the same channel, which is what makes sys.stderr = sys.stdout observable.
        /// </summary>
        private static void WriteOnStderr(string text)
        {
            PyObject stream = null;
            if (PyImportSystem.TryGetModule("sys", out var sys))
                sys.ModuleDict.TryGetValue("stderr", out stream);
            if (stream == null || ReferenceEquals(stream, PyNone.Instance))
            {
                Console.Error.Write(text);
                return;
            }
            var newline = text.EndsWith("\n");
            var body = newline ? text.Substring(0, text.Length - 1) : text;
            if (body.Length != 0)
                stream.GetAttribute("write").Call(new PyObject[] { new PyString(body) }, null);
            if (newline)
                stream.GetAttribute("write").Call(new PyObject[] { new PyString("\n") }, null);
        }
    }
}
