// environment_updated.cs - Modified Environment class to use Builtins module
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    public enum EnvironmentType
    {
        Builtin,
        Global,
        Enclosing,
        Local
    }

    public class Environment : PythonTypeObject
    {
        public Dictionary<string, PythonTypeObject> variables { get; private set; } = new Dictionary<string, PythonTypeObject>();
        public Environment parent;
        public Environment globalEnv;  // Global 환경 참조 추가
        public EnvironmentType envType;
        public List<string> SearchPaths { get; set; }

        // global/nonlocal 선언된 변수들 추적
        public HashSet<string> globalVars = new HashSet<string>();
        public HashSet<string> nonlocalVars = new HashSet<string>();

        public Environment(Environment parent = null, Environment global = null, EnvironmentType type = EnvironmentType.Local)
        {
            this.parent = parent;
            this.globalEnv = global ?? (parent?.globalEnv) ?? this;  // global 환경 상속
            this.envType = type;

            if (parent != null && parent.SearchPaths != null)
            {
                SearchPaths = parent.SearchPaths;
            }
            else
            {
                SearchPaths = StandardLibrary.GetGlobalSearchPaths();
            }
        }

        // 실제 locals() 구현 - 현재 환경의 로컬 변수만 (built-in 제외)
        public PythonDict GetLocals()
        {
            var dict = new PythonDict();
            foreach (var kvp in variables)
            {
                // __builtins__는 제외
                if (kvp.Key != "__builtins__")
                {
                    dict.Items[new PythonString(kvp.Key)] = kvp.Value;
                }
            }
            return dict;
        }


        // 실제 globals() 구현 - global 환경의 변수들
        public PythonDict GetGlobals()
        {
            var dict = new PythonDict();
            var globalVars = globalEnv?.variables ?? variables;

            foreach (var kvp in globalVars)
            {
                dict.Items[new PythonString(kvp.Key)] = kvp.Value;
            }
            return dict;
        }

        // SetupBuiltins 메서드 수정
        public static void SetupBuiltins(Environment env)
        {
            // Global 환경임을 표시
            env.envType = EnvironmentType.Global;
            env.globalEnv = env;

            // 기존 코드...
            Builtins.SetupBuiltins(env);
            SetupSpecialBuiltins(env);
        }

        // Special built-ins that need access to the environment
        private static void SetupSpecialBuiltins(Environment env)
        {
            // Override eval() with proper implementation
            env.SetVariable("eval", new BuiltinFunction("eval", args =>
            {
                if (args.Count < 1 || args.Count > 3)
                    throw new PythonException("TypeError", "eval() takes 1 to 3 arguments");

                var expression = args[0];

                // Handle both string and code object as first argument
                CodeObject codeObject = null;
                if (expression is PythonString exprStr)
                {
                    // Compile the string expression
                    codeObject = PythonCompiler.Compile(exprStr.Value, "<eval>", "eval");
                }
                else if (expression is PythonCodeObject pyCodeObj)
                {
                    // Use the pre-compiled code object
                    codeObject = pyCodeObj.Code;
                }
                else
                {
                    throw new PythonException("TypeError", "eval() first argument must be a string or code object");
                }

                // Handle globals argument - can be Environment or Dict
                Environment globals = env;
                if (args.Count > 1 && args[1] != null && !(args[1] is PythonNone))
                {
                    if (args[1] is Environment g)
                        globals = g;
                    else if (args[1] is PythonDict gDict)
                        globals = Environment.FromDict(gDict, env);
                    else
                        throw new PythonException("TypeError", "eval() globals must be a dict or environment");
                }

                // Handle locals argument - can be Environment or Dict
                Environment locals = globals;
                if (args.Count > 2 && args[2] != null && !(args[2] is PythonNone))
                {
                    if (args[2] is Environment l)
                        locals = l;
                    else if (args[2] is PythonDict lDict)
                        locals = Environment.FromDict(lDict, globals);
                    else
                        throw new PythonException("TypeError", "eval() locals must be a dict or environment");
                }

                try
                {
                    // Execute the code object directly
                    var vm = new VirtualMachine(globals);
                    return vm.Execute(locals, codeObject);
                }
                catch (Exception ex)
                {
                    throw new PythonException("SyntaxError", $"Error in eval(): {ex.Message}");
                }
            }));

            // Override exec() with proper implementation
            env.SetVariable("exec", new BuiltinFunction("exec", args =>
            {
                if (args.Count < 1 || args.Count > 3)
                    throw new PythonException("TypeError", "exec() takes 1 to 3 arguments");

                var source = args[0];
                if (!(source is PythonString sourceStr))
                    throw new PythonException("TypeError", "exec() first argument must be a string");

                // Special handling for globals() dict passed as second argument
                if (args.Count > 1 && args[1] is PythonDict gDict)
                {
                    try
                    {
                        // Execute in current environment and update the dict
                        var lexer = new Lexer(sourceStr.Value);
                        var tokens = lexer.Tokenize();
                        var parser = new Parser(tokens);
                        var ast = parser.Parse();

                        // Execute statements
                        foreach (var statement in ast)
                        {
                            statement.Evaluate(env);
                        }

                        // Update the passed dict with any new variables
                        var currentVars = env.GetAllVariables();
                        foreach (var kvp in currentVars)
                        {
                            gDict.Items[new PythonString(kvp.Key)] = kvp.Value;
                        }

                        return PythonNone.Instance;
                    }
                    catch (Exception ex)
                    {
                        throw new PythonException("SyntaxError", $"Error in exec(): {ex.Message}");
                    }
                }

                // Handle globals argument - can be Environment or Dict
                Environment globals = env;
                if (args.Count > 1 && args[1] != null && !(args[1] is PythonNone))
                {
                    if (args[1] is Environment g)
                        globals = g;
                    else
                        throw new PythonException("TypeError", "exec() globals must be a dict or environment");
                }

                // Handle locals argument - can be Environment or Dict
                Environment locals = globals;
                if (args.Count > 2 && args[2] != null && !(args[2] is PythonNone))
                {
                    if (args[2] is Environment l)
                        locals = l;
                    else if (args[2] is PythonDict lDict)
                    {
                        locals = Environment.FromDict(lDict, globals);
                    }
                    else
                        throw new PythonException("TypeError", "exec() locals must be a dict or environment");
                }

                try
                {
                    PythonCompiler.Exec(sourceStr.Value, globals, locals);
                    return PythonNone.Instance;
                }
                catch (Exception ex)
                {
                    throw new PythonException("SyntaxError", $"Error in exec(): {ex.Message}");
                }
            }));

            // Override compile() with proper implementation
            env.SetVariable("compile", new BuiltinFunction("compile", args =>
            {
                if (args.Count != 3)
                    throw new PythonException("TypeError", "compile() takes exactly 3 arguments");

                var source = args[0];
                var filename = args[1];
                var mode = args[2];

                if (!(source is PythonString sourceStr))
                    throw new PythonException("TypeError", "compile() first argument must be a string");
                if (!(filename is PythonString filenameStr))
                    throw new PythonException("TypeError", "compile() second argument must be a string");
                if (!(mode is PythonString modeStr))
                    throw new PythonException("TypeError", "compile() third argument must be a string");

                if (modeStr.Value != "exec" && modeStr.Value != "eval")
                    throw new PythonException("ValueError", "compile() mode must be 'exec' or 'eval'");

                try
                {
                    var codeObject = PythonCompiler.Compile(sourceStr.Value, filenameStr.Value, modeStr.Value);
                    return new PythonCodeObject(codeObject);
                }
                catch (Exception ex)
                {
                    throw new PythonException("SyntaxError", $"Error in compile(): {ex.Message}");
                }
            }));

            env.SetVariable("globals", new BuiltinFunction("globals", (currentEnv, args) =>
            {
                if (args.Count != 0)
                    throw new PythonException("TypeError", "globals() takes no arguments");

                return currentEnv.GetGlobals();
            }));

            // Override locals() to use current environment  
            // locals() - 환경을 받는 버전으로 수정  
            env.SetVariable("locals", new BuiltinFunction("locals", (currentEnv, args) =>
            {
                if (args.Count != 0)
                    throw new PythonException("TypeError", "locals() takes no arguments");

                return currentEnv.GetLocals();
            }));

            // Override dir() to use current environment when no args
            env.SetVariable("dir", new BuiltinFunction("dir", (currentEnv, args) =>
            {
                if (args.Count > 1)
                    throw new PythonException("TypeError", "dir() takes at most 1 argument");

                var result = new PythonList();

                if (args.Count == 0)
                {
                    // No arguments - return names in current scope (locals only)
                    var localVars = currentEnv.GetLocals();
                    var names = localVars.Items.Keys
                        .Select(k => (k as PythonString)?.Value)
                        .Where(n => n != null)
                        .OrderBy(n => n)
                        .ToList();

                    foreach (var name in names)
                    {
                        result.Items.Add(new PythonString(name));
                    }
                    return result;
                }

                // With argument - use the existing logic
                var obj = args[0];
                // ... 기존 dir() 로직 ...
                var builtinDir = Builtins.GetBuiltinsModule().GetAttribute("dir") as BuiltinFunction;
                return builtinDir?.Call(args) ?? result;

                // return result; ???
            }));
        }

        public void SetVariable(string name, PythonTypeObject value)
        {
            // global 선언된 변수는 global 환경에 설정
            if (globalVars.Contains(name))
            {
                if (globalEnv != null && globalEnv != this)
                {
                    globalEnv.variables[name] = value;
                    return;
                }
            }

            // nonlocal 선언된 변수는 enclosing 환경에서 찾아서 설정
            if (nonlocalVars.Contains(name))
            {
                Environment enclosing = parent;
                while (enclosing != null && enclosing.envType != EnvironmentType.Global)
                {
                    if (enclosing.variables.ContainsKey(name))
                    {
                        enclosing.variables[name] = value;
                        return;
                    }
                    enclosing = enclosing.parent;
                }
                throw new PythonException("SyntaxError", $"no binding for nonlocal '{name}' found");
            }

            // 일반 변수는 현재 환경에 설정
            variables[name] = value;
        }

        public PythonTypeObject GetVariable(string name)
        {
            // global 선언된 변수는 global 환경에서 직접 가져옴
            if (globalVars.Contains(name))
            {
                if (globalEnv != null && globalEnv != this && globalEnv.variables.ContainsKey(name))
                {
                    return globalEnv.variables[name];
                }
            }

            // LEGB 순서로 탐색
            // 1. Local
            if (variables.ContainsKey(name))
                return variables[name];

            // 2. Enclosing (parent가 global이 아닌 경우만)
            Environment current = parent;
            while (current != null && current.envType != EnvironmentType.Global)
            {
                if (current.variables.ContainsKey(name))
                    return current.variables[name];
                current = current.parent;
            }

            // 3. Global
            if (globalEnv != null && globalEnv != this && globalEnv.variables.ContainsKey(name))
                return globalEnv.variables[name];

            // 4. Built-in
            if (globalEnv != null && globalEnv.variables.ContainsKey("__builtins__"))
            {
                var builtinsModule = globalEnv.variables["__builtins__"];
                if (builtinsModule is PythonModule module)
                {
                    try
                    {
                        return module.GetAttribute(name);
                    }
                    catch (PythonException) { }
                }
            }

            throw new PythonException("NameError", $"Name '{name}' is not defined");
        }

        // Rest of the Environment class methods remain unchanged
        public bool HasLocalVariable(string name) => variables.ContainsKey(name);

        public PythonTypeObject GetLocalVariable(string name)
        {
            if (variables.ContainsKey(name))
                return variables[name];
            throw new PythonException("NameError", $"Name '{name}' is not defined");
        }

        public PythonTypeObject GetClassVariable(string name)
        {
            if (variables.ContainsKey(name))
                return variables[name];
            throw new PythonException("NameError", $"Name '{name}' is not defined");
        }

        public static Environment FromDict(PythonDict dict, Environment parent = null)
        {
            var env = new Environment(parent);
            foreach (var kvp in dict.Items)
            {
                if (kvp.Key is PythonString keyStr)
                {
                    env.SetVariable(keyStr.Value, kvp.Value);
                }
            }
            return env;
        }

        public PythonDict ToDict()
        {
            var dict = new PythonDict();
            var allVars = GetAllVariables();
            foreach (var kvp in allVars)
            {
                dict.Items[new PythonString(kvp.Key)] = kvp.Value;
            }
            return dict;
        }


        public void DeleteVariable(string name)
        {
            if (variables.ContainsKey(name))
            {
                variables.Remove(name);
            }
            else if (parent != null)
            {
                parent.DeleteVariable(name);
            }
            else
            {
                throw new PythonException("NameError", $"Name '{name}' is not defined");
            }
        }

        public bool HasVariable(string name)
        {
            // 1. 현재 환경 확인
            if (variables.ContainsKey(name))
                return true;

            // 2. 부모 환경 확인
            if (parent != null)
                return parent.HasVariable(name);

            // 3. __builtins__ 확인 (새로 추가)
            if (parent == null)  // Global environment
            {
                if (variables.ContainsKey("__builtins__"))
                {
                    var builtinsModule = variables["__builtins__"];
                    if (builtinsModule is PythonModule module)
                    {
                        try
                        {
                            module.GetAttribute(name);
                            return true;
                        }
                        catch (PythonException)
                        {
                            return false;
                        }
                    }
                }
            }

            return false;
        }

        public Dictionary<string, PythonTypeObject> GetAllVariables()
        {
            var result = new Dictionary<string, PythonTypeObject>();
            if (parent != null)
            {
                foreach (var kvp in parent.GetAllVariables())
                    result[kvp.Key] = kvp.Value;
            }
            foreach (var kvp in variables)
            {
                // __builtins__ 모듈 자체는 포함하되, 그 내용은 포함하지 않음
                result[kvp.Key] = kvp.Value;
            }
            return result;
        }
        
        public override PythonType Type => PythonType.Environment;
        public override bool IsTrue() => true;
        public override string ToPythonString() => $"<environment at {GetHashCode():X}>";
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
        public override object GetRawValue() => this;
    }
}