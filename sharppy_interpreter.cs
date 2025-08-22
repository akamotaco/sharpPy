// interpreter_with_builtin_sys.cs
using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;

#if GODOT
using Godot_IO;
#else
using DotNet_IO;
#endif

namespace SharpPy
{
    public class PythonInterpreter
    {
        private Environment globalEnv;
        protected VirtualMachine virtualMachine;
        private bool useBytecode;
        private string currentFileName = "<string>";

        private Dictionary<string, PythonModule> builtinModules;

        // sys.path에 대한 직접 참조 (편의를 위해)
        public SysModuleInstance SysModule { get; private set; }  // SysModuleInstance 타입으로 변경

        public PythonInterpreter(bool useBytecode = false)
        {
            globalEnv = new Environment(null, null, EnvironmentType.Global);
            globalEnv.globalEnv = globalEnv;

            // ✅ 환경에 인터프리터 연결
            globalEnv.Interpreter = this;

            var initialPaths = InitializeSearchPaths();
            
            // 2. sys 모듈을 특별하게 생성 (SysModuleInstance 사용)
            SysModule = new SysModuleInstance(this, initialPaths);

            // ✅ 추가: ModuleSystem에 sys 등록
            ModuleSystem.RegisterBuiltinModule("sys", SysModule);
            
            // 4. 내장 모듈 레지스트리 초기화
            builtinModules = new Dictionary<string, PythonModule>
            {
                ["sys"] = SysModule,
                // 다른 항상 사용 가능한 모듈들 추가 가능
                // ["builtins"] = ...,
            };

            // 5. sys.modules 초기화
            SysModule.RegisterModule("sys", SysModule);
            SysModule.RegisterModule("__main__", new PythonModule("__main__", initialPaths));

            // 6. builtins 설정 (sys 제외)
            Environment.SetupBuiltins(globalEnv);

            // 7. 메인 모듈 설정
            globalEnv.SetVariable("__name__", new PythonString("__main__"));
            globalEnv.SetVariable("__file__", new PythonString("<stdin>"));

            virtualMachine = new VirtualMachine(globalEnv);
            this.useBytecode = useBytecode;
        }

        public Environment CreateEnvironment(Environment parent = null)
        {
            var env = new Environment(parent ?? globalEnv);
            env.Interpreter = this;  // 인터프리터 연결
            return env;
        }

        private List<string> InitializeSearchPaths()
        {
            var paths = new List<string>();

            // 현재 디렉토리
            paths.Add(".");

            // 실행 파일 디렉토리의 lib
            string exeDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(exeDir))
            {
                paths.Add(Path.Combine(exeDir, "lib"));
                paths.Add(Path.Combine(exeDir, "lib", "python"));
                paths.Add(Path.Combine(exeDir, "site-packages"));
            }

            // PYTHONPATH 환경 변수
            string pythonPath = System.Environment.GetEnvironmentVariable("PYTHONPATH");
            if (!string.IsNullOrEmpty(pythonPath))
            {
                var envPaths = pythonPath.Split(Path.PathSeparator);
                foreach (var path in envPaths)
                {
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        paths.Add(path.Trim());
                    }
                }
            }

            return paths;
        }

        public void SetGlobalEnv(Environment env)
        {
            globalEnv = env;

            // sys 모듈이 없으면 내장 모듈로 제공 (import를 통해서만 접근 가능)
            // 직접 설정하지 않음

            virtualMachine = new VirtualMachine(globalEnv);
        }

        public Environment GetGlobalEnv() => globalEnv;

        public object Execute(string code, string filename = "<string>")
        {
            try
            {
                var previousFileName = currentFileName;
                currentFileName = filename;

                // 글로벌 환경에 파일 정보 설정
                globalEnv.CurrentFileName = filename;

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
                // sys.exit() 처리
                if (ex.Type == "SystemExit")
                {
                    Console.WriteLine($"SystemExit: {ex.Message}");
                    return null;
                }

                DisplayError(ex, code, filename);
                return null;
            }
        }

        public void ExecuteFile(string filename)
        {
            try
            {
                if (!Helper.FileExists(filename))
                {
                    Console.WriteLine($"Error: File '{filename}' not found");
                    return;
                }

                // 파일 실행 시 환경 설정
                globalEnv.SetVariable("__file__", new PythonString(filename));
                globalEnv.CurrentFileName = filename;

                // 파일이 있는 디렉토리를 sys.path 맨 앞에 추가
                string fileDir = Path.GetDirectoryName(Path.GetFullPath(filename));
                if (!string.IsNullOrEmpty(fileDir))
                {
                    var sysPath = SysModule?.GetAttribute("path");
                    if (sysPath is SysPathList pathList)
                    {
                        // insert(0, path) 호출
                        var insertMethod = pathList.GetMethod("insert");
                        if (insertMethod != null)
                        {
                            insertMethod.Call(new List<PythonTypeObject>
                            {
                                PythonInt.Create(0),
                                new PythonString(fileDir)
                            });
                        }
                    }
                }

                if (filename.EndsWith(".pyc"))
                {
                    LoadAndExecuteBytecode(filename);
                }
                else
                {
                    string code = Helper.ReadAllText(filename);
                    Execute(code, filename);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading file '{filename}': {ex.Message}");
            }
        }

        // sys 모듈에 대한 특별한 접근자
        public SysModuleInstance GetSysModule()
        {
            // import sys를 하지 않아도 내부적으로 sys 모듈 참조 가능
            return SysModule;
        }

        // sys.path에 경로 추가 (편의 메서드)
        public void AddToSysPath(string path)
        {
            var sysPath = SysModule?.GetAttribute("path");
            if (sysPath is SysPathList pathList)
            {
                var appendMethod = pathList.GetMethod("append");
                if (appendMethod != null)
                {
                    appendMethod.Call(new List<PythonTypeObject> { new PythonString(path) });
                }
            }
        }

        // 나머지 메서드들 (ExecuteAST, DisplayError, StartRepl 등)은 기존과 동일...

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
                    throw;
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
            string displayFileName = ex.FileName != "<string>" ? ex.FileName : filename;

            if (ex.Line > 0)
            {
                Console.WriteLine($"  File \"{displayFileName}\", line {ex.Line}, column {ex.Column}");

                if (displayFileName != "<string>" && Helper.FileExists(displayFileName))
                {
                    try
                    {
                        var lines = Helper.ReadAllText(displayFileName);
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
                        ShowErrorFromCode(code, ex.Line, ex.Column);
                    }
                }
                else
                {
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

        // 나머지 메서드들...
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

        public virtual object CompileAndExecute(Environment env, string code, string filename = "<string>")
        {
            try
            {
                var codeObject = PythonCompiler.Compile(code, filename);
                return virtualMachine.Execute(env, codeObject);
            }
            catch (PythonException ex)
            {
                if (ex.FileName == "<string>" && filename != "<string>")
                {
                    ex = new PythonException(ex.Type, ex.Message, ex.Line, ex.Column, filename);
                }
                DisplayError(ex, code, filename);
                return null;
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