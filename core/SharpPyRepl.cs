using System;
using System.Text;
using System.Threading;

namespace SharpPy.Core
{
    /// <summary>
    /// SharpPy REPL (Read-Eval-Print Loop) - Python-style interactive shell
    /// </summary>
    public class SharpPyRepl
    {
        private readonly IntegratedPythonInterpreter _interpreter;
        private bool _isRunning;
        private StringBuilder _multiLineBuffer;
        private bool _inMultiLineMode;
        private bool _keyboardInterruptOccurred;
        private readonly ManualResetEventSlim _interruptEvent;

        public SharpPyRepl()
        {
            _interpreter = new IntegratedPythonInterpreter();
            _multiLineBuffer = new StringBuilder();
            _isRunning = false;
            _inMultiLineMode = false;
            _keyboardInterruptOccurred = false;
            _interruptEvent = new ManualResetEventSlim(false);
        }

        public void Start()
        {
            PrintWelcomeMessage();
            _isRunning = true;

            // CPython 3.12: Register Ctrl+C handler for KeyboardInterrupt
            // Reference: Python/pythonrun.c:1758-1759
            Console.CancelKeyPress += OnCancelKeyPress;

            while (_isRunning)
            {
                try
                {
                    string input = GetUserInput();

                    if (string.IsNullOrEmpty(input))
                    {
                        if (_inMultiLineMode)
                        {
                            // Empty line in multi-line mode - execute accumulated code
                            ExecuteMultiLineCode();
                        }
                        continue;
                    }

                    if (IsExitCommand(input))
                    {
                        break;
                    }

                    if (IsHelpCommand(input))
                    {
                        ShowHelp();
                        continue;
                    }

                    ProcessInput(input);
                }
                // CPython 3.12: Catch OperationCanceledException from Console.ReadLine() during Ctrl+C
                // This exception is thrown by .NET when CancelKeyPress event is triggered
                // Reference: Python/pythonrun.c:1758-1759, Modules/main.c:670-693
                catch (OperationCanceledException)
                {
                    // Already handled by OnCancelKeyPress, just continue to next prompt
                    continue;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 오류: {ex.Message}");
                    ResetMultiLineMode();
                }
            }

            // Unregister handler
            Console.CancelKeyPress -= OnCancelKeyPress;

            Console.WriteLine("👋 SharpPy를 사용해주셔서 감사합니다!");
        }

        /// <summary>
        /// Handle Ctrl+C (SIGINT) and raise KeyboardInterrupt
        /// CPython 3.12: Python/pythonrun.c:1758-1759 (run_eval_code_obj)
        /// CPython 3.12: Modules/main.c:670-693 (pymain_run_python)
        /// CPython 3.12: Parser/myreadline.c:147-167 (Ctrl+C handling in ReadConsoleW)
        /// </summary>
        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            // CPython 3.12: Prevent process termination, handle as KeyboardInterrupt
            e.Cancel = true;

            // Set flag to distinguish Ctrl+C from EOF
            _keyboardInterruptOccurred = true;

            // Signal the event so GetUserInput can wake up
            _interruptEvent.Set();

            // Print newline for clean prompt
            Console.WriteLine();

            // Reset multi-line mode
            ResetMultiLineMode();

            // CPython 3.12: In interactive mode, Ctrl+C just interrupts current line
            // Reference: Python/pythonrun.c:1758-1759
            Console.WriteLine("KeyboardInterrupt");
        }

        private void PrintWelcomeMessage()
        {
            Console.WriteLine("🐍 SharpPy - Python Interpreter in C#");
            Console.WriteLine("=====================================");
            Console.WriteLine("SharpPy 0.9.2 on Windows (.NET)");
            Console.WriteLine("Type \"help\", \"copyright\", \"credits\" or \"license\" for more information.");
            Console.WriteLine("Type \"exit()\" or \"quit()\" to exit, Ctrl+C to interrupt.");
            Console.WriteLine();
        }

        private string GetUserInput()
        {
            if (_inMultiLineMode)
            {
                Console.Write("... ");
            }
            else
            {
                Console.Write(">>> ");
            }

            // Reset event before reading
            _interruptEvent.Reset();

            // CPython 3.12: Parser/myreadline.c:147-167
            // Read input from console
            string? input = Console.ReadLine();

            // CPython 3.12: Parser/myreadline.c:157
            // Wait for interrupt event with timeout (like CPython's 100ms wait)
            // This handles the race condition where CancelKeyPress fires after ReadLine returns
            if (string.IsNullOrEmpty(input))
            {
                _interruptEvent.Wait(100);
            }

            // CPython 3.12: Parser/myreadline.c:150-167
            // On Windows, ReadConsoleW returns empty when Ctrl+C occurs
            // Distinguish between Ctrl+C (interrupt) and real EOF
            if (input == null || (string.IsNullOrEmpty(input) && _keyboardInterruptOccurred))
            {
                if (_keyboardInterruptOccurred)
                {
                    // Ctrl+C - don't exit, just reset flag and return empty
                    _keyboardInterruptOccurred = false;
                    return "";
                }
                else if (input == null)
                {
                    // Real EOF (Ctrl+D or pipe end) - exit REPL
                    _isRunning = false;
                    return "";
                }
            }

            return input ?? "";
        }

        private bool IsExitCommand(string input)
        {
            string trimmed = input.Trim().TrimEnd('(', ')');
            return trimmed == "exit" || trimmed == "quit";
        }

        private bool IsHelpCommand(string input)
        {
            string trimmed = input.Trim().TrimEnd('(', ')');
            return trimmed == "help" || trimmed == "copyright" || 
                   trimmed == "credits" || trimmed == "license";
        }

        private void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("🐍 SharpPy Help");
            Console.WriteLine("===============");
            Console.WriteLine("SharpPy는 C#으로 구현된 Python 3.12 호환 인터프리터입니다.");
            Console.WriteLine();
            Console.WriteLine("지원하는 기능:");
            Console.WriteLine("• 기본 Python 문법 (변수, 함수, 클래스, 제어문)");
            Console.WriteLine("• Python 3.12 PEP 기능 (Enhanced F-strings, Type Parameters 등)");
            Console.WriteLine("• 표준 라이브러리 (math, json, datetime, re 등)");
            Console.WriteLine("• 고급 기능 (클로저, 제너레이터, 데코레이터)");
            Console.WriteLine();
            Console.WriteLine("명령어:");
            Console.WriteLine("• exit() 또는 quit() - REPL 종료");
            Console.WriteLine("• help - 이 도움말 표시");
            Console.WriteLine("• Ctrl+C - 현재 입력 중단");
            Console.WriteLine();
            Console.WriteLine("예제:");
            Console.WriteLine(">>> 2 + 3");
            Console.WriteLine("5");
            Console.WriteLine(">>> name = \"World\"");
            Console.WriteLine(">>> f\"Hello {name}!\"");
            Console.WriteLine("'Hello World!'");
            Console.WriteLine();
        }

        private void ProcessInput(string input)
        {
            // Check if this line should start or continue multi-line mode
            if (ShouldEnterMultiLineMode(input))
            {
                _inMultiLineMode = true;
                _multiLineBuffer.AppendLine(input);
                return;
            }

            if (_inMultiLineMode)
            {
                _multiLineBuffer.AppendLine(input);
                
                // Check if we should continue multi-line mode
                if (ShouldContinueMultiLineMode(input))
                {
                    return;
                }
                
                // Execute accumulated multi-line code
                ExecuteMultiLineCode();
            }
            else
            {
                // Single line execution
                ExecuteSingleLine(input);
            }
        }

        private bool ShouldEnterMultiLineMode(string input)
        {
            string trimmed = input.Trim();
            
            // Function definition, class definition, control structures
            if (trimmed.StartsWith("def ") || trimmed.StartsWith("class ") ||
                trimmed.StartsWith("if ") || trimmed.StartsWith("elif ") ||
                trimmed.StartsWith("else:") || trimmed.StartsWith("for ") ||
                trimmed.StartsWith("while ") || trimmed.StartsWith("try:") ||
                trimmed.StartsWith("except") || trimmed.StartsWith("finally:") ||
                trimmed.StartsWith("with "))
            {
                return true;
            }

            // Check for unmatched brackets, parentheses, or quotes
            return HasUnmatchedDelimiters(input);
        }

        private bool ShouldContinueMultiLineMode(string input)
        {
            // Continue if line is indented or empty
            if (string.IsNullOrWhiteSpace(input) || input.StartsWith(" ") || input.StartsWith("\t"))
            {
                return true;
            }

            // Check if overall buffer has unmatched delimiters
            string fullCode = _multiLineBuffer.ToString();
            return HasUnmatchedDelimiters(fullCode);
        }

        private bool HasUnmatchedDelimiters(string code)
        {
            int parentheses = 0, brackets = 0, braces = 0;
            bool inString = false;
            char stringChar = '\0';
            
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                
                if (!inString)
                {
                    if (c == '"' || c == '\'')
                    {
                        inString = true;
                        stringChar = c;
                    }
                    else if (c == '(') parentheses++;
                    else if (c == ')') parentheses--;
                    else if (c == '[') brackets++;
                    else if (c == ']') brackets--;
                    else if (c == '{') braces++;
                    else if (c == '}') braces--;
                }
                else
                {
                    if (c == stringChar && (i == 0 || code[i-1] != '\\'))
                    {
                        inString = false;
                    }
                }
            }

            return parentheses > 0 || brackets > 0 || braces > 0 || inString;
        }

        private void ExecuteMultiLineCode()
        {
            string code = _multiLineBuffer.ToString().Trim();
            if (!string.IsNullOrEmpty(code))
            {
                ExecuteCode(code, false);
            }
            ResetMultiLineMode();
        }

        private void ExecuteSingleLine(string input)
        {
            ExecuteCode(input.Trim(), true);
        }

        private void ExecuteCode(string code, bool isSingleLine)
        {
            try
            {
                // CPython 3.12: REPL uses Py_single_input mode
                // Reference: Python/pythonrun.c:266 - uses Py_single_input for REPL
                // Reference: Include/compile.h:8 - #define Py_single_input 256
                var result = _interpreter.ExecuteSingle(code);

                // CPython 3.12: In single mode, non-None expression results are auto-printed
                // This is handled by the interactive flag in the compiler
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 실행 오류: {ex.Message}");
#if DEBUG
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
#endif
            }
        }

        private bool IsAssignmentOrStatement(string code)
        {
            string trimmed = code.Trim();
            
            // Check for assignments
            if (trimmed.Contains("=") && !trimmed.Contains("==") && !trimmed.Contains("!=") &&
                !trimmed.Contains("<=") && !trimmed.Contains(">="))
            {
                return true;
            }

            // Check for statements (keywords that don't return values)
            return trimmed.StartsWith("def ") || trimmed.StartsWith("class ") ||
                   trimmed.StartsWith("import ") || trimmed.StartsWith("from ") ||
                   trimmed.StartsWith("print(") || trimmed.StartsWith("del ") ||
                   trimmed.StartsWith("global ") || trimmed.StartsWith("nonlocal ");
        }

        private string FormatResult(PyObject result)
        {
            if (result == null)
                return "None";
                
            return result.ToString();
        }

        private void ResetMultiLineMode()
        {
            _inMultiLineMode = false;
            _multiLineBuffer.Clear();
        }
    }
    
    // Extension method for quiet execution
    public static class InterpreterExtensions
    {
        public static PyObject ExecuteQuiet(this IntegratedPythonInterpreter interpreter, string code)
        {
            // For REPL, we still want to see the actual Python print() output,
            // but suppress the verbose compilation/execution messages
            var originalOut = Console.Out;
            var outputBuffer = new System.IO.StringWriter();
            
            try
            {
                // Capture all output first
                Console.SetOut(outputBuffer);
                var result = interpreter.Execute(code);
                
                // Restore original output
                Console.SetOut(originalOut);
                
                // Extract only the Python print() outputs (lines without prefixes like 🔍, 📝, etc.)
                string fullOutput = outputBuffer.ToString();
                var lines = fullOutput.Split('\n');
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    // Show lines that are actual Python output (not debug messages)
                    if (!string.IsNullOrEmpty(trimmedLine) &&
                        !trimmedLine.StartsWith("🐍") && !trimmedLine.StartsWith("📄") &&
                        !trimmedLine.StartsWith("🏗️") && !trimmedLine.StartsWith("======") &&
                        !trimmedLine.StartsWith("📝") && !trimmedLine.StartsWith("🔍") &&
                        !trimmedLine.StartsWith("✅") && !trimmedLine.StartsWith("🔧") &&
                        !trimmedLine.StartsWith("→") && !trimmedLine.StartsWith("🚀") &&
                        !trimmedLine.StartsWith("📁") && !trimmedLine.StartsWith("🔗") &&
                        !trimmedLine.StartsWith("🆕") && !trimmedLine.StartsWith("소스:") &&
                        !trimmedLine.Contains("스택:") && !trimmedLine.Contains("VM 완료") &&
                        !trimmedLine.StartsWith("🎉"))
                    {
                        Console.WriteLine(line);
                    }
                }
                
                return result;
            }
            catch
            {
                Console.SetOut(originalOut);
                throw;
            }
        }
    }
}