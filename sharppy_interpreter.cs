// enhanced_interpreter.cs
using System;
using System.Linq;
using System.IO;

namespace SharpPy
{
    public class PythonInterpreter
    {
        private Environment globalEnv;
        protected VirtualMachine virtualMachine;
        private bool useBytecode;
        private string currentFileName = "<string>";
        
        public PythonInterpreter(bool useBytecode = false)
        {
            globalEnv = new Environment(null, null, EnvironmentType.Global);
            globalEnv.globalEnv = globalEnv;
            Environment.SetupBuiltins(globalEnv);
            
            // __name__ = "__main__" 설정 (메인 모듈)
            globalEnv.SetVariable("__name__", new PythonString("__main__"));
            globalEnv.SetVariable("__file__", new PythonString("<stdin>"));
            
            virtualMachine = new VirtualMachine(globalEnv);
            this.useBytecode = useBytecode;
        }

        public void SetGlobalEnv(Environment env)
        {
            globalEnv = env;
            // Console.WriteLine("일단 주석처리");
            // 새로운 환경이 설정될 때 builtin이 없으면 추가
            // if (!env.HasVariable("print"))
            // {
            //     Environment.SetupBuiltins(env);
            // }
            virtualMachine = new VirtualMachine(globalEnv);
        }

        public Environment GetGlobalEnv() => globalEnv;

        public object Execute(string code, string filename = "<string>")
        {
            try
            {
                var previousFileName = currentFileName;
                currentFileName = filename;
                
                try
                {
                    var env = this.globalEnv;
                    if (useBytecode)
                    {
                        var codeObject = PythonCompiler.Compile(code, filename, "exec");
                        return virtualMachine.Execute(env, codeObject);
                    }
                    else
                    {
                        return ExecuteAST(code, filename);
                    }
                }
                finally
                {
                    currentFileName = previousFileName;
                }
            }
            catch (PythonException ex)
            {
                // 파일명 업데이트 (throw하지 않고 새 예외 객체 생성)
                if (string.IsNullOrEmpty(ex.FileName) || ex.FileName == "<string>")
                {
                    ex = new PythonException(ex.Type, ex.Message, ex.Line, ex.Column, filename);
                }
                DisplayError(ex, code, filename);
                return null;  // null 반환하여 실행 계속
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  File \"{filename}\"");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Exception type: {ex.GetType().Name}");
                
                if (ex.StackTrace != null)
                {
                    var relevantStack = ex.StackTrace.Split('\n')
                        .Where(line => line.Contains("SharpPy") || line.Contains("PythonInterpreter"))
                        .Take(3);
                    foreach (var line in relevantStack)
                    {
                        Console.WriteLine($"  {line.Trim()}");
                    }
                }
                Console.WriteLine();
                return null;  // null 반환하여 실행 계속
            }
        }

        private object ExecuteAST(string code, string filename)
        {
            var lexer = new Lexer(code);
            var tokens = lexer.Tokenize();

            var parser = new Parser(tokens);
            var ast = parser.Parse();

            object result = null;
            foreach (var statement in ast)
            {
                try
                {
                    result = statement.Evaluate(globalEnv);
                }
                catch (PythonException ex)
                {
                    // 파일명 정보만 업데이트하고 다시 throw
                    if (ex.FileName == "<string>" && filename != "<string>")
                    {
                        throw new PythonException(ex.Type, ex.Message, 
                            ex.Line > 0 ? ex.Line : statement.Line, 
                            ex.Column > 0 ? ex.Column : statement.Column, 
                            filename);
                    }
                    if (ex.Line == 0 && statement.Line > 0)
                    {
                        throw new PythonException(ex.Type, ex.Message, 
                            statement.Line, statement.Column, filename);
                    }
                    throw;  // 여기서 throw는 OK (상위 Execute에서 catch)
                }
                catch (ReturnException)
                {
                    throw;
                }
                catch (BreakException)
                {
                    throw;
                }
                catch (ContinueException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    int line = statement.Line > 0 ? statement.Line : 0;
                    int column = statement.Column > 0 ? statement.Column : 0;
                    throw new PythonException("RuntimeError", 
                        $"Internal error: {ex.Message}", line, column, filename);
                }
            }

            return result;
        }

        private void DisplayError(PythonException ex, string code, string filename)
        {
            // 실제 파일명 사용
            string displayFileName = ex.FileName != "<string>" ? ex.FileName : filename;

            if (ex.Line > 0)
            {
                Console.WriteLine($"  File \"{displayFileName}\", line {ex.Line}, column {ex.Column}");

                // 파일이 존재하면 해당 줄 표시
                if (displayFileName != "<string>" && File.Exists(displayFileName))
                {
                    try
                    {
                        var lines = File.ReadAllLines(displayFileName);
                        if (ex.Line <= lines.Length)
                        {
                            Console.WriteLine($"    {lines[ex.Line - 1]}");
                            if (ex.Column > 0)
                            {
                                var pointer = new string(' ', ex.Column - 1) + "^";
                                Console.WriteLine($"    {pointer}");
                            }
                        }
                    }
                    catch
                    {
                        // 파일 읽기 실패시 코드에서 표시
                        ShowErrorFromCode(code, ex.Line, ex.Column);
                    }
                }
                else
                {
                    // 인터랙티브 모드나 string 실행시
                    ShowErrorFromCode(code, ex.Line, ex.Column);
                }
            }
            else
            {
                Console.WriteLine($"  File \"{displayFileName}\"");
            }

            Console.WriteLine($"{ex.Type}: {ex.Message}");
            Console.WriteLine();
        }

        public virtual object CompileAndExecute(Environment env, string code, string filename = "<string>")
        {
            try
            {
                var codeObject = PythonCompiler.Compile(code, filename);
                return virtualMachine.Execute(env, codeObject);
            }
            catch (PythonException ex)
            {
                // 파일명 업데이트 (throw하지 않음)
                if (ex.FileName == "<string>" && filename != "<string>")
                {
                    ex = new PythonException(ex.Type, ex.Message, ex.Line, ex.Column, filename);
                }
                DisplayError(ex, code, filename);
                return null;  // null 반환
            }
        }

        public CodeObject Compile(string code, string filename = "<string>", string mode = "exec")
        {
            return PythonCompiler.Compile(code, filename, mode);
        }

        public void SaveBytecode(string code, string sourceFile, string outputFile)
        {
            var codeObject = PythonCompiler.Compile(code, sourceFile);
            BytecodeSerializer.SaveToFile(codeObject, outputFile);
        }

        public object LoadAndExecuteBytecode(string bytecodeFile)
        {
            var env = this.globalEnv;
            var codeObject = BytecodeSerializer.LoadFromFile(bytecodeFile);
            return virtualMachine.Execute(env, codeObject);
        }

        public void ShowBytecode(string code, string filename = "<string>")
        {
            var codeObject = PythonCompiler.Compile(code, filename);
            Console.WriteLine(Disassembler.Disassemble(codeObject));
        }

         public void ExecuteFile(string filename)
        {
            try
            {
                if (!File.Exists(filename))
                {
                    Console.WriteLine($"Error: File '{filename}' not found");
                    return;
                }

                // 파일 실행 시 __file__ 업데이트
                globalEnv.SetVariable("__file__", new PythonString(filename));
                
                if (filename.EndsWith(".pyc"))
                {
                    LoadAndExecuteBytecode(filename);
                }
                else
                {
                    string code = File.ReadAllText(filename);
                    Execute(code, filename);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading file '{filename}': {ex.Message}");
            }
        }

        private void ShowErrorFromCode(string code, int line, int column)
        {
            if (string.IsNullOrEmpty(code)) return;

            try
            {
                var lines = code.Split('\n');
                if (line > 0 && line <= lines.Length)
                {
                    Console.WriteLine($"    {lines[line - 1]}");
                    if (column > 0)
                    {
                        var pointer = new string(' ', column - 1) + "^";
                        Console.WriteLine($"    {pointer}");
                    }
                }
            }
            catch
            {
                // 무시
            }
        }

        public void StartRepl()
        {
            var modeStr = useBytecode ? " (Bytecode Mode)" : " (AST Mode)";
            Console.WriteLine($"Extended Pure C# Python Interpreter with Int/Float Types & Lambda Functions{modeStr}");
            Console.WriteLine("Type 'exit()' to quit");
            Console.WriteLine("Special commands:");
            Console.WriteLine("  __bytecode__(code) - Show bytecode for code");
            Console.WriteLine("  __mode__() - Toggle between AST and Bytecode execution");
            Console.WriteLine("Features: Variables, Functions, Classes, Inheritance, Type Hints, Slicing, Lambda, Bytecode, etc.");
            Console.WriteLine();

            int lineNumber = 1;

            while (true)
            {
                Console.Write(">>> ");
                string input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                {
                    lineNumber++;
                    continue;
                }

                if (input.Trim() == "exit()" || input.Trim() == "quit()")
                    break;

                // Special REPL commands
                if (input.Trim() == "__mode__()")
                {
                    useBytecode = !useBytecode;
                    virtualMachine = new VirtualMachine(globalEnv); // Reset VM
                    Console.WriteLine($"Switched to {(useBytecode ? "Bytecode" : "AST")} mode");
                    lineNumber++;
                    continue;
                }

                if (input.Trim().StartsWith("__bytecode__(") && input.Trim().EndsWith(")"))
                {
                    var code = input.Trim().Substring(13, input.Trim().Length - 14);
                    ShowBytecode(code.Trim('"', '\''), $"<stdin:{lineNumber}>");
                    lineNumber++;
                    continue;
                }

                try
                {
                    // For REPL, use a special filename that includes the line number
                    var result = Execute(input, $"<stdin:{lineNumber}>");
                    if (result != null && !(result is string && string.IsNullOrEmpty((string)result)))
                    {
                        // Format output nicely - distinguish int and float
                        if (result is bool b)
                            Console.WriteLine(b ? "True" : "False");
                        else if (result is int i)
                            Console.WriteLine(i);
                        else if (result is double d)
                            Console.WriteLine(d);
                        else
                            Console.WriteLine(result);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }

                lineNumber++;
            }
        }
    }
}