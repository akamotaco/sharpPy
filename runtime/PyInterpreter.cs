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
        private readonly PythonCompiler _compiler;
        private readonly PyVM _vm;
        private readonly PyScopeChain _globalScope;
        
        // For Python-like error reporting
        private string _currentFileName;
        private string[] _sourceLines;
        
        public IntegratedPythonInterpreter()
        {
            // CPython 3.12 compatible PEG parser is used via GeneratedParserBridge
            _compiler = new PythonCompiler();
            _vm = PyVM.Instance;
            _globalScope = new PyScopeChain(); // 기존 LEGB 시스템!
            
            // CPython 3.12: Python/pythonrun.c:408 - PyImport_AddModule("__main__")
            // Create __main__ module and register in sys.modules
            var mainModule = new PyModule("__main__", "<main>");
            mainModule.ModuleDict["__name__"] = new PyString("__main__");
            mainModule.ModuleDict["__doc__"] = PyNone.Instance;
            mainModule.ModuleDict["__package__"] = PyNone.Instance;
            mainModule.ModuleDict["__loader__"] = PyNone.Instance;
            mainModule.ModuleDict["__spec__"] = PyNone.Instance;
            PyImportSystem.SetModule("__main__", mainModule);

            // Set __name__ in global scope
            _globalScope.AssignVariable("__name__", new PyString("__main__"));

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

        public PyObject Execute(string sourceCode, string fileName, bool showTokenize, bool showAst, bool showBytecode, CompileMode mode = CompileMode.File)
        {
            // Store filename and source lines for Python-like error reporting
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
                // CPython 3.12: Pass generateExtraTokens=true for --tokens mode to include COMMENT/NL
                var tokens = PyParserRuntime.LexerSource(sourceCode, generateExtraTokens: showTokenize);

                if(showTokenize)
                {
                    Console.WriteLine("[===== tokenize log ====]");
                    for (int i = 0; i < tokens.Count; ++i)
                    {
                        var t = tokens[i];
                        Console.WriteLine($"{i}:(lines: {t.Line}-{t.EndLine}/ cols: {t.Column}-{t.EndColumn})\t{t.Value}\t[{t.Type}]");
                    }
                    Console.WriteLine("[===== tokenize end ====]");
                    return PyNone.Instance;
                }

                var statements = PyParserRuntime.ParseSource(tokens, sourceCode, fileName ?? "<string>");

                if(showAst)
                {
                    Console.WriteLine("[===== ast log ====]");
                    // CPython 3.12 compatible AST dump using existing ASTDumper
                    var dumper = new ASTDumper();

                    Console.WriteLine("Module(");
                    Console.WriteLine("  body=[");
                    for (int i = 0; i < statements.Count; ++i)
                    {
                        var formattedStmt = dumper.FormatNode(statements[i], 4);
                        Console.Write(formattedStmt);
                        if (i < statements.Count - 1)
                            Console.WriteLine(",");
                        else
                            Console.WriteLine();
                    }
                    Console.WriteLine("  ],");
                    Console.WriteLine("  type_ignores=[])");

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
                var codeObject = _compiler.Compile(statements, "<module>", new List<string>(), fileName, mode);

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
            catch (PythonException pe) when (pe.PyException is PySystemExit)
            {
                // CPython 3.12: SystemExit is a BaseException - don't print traceback for clean exit
                // Reference: Python/pythonrun.c:1758-1759
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
                var tokens = PyParserRuntime.LexerSource(sourceCode, generateExtraTokens: showTokenize);

                if(showTokenize)
                {
                    Console.WriteLine("[===== tokenize log ====]");
                    for (int i = 0; i < tokens.Count; ++i)
                    {
                        var t = tokens[i];
                        Console.WriteLine($"{i}:(lines: {t.Line}-{t.EndLine}/ cols: {t.Column}-{t.EndColumn})\t{t.Value}\t[{t.Type}]");
                    }
                    Console.WriteLine("[===== tokenize end ====]");
                    return null;
                }

                var statements = PyParserRuntime.ParseSource(tokens, sourceCode, fileName ?? "<string>");

                if(showAst)
                {
                    Console.WriteLine("[===== ast log ====]");
                    var dumper = new ASTDumper();

                    Console.WriteLine("Module(");
                    Console.WriteLine("  body=[");
                    for (int i = 0; i < statements.Count; ++i)
                    {
                        var formattedStmt = dumper.FormatNode(statements[i], 4);
                        Console.Write(formattedStmt);
                        if (i < statements.Count - 1)
                            Console.WriteLine(",");
                        else
                            Console.WriteLine();
                    }
                    Console.WriteLine("  ],");
                    Console.WriteLine("  type_ignores=[])");

                    Console.WriteLine("[===== ast end ====]");
                    return null;
                }

                // 2단계: 컴파일 (AST → 바이트코드)
#if DEBUG_LOG
                Console.WriteLine("\n" + new string('=', 30));
                Console.WriteLine("2️⃣ 컴파일: AST → 바이트코드 (실행 안 함)");
                Console.WriteLine(new string('=', 30));
#endif
                var codeObject = _compiler.Compile(statements, "<module>", new List<string>(), fileName);

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

                // CPython 3.12: Write .pyc file to __pycache__
                // Reference: Lib/py_compile.py:79-173
                // Reference: Lib/importlib/_bootstrap_external.py:768-775
                if (!string.IsNullOrEmpty(fileName) && fileName != "<string>")
                {
                    try
                    {
                        var pycPath = PycFileWriter.GetPycPath(fileName);
                        PycFileWriter.WritePycFile(pycPath, codeObject, fileName);
                        Console.WriteLine($"Compiled: {fileName} -> {pycPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to write .pyc file: {ex.Message}");
                    }
                }

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
            Console.WriteLine("Traceback (most recent call last):");

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

            Console.WriteLine($"  File \"{fileName}\", line {lineNumber}, in {functionName}");

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
                Console.WriteLine($"    {trimmedLine}");

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
                    Console.WriteLine($"    {markers}");
                }
            }
            else
            {
                Console.WriteLine($"    # Source line not available (line {lineNumber})");
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

                Console.WriteLine($"  File \"{fileName}\", line {lineNumber}, in {functionName}");
                ShowSourceLineWithMarkers(fileName, lineNumber, currentFrame.CurrentColumnOffset);
            }
            else if (e is PySyntaxErrorException syntaxEx && syntaxEx.LineNumber > 0)
            {
                // SyntaxError special handling
                var fileName = syntaxEx.FileName ?? _currentFileName ?? "<stdin>";
                Console.WriteLine($"  File \"{fileName}\", line {syntaxEx.LineNumber}");

                var sourceLine = syntaxEx.SourceLine;
                if (string.IsNullOrEmpty(sourceLine) && _sourceLines != null && syntaxEx.LineNumber <= _sourceLines.Length)
                {
                    sourceLine = _sourceLines[syntaxEx.LineNumber - 1];
                }

                if (!string.IsNullOrEmpty(sourceLine))
                {
                    Console.WriteLine($"    {sourceLine}");
                    if (syntaxEx.ColumnOffset >= 0)
                    {
                        var markerLength = syntaxEx.EndColumnOffset > syntaxEx.ColumnOffset
                            ? syntaxEx.EndColumnOffset - syntaxEx.ColumnOffset : 3;
                        var marker = new string(' ', syntaxEx.ColumnOffset) + new string('^', markerLength);
                        Console.WriteLine($"    {marker}");
                    }
                }
            }
            else
            {
                // No frame info available
                Console.WriteLine($"  File \"{_currentFileName ?? "<stdin>"}\", line ?, in <module>");
                Console.WriteLine($"    # Line information not available");
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

            Console.WriteLine();

            // Print traceback for the chained exception
            if (chainedException.__traceback__ != null)
            {
                Console.WriteLine("Traceback (most recent call last):");

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
            Console.WriteLine($"{chainedException.GetTypeName()}: {chainedException.ToStr().Value}");
            Console.WriteLine();

            // Print transition message
            if (isContext)
            {
                Console.WriteLine("During handling of the above exception, another exception occurred:");
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("The above exception was the direct cause of the following exception:");
                Console.WriteLine();
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

            Console.WriteLine($"{pythonExceptionType}: {exceptionMessage}");
        }
        
        /// <summary>
        /// Extract the problematic word/token from exception message for better ^^^ marker positioning
        /// </summary>
        private string GetErrorWordFromException(Exception e)
        {
            var message = e.Message;

            // Extract variable name from NameError: "name 'variable_name' is not defined"
            if (message.Contains("not defined"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(message, @"name '([^']+)' is not defined");
                if (match.Success) return match.Groups[1].Value;
            }

            // Extract attribute name from AttributeError: "object has no attribute 'attribute_name'"
            if (message.Contains("has no attribute"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(message, @"has no attribute '([^']+)'");
                if (match.Success) return match.Groups[1].Value;
            }

            // Extract argument name from TypeError: "missing required argument: 'argument_name'"
            if (message.Contains("required argument"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(message, @"required argument: '([^']+)'");
                if (match.Success) return match.Groups[1].Value;
            }

            return "";
        }

        // 대화형 실행 (REPL 스타일)
        public void Interactive()
        {
            Console.WriteLine("🐍 Python 대화형 모드 (기존 시스템 통합)");
            Console.WriteLine("종료하려면 'exit' 입력");
            
            while (true)
            {
                Console.Write(">>> ");
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
    }

    #endregion
}