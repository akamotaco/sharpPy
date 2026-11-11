using System.IO;
using SharpPy.Generated;
using SharpPy.Tools;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 compilation modes
    /// Reference: Include/compile.h:8-11
    /// </summary>
    public enum CompileMode
    {
        // Include/compile.h:9
        // #define Py_file_input 257
        File = 257,      // exec mode: multiple statements (file execution)

        // Include/compile.h:10
        // #define Py_eval_input 258
        Eval = 258,      // eval mode: single expression (returns value)

        // Include/compile.h:8
        // #define Py_single_input 256
        Single = 256     // single mode: single statement/expression (REPL, prints expr result)
    }

    #region Interpreter Integration (기존 시스템과 통합)

    // 통합 Python 인터프리터 (기존 시스템 활용)
    public class IntegratedPythonInterpreter
    {
        // Using CPython 3.12 compatible PEG parser via bridge
        private readonly PyVM _vm;
        private readonly PyScopeChain _globalScope;
        
        // For Python-like error reporting
        
        public IntegratedPythonInterpreter()
        {
            // CPython 3.12 compatible PEG parser is used via GeneratedParserBridge
            _vm = PyVM.Instance;

            // CPython 3.12: Python/pythonrun.c:408 - PyImport_AddModule("__main__")
            // Create __main__ module and register in sys.modules
            var mainModule = new PyModule("__main__", "<main>");
            mainModule.ModuleDict.Remove("__file__");
            mainModule.ModuleDict["__name__"] = PyString.From("__main__");
            mainModule.ModuleDict["__doc__"] = PyNone.Instance;
            mainModule.ModuleDict["__package__"] = PyNone.Instance;
            mainModule.ModuleDict["__loader__"] = PyNone.Instance;
            mainModule.ModuleDict["__spec__"] = PyNone.Instance;
            PyImportSystem.SetModule("__main__", mainModule);

            // CPython 3.12: Python/ceval.c - frame->f_globals references module->md_dict
            // _globalScope must use mainModule's dict and reference mainModule
            // This ensures globals() returns the same object as sys.modules['__main__'].__dict__
            _globalScope = new PyScopeChain(mainModule.ModuleDict, "__main__", mainModule);

            // init_interp_main: the standard streams, once the import system is up.
            using (PyHost.Enter())
            {
                PyImportSystem.Import("sys");
                Modules.SysModule.InitStdStreams();
            }

            SetupBuiltinHelpers();
        }
        
        // 기존 시스템과 연동을 위한 도우미 함수들 추가
        private void SetupBuiltinHelpers()
        {
            // CPython 스타일로 MAKE_FUNCTION 바이트코드 사용으로 변경
            // __make_function__ 헬퍼는 더 이상 필요없음
            
            // var makeFunctionHelper = new PyBuiltinFunction("__make_function__");
            // _globalScope.AssignVariable("__make_function__", makeFunctionHelper);
        }
        
        // 전체 실행 파이프라인 (기존 시스템과 완전 통합)
        public PyObject Execute(string sourceCode)
        {
            return Execute(sourceCode, null, false, false, false, CompileMode.File);
        }

        /// <summary>
        /// Execute Python code in single mode (REPL)
        /// CPython 3.12: Python/pythonrun.c:266 uses Py_single_input for REPL
        /// </summary>
        public PyObject ExecuteSingle(string sourceCode)
        {
            return Execute(sourceCode, "<stdin>", false, false, false, CompileMode.Single);
        }

        /// <summary>
        /// Evaluate Python expression and return result
        /// CPython 3.12: Python/bltinmodule.c:776-781 uses Py_eval_input for eval()
        /// </summary>
        public PyObject ExecuteEval(string expression)
        {
            return Execute(expression, "<eval>", false, false, false, CompileMode.Eval);
        }

        // fromFile: the script pyrun_file runs, tokenized as a file.
        public PyObject Execute(string sourceCode, string fileName, bool showTokenize, bool showAst, bool showBytecode, CompileMode mode = CompileMode.File,
            bool fromFile = false)
        {
            using var hostScope = PyHost.Enter();
            // A host reads the console when a run returns, so what the streams buffered is out by then.
            try
            {
                return ExecuteUnflushed(sourceCode, fileName, showTokenize, showAst, showBytecode, mode, fromFile);
            }
            finally
            {
                Modules.SysModule.FlushStdFiles();
            }
        }

        private PyObject ExecuteUnflushed(string sourceCode, string fileName, bool showTokenize, bool showAst, bool showBytecode, CompileMode mode, bool fromFile)
        {
            // CPython 3.12: Python/pythonrun.c - co_filename uses absolute path for file execution
            // Convert relative paths to absolute paths (like CPython does for __file__)
            // But keep special names like "<string>", "<stdin>", "<module>" as-is
#if GODOT
            bool virtualPath = fileName?.StartsWith("res://", StringComparison.Ordinal) == true ||
                               fileName?.StartsWith("user://", StringComparison.Ordinal) == true;
#else
            const bool virtualPath = false;
#endif
            if (!virtualPath && !string.IsNullOrEmpty(fileName) && !fileName.StartsWith("<", StringComparison.Ordinal) && !Path.IsPathRooted(fileName))
            {
                fileName = Path.GetFullPath(fileName);
            }

            // Store filename and source lines for Python-like error reporting
            if (mode == CompileMode.File && !string.IsNullOrEmpty(fileName) && !fileName.StartsWith("<", StringComparison.Ordinal))
                PyGlobalsDict.Store(_globalScope.GlobalScope.Variables, "__file__", PyString.From(fileName));
            // Fix: Handle all line ending types correctly (\r\n, \r, \n)
            
            // Verbose 모드일 때만 상세 디버그 정보 출력
#if DEBUG_LOG
            Console.WriteLine("🐍 통합 Python 인터프리터 실행");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"소스:\n{sourceCode}");
            if (!string.IsNullOrEmpty(fileName))
            {
                Console.WriteLine($"파일: {fileName}");
            }
            Console.WriteLine(new string('=', 60));
#endif
            
            try
            {
                // 1단계: 파싱 (소스 → AST)
#if DEBUG_LOG
                Console.WriteLine("\n" + new string('=',30));
                Console.WriteLine("1️⃣ 파싱: 소스 → AST");
                Console.WriteLine(new string('=', 30));
#endif
                                if(showTokenize)
                {
                    var tokens = PyParserRuntime.LexerSource(sourceCode, generateExtraTokens: true);
                    Console.WriteLine("[===== tokenize log ====]");
                    for (int i = 0; i < tokens.Count; ++i)
                    {
                        var t = tokens[i];
                        Console.WriteLine($"{i}:(lines: {t.Line}-{t.EndLine}/ cols: {t.Column}-{t.EndColumn})\t{t.Value}\t[{t.Type}]");
                    }
                    Console.WriteLine("[===== tokenize end ====]");
                    return PyNone.Instance;
                }

                if (showAst)
                {
                    // The tree the parser built, the one to compare with CPython's ast.dump.
                    if (Environment.GetEnvironmentVariable("SHARPPY_SYMTABLE") == "1")
                        Console.Write(SymtableDumper.Dump(PyParserRuntime.ParseToAsdl(sourceCode, fileName ?? "<string>"), fileName ?? "<string>"));
                    Console.WriteLine("[===== ast log ====]");
                    Console.WriteLine(AsdlDumper.Dump(PyParserRuntime.ParseToAsdl(sourceCode, fileName ?? "<string>")));
                    Console.WriteLine("[===== ast end ====]");
                    return PyNone.Instance;
                }


                // 2단계: 컴파일 (AST → 바이트코드)
#if DEBUG_LOG
                Console.WriteLine("\n" + new string('=', 30));
                Console.WriteLine($"2️⃣ 컴파일: AST → 바이트코드 (mode: {mode})");
                Console.WriteLine(new string('=', 30));
#endif
                // CPython 3.12: Python/bltinmodule.c:776-781
                // "exec" → Py_file_input, "eval" → Py_eval_input, "single" → Py_single_input
                var codeObject = Compiler.CompileSource(sourceCode, fileName ?? "<string>", mode, fileInput: fromFile);

#if DEBUG_LOG
                Console.WriteLine($"🔍 컴파일 직후 Exception Table entries: {codeObject.ExceptionTable.Count}");
#endif

                // 3단계: 바이트코드 확인 (ShowBytecode 또는 VerboseMode일 때)
                if (SharpPyConfig.ShowBytecode || showBytecode)
                {
                    Console.WriteLine("[===== bytecode log ====]");
                    codeObject.Disassemble();
                    Console.WriteLine("[===== bytecode end ====]");
                }
#if DEBUG_LOG
                else
                {
                    Console.WriteLine("\n" + new string('=', 30));
                    Console.WriteLine("3️⃣ 바이트코드 확인");
                    Console.WriteLine(new string('=', 30));
                    codeObject.Disassemble();
                }
                Console.WriteLine($"🔍 디스어셈블리 후 Exception Table entries: {codeObject.ExceptionTable.Count}");
#endif

                // 4단계: VM 실행 (기존 시스템들과 연동)
#if DEBUG_LOG
                Console.WriteLine("\n" + new string('=', 30));
                Console.WriteLine("4️⃣ VM 실행 (기존 LEGB 시스템 사용)");
                Console.WriteLine(new string('=', 30));
#endif

#if DEBUG_LOG
                Console.WriteLine($"🔍 VM 실행 직전 Exception Table entries: {codeObject.ExceptionTable.Count}");
#endif

                var result = _vm.ExecuteModule(codeObject, _globalScope);
                
#if DEBUG_LOG
                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine($"🎉 최종 결과: {result}");
                Console.WriteLine(new string('=', 60));
#endif
                
                return result;
            }
            catch (PythonException pe) when (PySystemExit.Matches(pe.PyException))
            {
                // The test is PyErr_GivenExceptionMatches(exc, PyExc_SystemExit)
                // (Python/pythonrun.c:717), so a subclass of SystemExit is silent too: a CLR-type test
                // printed a traceback and turned the status into 1 for `class Sub(SystemExit)`.
                throw;
            }
            catch (Exception e)
            {
                // Always print traceback in both debug and release modes
                PrintPythonStyleTraceback(e);
                // Re-throw to let Program.cs handle exit code
                throw;
            }
        }

        /// <summary>
        /// Compile Python source code to bytecode without executing it
        /// CPython 3.12: Similar to `python -m py_compile file.py`
        /// </summary>
        /// <param name="sourceCode">Python source code</param>
        /// <param name="fileName">File name for error reporting</param>
        /// <param name="showTokenize">Show tokenization output</param>
        /// <param name="showAst">Show AST output</param>
        /// <param name="showBytecode">Show bytecode disassembly</param>
        /// <returns>Compiled code object (not executed)</returns>
        public PyCodeObject CompileOnly(string sourceCode, string fileName, bool showTokenize, bool showAst, bool showBytecode, bool fromFile = false)
        {
            // Store filename and source lines for Python-like error reporting

#if DEBUG_LOG
            Console.WriteLine("🐍 Python 컴파일 전용 모드 (실행 안 함)");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"소스:\n{sourceCode}");
            if (!string.IsNullOrEmpty(fileName))
            {
                Console.WriteLine($"파일: {fileName}");
            }
            Console.WriteLine(new string('=', 60));
#endif

            try
            {
                // 1단계: 파싱 (소스 → AST)
#if DEBUG_LOG
                Console.WriteLine("\n" + new string('=',30));
                Console.WriteLine("1️⃣ 파싱: 소스 → AST");
                Console.WriteLine(new string('=', 30));
#endif
                if(showTokenize)
                {
                    var tokens = PyParserRuntime.LexerSource(sourceCode, generateExtraTokens: true);
                    Console.WriteLine("[===== tokenize log ====]");
                    for (int i = 0; i < tokens.Count; ++i)
                    {
                        var t = tokens[i];
                        Console.WriteLine($"{i}:(lines: {t.Line}-{t.EndLine}/ cols: {t.Column}-{t.EndColumn})\t{t.Value}\t[{t.Type}]");
                    }
                    Console.WriteLine("[===== tokenize end ====]");
                    return null;
                }

                if (showAst)
                {
                    // The tree the parser built, the one to compare with CPython's ast.dump.
                    if (Environment.GetEnvironmentVariable("SHARPPY_SYMTABLE") == "1")
                        Console.Write(SymtableDumper.Dump(PyParserRuntime.ParseToAsdl(sourceCode, fileName ?? "<string>"), fileName ?? "<string>"));
                    Console.WriteLine("[===== ast log ====]");
                    Console.WriteLine(AsdlDumper.Dump(PyParserRuntime.ParseToAsdl(sourceCode, fileName ?? "<string>")));
                    Console.WriteLine("[===== ast end ====]");
                    return null;
                }


                // 2단계: 컴파일 (AST → 바이트코드)
#if DEBUG_LOG
                Console.WriteLine("\n" + new string('=', 30));
                Console.WriteLine("2️⃣ 컴파일: AST → 바이트코드 (실행 안 함)");
                Console.WriteLine(new string('=', 30));
#endif
                var codeObject = Compiler.CompileSource(sourceCode, fileName ?? "<string>", fileInput: fromFile);

#if DEBUG_LOG
                Console.WriteLine($"🔍 컴파일 완료: {codeObject.Instructions.Count} instructions");
#endif

                // 3단계: 바이트코드 출력 (요청 시)
                if (SharpPyConfig.ShowBytecode || showBytecode)
                {
                    Console.WriteLine("[===== bytecode log ====]");
                    codeObject.Disassemble();
                    Console.WriteLine("[===== bytecode end ====]");
                }

#if DEBUG_LOG
                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine("✅ 컴파일 완료 (실행하지 않음)");
                Console.WriteLine(new string('=', 60));
#endif

                // Compiling a file caches it the way importing it would (SourceLoader.get_code).
                if (!string.IsNullOrEmpty(fileName) && fileName != "<string>")
                    PyBytecodeCache.Store(fileName, codeObject);

                return codeObject;
            }
            catch (Exception e)
            {
                // Always print traceback in both debug and release modes
                PrintPythonStyleTraceback(e);
                // Re-throw to let Program.cs handle exit code
                throw;
            }
        }

        /// <summary>
        /// _PyErr_PrintEx (Python/pythonrun.c): sys.last_* are set and sys.excepthook reports; a hook
        /// that fails is reported in its turn, then the original exception, both by PyErr_Display.
        /// </summary>
        internal void PrintPythonStyleTraceback(Exception e)
        {
            // The parser's own host fault says SyntaxError; any other host fault is the VM's conversion.
            PyBaseException exception = e is PySyntaxErrorException syntax
                ? new PySyntaxError(syntax.Message)
                : PyVM.ConvertToPythonException(e);
            PyObject type = exception.GetPyType();
            PyObject traceback = (PyObject)exception.__traceback__ ?? PyNone.Instance;
            var sys = PyImportSystem.Import("sys");
            foreach (var (name, value) in new[] { ("last_exc", (PyObject)exception), ("last_type", type), ("last_value", exception), ("last_traceback", traceback) })
            {
                try
                {
                    sys.SetAttribute(name, value);
                }
                catch (PythonException)
                {
                }
            }
            if (!sys.ModuleDict.TryGetValue("excepthook", out var hook))
            {
                WriteStderr("sys.excepthook is missing\n");
                PyErrorDisplay.Display(exception, null);
                return;
            }
            try
            {
                hook.Call(new[] { type, exception, traceback }, null);
            }
            catch (PythonException failure)
            {
                if (PySystemExit.Matches(failure.PyException))
                    throw;
                Console.Out.Flush();
                WriteStderr("Error in sys.excepthook:\n");
                PyErrorDisplay.Display(failure.PyException, null);
                WriteStderr("\nOriginal exception was:\n");
                PyErrorDisplay.Display(exception, null);
            }
        }

        // PySys_WriteStderr: to sys.stderr, or to the process's stderr when there is none.
        private static void WriteStderr(string text)
        {
            try
            {
                if (PyImportSystem.Import("sys").ModuleDict.TryGetValue("stderr", out var stderr) && stderr is not PyNone)
                {
                    stderr.GetAttribute("write").Call(new PyObject[] { PyString.From(text) }, null);
                    return;
                }
            }
            catch (PythonException)
            {
            }
            Console.Error.Write(text);
        }

        // 대화형 실행 (REPL 스타일)
        public void Interactive()
        {
            Console.Error.WriteLine("🐍 Python 대화형 모드 (기존 시스템 통합)");
            Console.Error.WriteLine("종료하려면 'exit' 입력");
            
            while (true)
            {
                Console.Error.Write(">>> ");
                var input = Console.ReadLine();
                
                if (string.IsNullOrEmpty(input) || input == "exit")
                    break;
                    
                try
                {
                    var result = Execute(input);
                    if (result != PyNone.Instance)
                    {
                        Console.WriteLine($"출력: {result}");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"오류: {e.Message}");
                }
            }
        }
        
        // 현재 전역 스코프 상태 출력
        public void PrintGlobalState()
        {
            Console.WriteLine("\n📊 전역 스코프 상태:");
            _globalScope.PrintSystemState();
        }

        /// <summary>
        /// 전역 스코프에서 변수 값 가져오기
        /// </summary>
        public PyObject GetGlobalVariable(string name)
        {
            return _globalScope.LookupVariable(name);
        }

        /// <summary>
        /// 전역 스코프에 변수 설정
        /// </summary>
        public void SetGlobalVariable(string name, PyObject value)
        {
            _globalScope.AssignVariable(name, value);
        }
    }

    #endregion
}
