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
        private string _currentFileName;
        private string[] _sourceLines;
        
        public IntegratedPythonInterpreter()
        {
            // CPython 3.12 compatible PEG parser is used via GeneratedParserBridge
            _vm = PyVM.Instance;

            // CPython 3.12: Python/pythonrun.c:408 - PyImport_AddModule("__main__")
            // Create __main__ module and register in sys.modules
            var mainModule = new PyModule("__main__", "<main>");
            mainModule.ModuleDict.Remove("__file__");
            mainModule.ModuleDict["__name__"] = new PyString("__main__");
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

        public PyObject Execute(string sourceCode, string fileName, bool showTokenize, bool showAst, bool showBytecode, CompileMode mode = CompileMode.File)
        {
            using var hostScope = PyHost.Enter();
            // A host reads the console when a run returns, so what the streams buffered is out by then.
            try
            {
                return ExecuteUnflushed(sourceCode, fileName, showTokenize, showAst, showBytecode, mode);
            }
            finally
            {
                Modules.SysModule.FlushStdFiles();
            }
        }

        private PyObject ExecuteUnflushed(string sourceCode, string fileName, bool showTokenize, bool showAst, bool showBytecode, CompileMode mode)
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
                PyGlobalsDict.Store(_globalScope.GlobalScope.Variables, "__file__", new PyString(fileName));
            _currentFileName = fileName;
            // Fix: Handle all line ending types correctly (\r\n, \r, \n)
            _sourceLines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
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
                var codeObject = Compiler.CompileSource(sourceCode, fileName ?? "<string>", mode);

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
        public PyCodeObject CompileOnly(string sourceCode, string fileName, bool showTokenize, bool showAst, bool showBytecode)
        {
            // Store filename and source lines for Python-like error reporting
            _currentFileName = fileName;
            _sourceLines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

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
                var codeObject = Compiler.CompileSource(sourceCode, fileName ?? "<string>");

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
        /// Print Python-style traceback with filename, line numbers, and source code
        /// CPython 3.12 compatible: Prints full call stack with traceback chain
        /// </summary>
        private void PrintPythonStyleTraceback(Exception e)
        {
            // PyErr_PrintEx: sys.excepthook reports, and a failing hook is reported in its turn.
            if (e is PythonException { PyException: not null } raised)
            {
                var exception = raised.PyException;
                var arguments = new PyObject[]
                {
                    exception.GetPyType(), exception, (PyObject)exception.__traceback__ ?? PyNone.Instance,
                };
                try
                {
                    var sys = PyImportSystem.Import("sys");
                    sys.SetAttribute("last_exc", exception);
                    sys.SetAttribute("last_type", arguments[0]);
                    sys.SetAttribute("last_value", exception);
                    sys.SetAttribute("last_traceback", arguments[2]);
                    sys.GetAttribute("excepthook").Call(arguments, null);
                    return;
                }
                catch (PythonException failure)
                {
                    if (PySystemExit.Matches(failure.PyException))
                        throw;
                    Console.Error.WriteLine("Error in sys.excepthook:");
                    Console.Error.WriteLine($"{failure.PyException.GetTypeName()}: {failure.PyException.AsString().Value}");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Original exception was:");
                }
            }
            Console.Error.WriteLine("Traceback (most recent call last):");

            // CPython 3.12: Use traceback chain from exception if available
            if (e is PythonException pyEx && pyEx.PyException.__traceback__ != null)
            {
                // CPython 3.12: Chain is already in outermost-first order
                // __traceback__ points to outermost frame, next points to progressively inner frames
                // e.g., <module> → outer → middle → inner → NULL
                var tb = pyEx.PyException.__traceback__;
                while (tb != null)
                {
                    PrintTracebackFrame(tb);
                    tb = tb.Next;
                }
            }
            else
            {
                // Fallback: Print current frame only (old behavior)
                PrintCurrentFrameFallback(e);
            }

            // CPython 3.12: Print exception chaining (__cause__ and __context__)
            if (e is PythonException pyEx2 && pyEx2.PyException != null)
            {
                PrintExceptionChaining(pyEx2.PyException);
            }

            // Print exception type and message
            PrintExceptionMessage(e);
        }

        /// <summary>
        /// Print a single traceback frame (CPython 3.12 format)
        /// </summary>
        private void PrintTracebackFrame(PyTraceback tb)
        {
            var frame = tb.Frame;
            var fileName = frame.CurrentFileName ?? _currentFileName ?? "<stdin>";
            var functionName = frame.Code.Name ?? "<module>";
            var lineNumber = tb.LineNo;

            Console.Error.WriteLine($"  File \"{fileName}\", line {lineNumber}, in {functionName}");

            // Show source line with ^^^ markers using column info from traceback
            var colOffset = tb.ColNo >= 0 ? tb.ColNo : frame.CurrentColumnOffset;
            var endColOffset = tb.EndColNo >= 0 ? tb.EndColNo : colOffset;
            ShowSourceLineWithMarkers(fileName, lineNumber, colOffset, endColOffset);
        }

        /// <summary>
        /// Show source line with ^^^ markers (CPython 3.12 format)
        /// </summary>
        private void ShowSourceLineWithMarkers(string fileName, int lineNumber, int columnOffset, int endColumnOffset = -1)
        {
            // Try to get source line
            string? sourceLine = null;

            if (_sourceLines != null && lineNumber > 0 && lineNumber <= _sourceLines.Length)
            {
                sourceLine = _sourceLines[lineNumber - 1];
            }

            if (!string.IsNullOrEmpty(sourceLine))
            {
                var trimmedLine = sourceLine.Trim();
                Console.Error.WriteLine($"    {trimmedLine}");

                // Add ^^^ markers if column information is available
                if (columnOffset >= 0)
                {
                    var leadingSpaces = sourceLine.Length - trimmedLine.Length;
                    var adjustedStartColumn = Math.Max(0, columnOffset - leadingSpaces);

                    // Calculate marker length (CPython 3.12 style)
                    int markerLength = 1;
                    if (endColumnOffset > columnOffset)
                    {
                        var adjustedEndColumn = Math.Max(0, endColumnOffset - leadingSpaces);
                        markerLength = Math.Max(1, adjustedEndColumn - adjustedStartColumn);
                    }

                    // Clamp to line length
                    adjustedStartColumn = Math.Min(adjustedStartColumn, trimmedLine.Length);
                    markerLength = Math.Min(markerLength, trimmedLine.Length - adjustedStartColumn);

                    var markers = new string(' ', adjustedStartColumn) + new string('^', Math.Max(1, markerLength));
                    Console.Error.WriteLine($"    {markers}");
                }
            }
            else
            {
                Console.Error.WriteLine($"    # Source line not available (line {lineNumber})");
            }
        }

        /// <summary>
        /// Print current frame only (fallback for exceptions without traceback)
        /// </summary>
        private void PrintCurrentFrameFallback(Exception e)
        {
            var currentFrame = PyVM.CurrentFrame;

            if (currentFrame != null && currentFrame.CurrentLineNumber > 0)
            {
                var fileName = _currentFileName ?? "<stdin>";
                var functionName = currentFrame.Code.Name ?? "<module>";
                var lineNumber = currentFrame.CurrentLineNumber;

                Console.Error.WriteLine($"  File \"{fileName}\", line {lineNumber}, in {functionName}");
                ShowSourceLineWithMarkers(fileName, lineNumber, currentFrame.CurrentColumnOffset);
            }
            else if (e is PySyntaxErrorException syntaxEx && syntaxEx.LineNumber > 0)
            {
                // SyntaxError special handling
                var fileName = syntaxEx.FileName ?? _currentFileName ?? "<stdin>";
                Console.Error.WriteLine($"  File \"{fileName}\", line {syntaxEx.LineNumber}");

                var sourceLine = syntaxEx.SourceLine;
                if (string.IsNullOrEmpty(sourceLine) && _sourceLines != null && syntaxEx.LineNumber <= _sourceLines.Length)
                {
                    sourceLine = _sourceLines[syntaxEx.LineNumber - 1];
                }

                if (!string.IsNullOrEmpty(sourceLine))
                {
                    Console.Error.WriteLine($"    {sourceLine}");
                    if (syntaxEx.ColumnOffset >= 0)
                    {
                        var markerLength = syntaxEx.EndColumnOffset > syntaxEx.ColumnOffset
                            ? syntaxEx.EndColumnOffset - syntaxEx.ColumnOffset : 3;
                        var marker = new string(' ', syntaxEx.ColumnOffset) + new string('^', markerLength);
                        Console.Error.WriteLine($"    {marker}");
                    }
                }
            }
            else
            {
                // No frame info available
                Console.Error.WriteLine($"  File \"{_currentFileName ?? "<stdin>"}\", line ?, in <module>");
                Console.Error.WriteLine($"    # Line information not available");
            }
        }

        /// <summary>
        /// Print exception chaining (__cause__ and __context__)
        /// CPython 3.12: Recursive exception printing
        /// </summary>
        private void PrintExceptionChaining(PyBaseException exception)
        {
            // Print __cause__ first (explicit chaining with "raise ... from ...")
            if (exception.__cause__ != null)
            {
                // Recursively print the cause
                PrintChainedException(exception.__cause__, isContext: false);
                return; // __cause__ takes precedence over __context__
            }

            // Print __context__ if no __cause__ and __suppress_context__ is False
            if (!exception.__suppress_context__ && exception.__context__ != null)
            {
                PrintChainedException(exception.__context__, isContext: true);
            }
        }

        /// <summary>
        /// Print a chained exception
        /// </summary>
        private void PrintChainedException(PyBaseException chainedException, bool isContext)
        {
            // CPython 3.12: Print chained exceptions recursively first (oldest first)
            // Recursively handle chaining BEFORE printing this exception
            PrintExceptionChaining(chainedException);

            Console.Error.WriteLine();

            // Print traceback for the chained exception
            if (chainedException.__traceback__ != null)
            {
                Console.Error.WriteLine("Traceback (most recent call last):");

                // CPython 3.12: Chain is already in outermost-first order
                // __traceback__ points to outermost frame, next points to progressively inner frames
                var tb = chainedException.__traceback__;
                while (tb != null)
                {
                    PrintTracebackFrame(tb);
                    tb = tb.Next;
                }
            }

            // Print exception message
            Console.Error.WriteLine($"{chainedException.GetTypeName()}: {chainedException.ToStr().Value}");
            Console.Error.WriteLine();

            // Print transition message
            if (isContext)
            {
                Console.Error.WriteLine("During handling of the above exception, another exception occurred:");
                Console.Error.WriteLine();
            }
            else
            {
                Console.Error.WriteLine("The above exception was the direct cause of the following exception:");
                Console.Error.WriteLine();
            }
        }

        /// <summary>
        /// Print exception type and message
        /// </summary>
        private void PrintExceptionMessage(Exception e)
        {
            string pythonExceptionType;
            string exceptionMessage;

            if (e is PythonException pyEx && pyEx.PyException != null)
            {
                pythonExceptionType = pyEx.PyException.GetTypeName();
                exceptionMessage = pyEx.PyException.ToStr().Value;
            }
            else
            {
                var exceptionTypeName = e.GetType().Name;
                pythonExceptionType = exceptionTypeName switch
                {
                    "PyNameError" => "NameError",
                    "PyTypeError" => "TypeError",
                    "PyValueError" => "ValueError",
                    "PyAttributeError" => "AttributeError",
                    "PyKeyError" => "KeyError",
                    "PyIndexError" => "IndexError",
                    "PyRuntimeError" => "RuntimeError",
                    "PyNotImplementedError" => "NotImplementedError",
                    "PySyntaxError" => "SyntaxError",
                    "PySyntaxErrorException" => "SyntaxError",
                    "PyIndentationError" => "IndentationError",
                    "PyTabError" => "TabError",
                    "PySystemError" => "SystemError",
                    "PyImportError" => "ImportError",
                    "PyModuleNotFoundError" => "ModuleNotFoundError",
                    "PyOSError" => "OSError",
                    "PyFileNotFoundError" => "FileNotFoundError",
                    "PyPermissionError" => "PermissionError",
                    _ when e.Message.Contains("not defined") => "NameError",
                    _ when e.Message.Contains("not found") => "NameError",
                    _ when e.Message.Contains("has no attribute") => "AttributeError",
                    _ when e.Message.Contains("required argument") => "TypeError",
                    _ when e.Message.Contains("Complex target patterns") => "RuntimeError",
                    _ when e.Message.Contains("not implemented") => "NotImplementedError",
                    _ => "RuntimeError"
                };
                exceptionMessage = e.Message;
            }

            // CPython 3.12: PyErr_Display appends suggestions for NameError at
            // display time (Python/suggestions.c offer_suggestions_for_name_error).
            // Caught exceptions keep the plain message; only this display gains the suffix.
            if (e is PythonException pyExSug && pyExSug.PyException is PyNameError displayErr &&
                displayErr.GetAttribute("name") is PyString wrongName && wrongName.Value.Length > 0)
            {
                string? displaySuggestion = ErrorSuggestions.GetDisplaySuggestionForNameError(
                    pyExSug.PyException.__traceback__, wrongName.Value);
                bool stdlibModule = StdlibModuleNames.Contains(wrongName.Value);
                if (displaySuggestion != null && stdlibModule)
                    exceptionMessage += $". Did you mean: '{displaySuggestion}'? Or did you forget to import '{wrongName.Value}'?";
                else if (displaySuggestion != null)
                    exceptionMessage += $". Did you mean: '{displaySuggestion}'?";
                else if (stdlibModule)
                    exceptionMessage += $". Did you forget to import '{wrongName.Value}'?";
            }

            Console.Error.WriteLine($"{pythonExceptionType}: {exceptionMessage}");
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
