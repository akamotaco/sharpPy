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

                // config_run_filename_abspath: the script's name is made absolute, on Windows even
                // when it is rooted already (GetFullPathNameW also turns forward slashes into
                // backslashes). sys.argv
                // keeps what was typed.
                if (OperatingSystem.IsWindows() || !System.IO.Path.IsPathRooted(pythonFile))
                {
                    try
                    {
                        pythonFile = System.IO.Path.GetFullPath(pythonFile);
                    }
                    catch (Exception)
                    {
                        // failed to get the absolute path: keep the name as given
                    }
                }

                // pymain_run_file runs after init_interp_main: the streams exist before the source is read.
                var interpreter = new IntegratedPythonInterpreter();
                string code;
                try
                {
                    code = Generated.Tokenizer.DecodeFile(IOHelper.ReadAllBytes(pythonFile), pythonFile);
                }
                catch (PythonException error)
                {
                    // The interpreter reports what it runs; this never got that far.
                    var failure = error.PyException;
                    PyImportSystem.Import("sys").GetAttribute("excepthook").Call(
                        new PyObject[] { failure.GetPyType(), failure, PyNone.Instance }, null);
                    throw;
                }

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
                        if (searchPath.Length() == 0) searchPath.Append(PyString.From(directory));
                        else searchPath.SetItem(PyInt.From(0), PyString.From(directory));
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
        /// _Py_HandleSystemExit (Python/pythonrun.c:708) as one path: dig the code out (:732), decide
        /// the status, write what has to be written to the sys.stderr of this moment, and leave one
        /// failure pending for one report. It runs before PyFinalizer.Shutdown() because
        /// handle_system_exit (:786) reports and fixes the status and only then Py_Exit (:777)
        /// finalises - so an atexit callback cannot move a status that is already fixed.
        ///
        /// Nothing may escape as a CLR exception. PyErr_Clear (:753) is what lets a code whose
        /// __str__ raises still reach shutdown with status 1.
        /// </summary>
        internal static int ReportExitState(Exception ex)
        {
            if (!(ex is PythonException pythonError) || !PySystemExit.Matches(pythonError.PyException))
                return 1;
            var exc = pythonError.PyException;

            // :732 PyObject_GetAttr(exc, "code"). A failed lookup is not an error to report - the
            // source comment says to let the else clause print it, and :753 clears it on the way. The
            // exception object itself is the payload then, so the status is 1, not 0
            // (probes/l24_exit_lookup.py).
            PyObject payload;
            try
            {
                payload = exc.GetAttribute("code");
                if (ReferenceEquals(payload, PyNone.Instance)) return 0;   // goto done (:735-:737)
            }
            catch (PythonException) { payload = exc; }

            PythonException pending = null;
            int status = PySystemExit.StatusOfCode(payload, out var toWrite, out var tooLarge);
            if (tooLarge)
            {
                // PyLong_AsLong returns -1 and leaves OverflowError raised (:743). The status stays
                // -1 and the error travels; it is not a string written here
                // (probes/l24_write_huge.py shows the four writes of the report instead).
                pending = new PythonException(
                    new PyOverflowError("Python int too large to convert to C long"));
            }

            if (toWrite != null)
            {
                string text;
                try { text = toWrite.ToStr().Value; }
                catch (PythonException failure) { pending = failure; text = null; }
                if (text != null && !WriteOnStderr(text, out var writeFailure)) pending = writeFailure;
                // PySys_WriteStderr("\n") (:761). It is attempted whatever happened to the message,
                // and the pending error is set aside and restored around it.
                SysWriteStderr("\n", ref pending);
            }

            // wait_for_thread_shutdown (Python/pylifecycle.c:3039) finds the error still pending at
            // :1830 and calls PyErr_WriteUnraisable(NULL) - object None, err_msg None - before
            // _PyAtExit_Call runs the atexit callbacks at :1845. One error, one report
            // (probes/l24_write_bad_str.py, l24_write_raises.py).
            if (pending != null)
            {
                try { PyUnraisable.Report(pending, PyNone.Instance); }
                catch (PythonException) { }
                catch (System.Exception) { }
            }
            return status;
        }

        /// <summary>
        /// PyFile_WriteObject (:755): the stream named sys.stderr right now, one call, the text as it
        /// stands - an empty message is still a write (probes/l24_write_empty.py). Returns false when
        /// the write failed and carries the failure out, because CPython leaves it pending.
        /// </summary>
        private static bool WriteOnStderr(string text, out PythonException failure)
        {
            failure = null;
            PyObject stream = null;
            if (PyImportSystem.TryGetModule("sys", out var sys))
                sys.ModuleDict.TryGetValue("stderr", out stream);
            if (stream == null || ReferenceEquals(stream, PyNone.Instance))
            {
                // :757-:759 PyObject_Print to the C stderr FILE.
                Console.Error.Write(text);
                return true;
            }
            try
            {
                stream.GetAttribute("write").Call(new PyObject[] { PyString.From(text) }, null);
                return true;
            }
            catch (PythonException error)
            {
                failure = error;
                return false;
            }
        }

        /// <summary>
        /// PySys_WriteStderr (:761 through sys_format, Python/sysmodule.c:4001-:4021): the pending
        /// error is set aside and restored, and if the write to sys.stderr fails the text goes to the
        /// C stderr FILE instead of failing again.
        /// </summary>
        private static void SysWriteStderr(string text, ref PythonException pending)
        {
            var saved = pending;
            pending = null;
            if (!WriteOnStderr(text, out _)) Console.Error.Write(text);
            pending = saved;
        }
    }
}
