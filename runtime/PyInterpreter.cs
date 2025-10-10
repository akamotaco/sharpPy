namespace SharpPy
{
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
            
            // CPython 3.12 호환: 모듈 생성시 __name__ 설정
            // PyModule_New()에서 __name__을 모듈 딕셔너리에 설정하는 것과 동일
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
            return Execute(sourceCode, null);
        }
        
        public PyObject Execute(string sourceCode, string fileName)
        {
            // Store filename and source lines for Python-like error reporting
            _currentFileName = fileName;
            _sourceLines = sourceCode.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            
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
                var statements = GeneratedParserBridge.ParseSource(sourceCode, fileName ?? "<string>");
                
                // 2단계: 컴파일 (AST → 바이트코드)
#if DEBUG_LOG
                Console.WriteLine("\n" + new string('=', 30));
                Console.WriteLine("2️⃣ 컴파일: AST → 바이트코드");
                Console.WriteLine(new string('=', 30));
#endif
                var codeObject = _compiler.Compile(statements, "<module>", new List<string>(), fileName);
                
#if DEBUG_LOG
                Console.WriteLine($"🔍 컴파일 직후 Exception Table entries: {codeObject.ExceptionTable.Count}");
#endif
                
                // 3단계: 바이트코드 확인 (ShowBytecode 또는 VerboseMode일 때)
                if (SharpPyConfig.ShowBytecode)
                {
                    codeObject.Disassemble();
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
        /// </summary>
        private void PrintPythonStyleTraceback(Exception e)
        {
            Console.WriteLine("Traceback (most recent call last):");
            
            // Get current execution frame from VM
            var currentFrame = PyVM.CurrentFrame;
            
            if (currentFrame != null && currentFrame.CurrentLineNumber > 0)
            {
                var fileName = _currentFileName ?? "<stdin>";
                var functionName = currentFrame.Code.Name ?? "<module>";
                var lineNumber = currentFrame.CurrentLineNumber;
                
                Console.WriteLine($"  File \"{fileName}\", line {lineNumber}, in {functionName}");
                
                // Show actual source line if available
                if (_sourceLines != null && lineNumber > 0 && lineNumber <= _sourceLines.Length)
                {
                    var sourceLine = _sourceLines[lineNumber - 1].Trim(); // Convert to 0-based index
                    Console.WriteLine($"    {sourceLine}");
                    
                    // Add ^^^ markers if column information is available
                    if (currentFrame.CurrentColumnOffset >= 0)
                    {
                        var leadingSpaces = _sourceLines[lineNumber - 1].Length - sourceLine.Length; // Account for trimmed whitespace
                        var adjustedColumn = Math.Max(0, currentFrame.CurrentColumnOffset - leadingSpaces);
                        var markers = new string(' ', Math.Min(adjustedColumn, sourceLine.Length)) + "^";
                        
                        // Extend markers if we can identify the token length
                        var errorWord = GetErrorWordFromException(e);
                        if (!string.IsNullOrEmpty(errorWord) && sourceLine.Contains(errorWord))
                        {
                            var wordIndex = sourceLine.IndexOf(errorWord);
                            if (wordIndex >= 0 && Math.Abs(wordIndex - adjustedColumn) <= 5) // Close enough
                            {
                                markers = new string(' ', wordIndex) + new string('^', errorWord.Length);
                            }
                        }
                        
                        Console.WriteLine($"    {markers}");
                    }
                }
                else
                {
                    Console.WriteLine($"    # Source line not available (line {lineNumber})");
                }
            }
            else
            {
                // CPython 3.12: Handle PySyntaxErrorException with detailed location info
                if (e is PySyntaxErrorException syntaxEx && syntaxEx.LineNumber > 0)
                {
                    var fileName = syntaxEx.FileName ?? _currentFileName ?? "<stdin>";
                    Console.WriteLine($"  File \"{fileName}\", line {syntaxEx.LineNumber}");

                    // Show source line from exception if available, otherwise from _sourceLines
                    var sourceLine = syntaxEx.SourceLine;
                    if (string.IsNullOrEmpty(sourceLine) && _sourceLines != null && syntaxEx.LineNumber <= _sourceLines.Length)
                    {
                        sourceLine = _sourceLines[syntaxEx.LineNumber - 1];
                    }

                    if (!string.IsNullOrEmpty(sourceLine))
                    {
                        Console.WriteLine($"    {sourceLine}");

                        // Show error marker (^^^)
                        if (syntaxEx.ColumnOffset >= 0)
                        {
                            var markerStart = Math.Max(0, syntaxEx.ColumnOffset);
                            var markerLength = syntaxEx.EndColumnOffset > syntaxEx.ColumnOffset
                                ? syntaxEx.EndColumnOffset - syntaxEx.ColumnOffset
                                : 3;
                            var marker = new string(' ', markerStart) + new string('^', markerLength);
                            Console.WriteLine($"    {marker}");
                        }
                    }
                }
                else
                {
                    // Try to get line number from PythonException
                    var lineNumber = -1;
                    if (e is PythonException pythonEx && pythonEx.LineNumber > 0)
                    {
                        lineNumber = pythonEx.LineNumber;
                    }

                    var fileName = _currentFileName ?? "<stdin>";
                    if (lineNumber > 0)
                    {
                        Console.WriteLine($"  File \"{fileName}\", line {lineNumber}, in <module>");

                        // Show actual source line if available
                        if (_sourceLines != null && lineNumber > 0 && lineNumber <= _sourceLines.Length)
                        {
                            var sourceLine = _sourceLines[lineNumber - 1].Trim();
                            Console.WriteLine($"    {sourceLine}");
                        }
                        else
                        {
                            Console.WriteLine($"    # Source line not available (line {lineNumber})");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  File \"{fileName}\", line ?, in <module>");
                        Console.WriteLine($"    # Line information not available");
                    }
                }
            }
            
            // Show the exception type and message (Python-style)
            // CPython 3.12 호환: PythonException에서 실제 Python 예외 타입 가져오기
            string pythonExceptionType;
            string exceptionMessage;

            if (e is PythonException pyEx && pyEx.PyException != null)
            {
                // PythonException인 경우: 내부 PyException의 GetTypeName() 사용
                pythonExceptionType = pyEx.PyException.GetTypeName();
                exceptionMessage = pyEx.PyException.ToStr();
            }
            else
            {
                // 다른 C# 예외인 경우: 기존 로직 사용
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
                    "PySyntaxErrorException" => "SyntaxError",  // CPython 3.12: Parser syntax error
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