// enhanced_environment_complete.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace SharpPy
{
    // Enhanced Environment Class with PythonTypeObject
    public class Environment : PythonTypeObject
    {
        public Dictionary<string, PythonTypeObject> variables { get; private set; } = new Dictionary<string, PythonTypeObject>();
        public Environment parent;
        public List<string> SearchPaths { get; set; }

        public override PythonType Type => PythonType.Environment;

        public Environment(Environment parent = null)
        {
            this.parent = parent;
            
            if (parent != null && parent.SearchPaths != null)
            {
                SearchPaths = parent.SearchPaths; // 복사 대신 참조 공유
            }
            else
            {
                SearchPaths = StandardLibrary.GetGlobalSearchPaths(); // 전역 참조 사용
            }
        }

        public bool HasLocalVariable(string name) => variables.ContainsKey(name);

        public PythonTypeObject GetLocalVariable(string name)
        {
            if (variables.ContainsKey(name))
                return variables[name];
            throw new PythonException("NameError", $"Name '{name}' is not defined");
        }

        // 클래스 환경에서만 변수를 찾는 메서드 (부모 클래스 포함)
        public PythonTypeObject GetClassVariable(string name)
        {
            if (variables.ContainsKey(name))
                return variables[name];
            // 부모 클래스 환경만 검색, 전역은 제외
            // 이 메서드는 클래스 계층만 탐색함
            throw new PythonException("NameError", $"Name '{name}' is not defined");
        }

        // Create Environment from PythonDict
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

        // Convert Environment to PythonDict
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

        public static void SetupBuiltins(Environment env)
        {
            env.SetVariable("print", new BuiltinFunction("print", args =>
            {
                var output = string.Join(" ", args.Select(arg =>
                {
                    if (arg is PythonNone) return "None";
                    if (arg is PythonString s) return s.Value;
                    return arg.ToPythonString();
                }));

                Console.WriteLine(output);
                return PythonNone.Instance;
            }));

            env.SetVariable("len", new BuiltinFunction("len", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "len() takes exactly one argument");
                var obj = args[0];
                if (obj is PythonString s) return new PythonInt(s.Length);
                if (obj is PythonList list) return new PythonInt(list.Items.Count);
                if (obj is PythonTuple tuple) return new PythonInt(tuple.Items.Count);
                if (obj is PythonDict dict) return new PythonInt(dict.Items.Count);
                throw new PythonException("TypeError", $"object of type '{obj.Type}' has no len()");
            }));

            env.SetVariable("str", new BuiltinFunction("str", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "str() takes exactly one argument");
                return args[0].ToStr();
            }));

            env.SetVariable("int", new BuiltinFunction("int", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "int() takes exactly one argument");
                return args[0].ToInt();
            }));

            env.SetVariable("float", new BuiltinFunction("float", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "float() takes exactly one argument");
                return args[0].ToFloat();
            }));

            env.SetVariable("bool", new BuiltinFunction("bool", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "bool() takes exactly one argument");
                return args[0].ToBool();
            }));

            env.SetVariable("list", new BuiltinFunction("list", args =>
            {
                var list = new PythonList();
                if (args.Count == 1)
                {
                    var obj = args[0];
                    if (obj is PythonList sourceList)
                        list.Items.AddRange(sourceList.Items);
                    else if (obj is PythonTuple sourceTuple)
                        list.Items.AddRange(sourceTuple.Items);
                    else if (obj is PythonString str)
                    {
                        foreach (char c in str.Value)
                            list.Items.Add(new PythonString(c.ToString()));
                    }
                    else if (obj is PythonDict dict)
                        list.Items.AddRange(dict.Items.Keys);
                }
                return list;
            }));

            env.SetVariable("dict", new BuiltinFunction("dict", args => new PythonDict()));

            env.SetVariable("tuple", new BuiltinFunction("tuple", args =>
            {
                var tuple = new PythonTuple();
                if (args.Count == 1)
                {
                    var obj = args[0];
                    if (obj is PythonList sourceList)
                        tuple.Items.AddRange(sourceList.Items);
                    else if (obj is PythonTuple sourceTuple)
                        tuple.Items.AddRange(sourceTuple.Items);
                    else if (obj is PythonString str)
                    {
                        foreach (char c in str.Value)
                            tuple.Items.Add(new PythonString(c.ToString()));
                    }
                    else if (obj is PythonDict dict)
                        tuple.Items.AddRange(dict.Items.Keys);
                }
                return tuple;
            }));

            env.SetVariable("range", new BuiltinFunction("range", args =>
            {
                if (args.Count < 1 || args.Count > 3) throw new PythonException("TypeError", "range() takes 1 to 3 arguments");

                int start = 0, stop, step = 1;

                if (args.Count == 1)
                    stop = NumberHelper.ToInt(args[0]);
                else if (args.Count == 2)
                {
                    start = NumberHelper.ToInt(args[0]);
                    stop = NumberHelper.ToInt(args[1]);
                }
                else
                {
                    start = NumberHelper.ToInt(args[0]);
                    stop = NumberHelper.ToInt(args[1]);
                    step = NumberHelper.ToInt(args[2]);
                    if (step == 0) throw new PythonException("ValueError", "range() step argument must not be zero");
                }

                var list = new PythonList();
                if (step > 0)
                    for (int i = start; i < stop; i += step)
                        list.Items.Add(new PythonInt(i));
                else
                    for (int i = start; i > stop; i += step)
                        list.Items.Add(new PythonInt(i));

                return list;
            }));

            env.SetVariable("input", new BuiltinFunction("input", args =>
            {
                if (args.Count > 1) throw new PythonException("TypeError", "input() takes at most 1 argument");
                if (args.Count == 1 && args[0] is PythonString prompt)
                    Console.Write(prompt.Value);
                return new PythonString(Console.ReadLine() ?? "");
            }));

            env.SetVariable("abs", new BuiltinFunction("abs", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "abs() takes exactly one argument");
                if (!NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "abs() argument must be a number");

                if (args[0] is PythonInt pi) return new PythonInt(Math.Abs(pi.Value));
                if (args[0] is PythonFloat pf) return new PythonFloat(Math.Abs(pf.Value));
                throw new PythonException("TypeError", "abs() argument must be a number");
            }));

            env.SetVariable("max", new BuiltinFunction("max", args =>
            {
                if (args.Count == 0) throw new PythonException("TypeError", "max expected at least 1 argument, got 0");
                if (args.Count == 1 && args[0] is PythonList list)
                {
                    if (list.Items.Count == 0) throw new PythonException("ValueError", "max() arg is an empty sequence");

                    PythonTypeObject maxVal = list.Items[0];
                    for (int i = 1; i < list.Items.Count; i++)
                    {
                        if (CompareValues(list.Items[i], maxVal) > 0)
                            maxVal = list.Items[i];
                    }
                    return maxVal;
                }

                PythonTypeObject max = args[0];
                for (int i = 1; i < args.Count; i++)
                {
                    if (CompareValues(args[i], max) > 0)
                        max = args[i];
                }
                return max;
            }));

            env.SetVariable("min", new BuiltinFunction("min", args =>
            {
                if (args.Count == 0) throw new PythonException("TypeError", "min expected at least 1 argument, got 0");
                if (args.Count == 1 && args[0] is PythonList list)
                {
                    if (list.Items.Count == 0) throw new PythonException("ValueError", "min() arg is an empty sequence");

                    PythonTypeObject minVal = list.Items[0];
                    for (int i = 1; i < list.Items.Count; i++)
                    {
                        if (CompareValues(list.Items[i], minVal) < 0)
                            minVal = list.Items[i];
                    }
                    return minVal;
                }

                PythonTypeObject min = args[0];
                for (int i = 1; i < args.Count; i++)
                {
                    if (CompareValues(args[i], min) < 0)
                        min = args[i];
                }
                return min;
            }));

            env.SetVariable("sum", new BuiltinFunction("sum", args =>
            {
                if (args.Count < 1 || args.Count > 2) throw new PythonException("TypeError", "sum() takes 1 or 2 arguments");
                PythonTypeObject start = args.Count == 2 ? args[1] : new PythonInt(0);

                if (args[0] is PythonList list)
                {
                    PythonTypeObject sum = start;
                    foreach (var item in list.Items)
                    {
                        sum = NumberHelper.Add(sum, item);
                    }
                    return sum;
                }
                throw new PythonException("TypeError", "sum() argument must be a sequence of numbers");
            }));

            env.SetVariable("enumerate", new BuiltinFunction("enumerate", args =>
            {
                if (args.Count < 1 || args.Count > 2) throw new PythonException("TypeError", "enumerate() takes 1 or 2 arguments");
                int start = args.Count == 2 ? NumberHelper.ToInt(args[1]) : 0;

                var result = new PythonList();
                if (args[0] is PythonList list)
                {
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        var tuple = new PythonTuple();
                        tuple.Items.Add(new PythonInt(start + i));
                        tuple.Items.Add(list.Items[i]);
                        result.Items.Add(tuple);
                    }
                }
                else if (args[0] is PythonTuple sourceTuple)
                {
                    for (int i = 0; i < sourceTuple.Items.Count; i++)
                    {
                        var tuple = new PythonTuple();
                        tuple.Items.Add(new PythonInt(start + i));
                        tuple.Items.Add(sourceTuple.Items[i]);
                        result.Items.Add(tuple);
                    }
                }
                else if (args[0] is PythonString str)
                {
                    for (int i = 0; i < str.Value.Length; i++)
                    {
                        var tuple = new PythonTuple();
                        tuple.Items.Add(new PythonInt(start + i));
                        tuple.Items.Add(new PythonString(str.Value[i].ToString()));
                        result.Items.Add(tuple);
                    }
                }
                return result;
            }));

            env.SetVariable("zip", new BuiltinFunction("zip", args =>
            {
                if (args.Count == 0) return new PythonList();

                var iterables = new List<PythonList>();
                foreach (var arg in args)
                {
                    if (arg is PythonList list)
                        iterables.Add(list);
                    else if (arg is PythonTuple tuple)
                    {
                        var tupleList = new PythonList();
                        tupleList.Items.AddRange(tuple.Items);
                        iterables.Add(tupleList);
                    }
                    else if (arg is PythonString str)
                    {
                        var strList = new PythonList();
                        foreach (char c in str.Value)
                            strList.Items.Add(new PythonString(c.ToString()));
                        iterables.Add(strList);
                    }
                    else throw new PythonException("TypeError", "zip argument must be iterable");
                }

                var result = new PythonList();
                int minLen = iterables.Min(it => it.Items.Count);

                for (int i = 0; i < minLen; i++)
                {
                    var tuple = new PythonTuple();
                    foreach (var iterable in iterables)
                        tuple.Items.Add(iterable.Items[i]);
                    result.Items.Add(tuple);
                }

                return result;
            }));

            env.SetVariable("type", new BuiltinFunction("type", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "type() takes exactly one argument");
                return new PythonString(GetTypeName(args[0]));
            }));

            env.SetVariable("map", new BuiltinFunction("map", args =>
            {
                if (args.Count != 2) throw new PythonException("TypeError", "map() takes exactly 2 arguments");
                var func = args[0];
                var iterable = args[1];

                if (!(func is Function function))
                    throw new PythonException("TypeError", "map() first argument must be callable");

                List<PythonTypeObject> items;
                if (iterable is PythonList list)
                    items = list.Items;
                else if (iterable is PythonTuple tuple)
                    items = tuple.Items;
                else if (iterable is PythonString str)
                {
                    items = new List<PythonTypeObject>();
                    foreach (char c in str.Value)
                        items.Add(new PythonString(c.ToString()));
                }
                else
                    throw new PythonException("TypeError", "map() second argument must be iterable");

                var result = new PythonList();
                foreach (var item in items)
                {
                    var mappedValue = function.Call(new List<PythonTypeObject> { item });
                    result.Items.Add(mappedValue);
                }
                return result;
            }));

            env.SetVariable("filter", new BuiltinFunction("filter", args =>
            {
                if (args.Count != 2) throw new PythonException("TypeError", "filter() takes exactly 2 arguments");
                var func = args[0];
                var iterable = args[1];

                List<PythonTypeObject> items;
                if (iterable is PythonList list)
                    items = list.Items;
                else if (iterable is PythonTuple tuple)
                    items = tuple.Items;
                else if (iterable is PythonString str)
                {
                    items = new List<PythonTypeObject>();
                    foreach (char c in str.Value)
                        items.Add(new PythonString(c.ToString()));
                }
                else
                    throw new PythonException("TypeError", "filter() second argument must be iterable");

                var result = new PythonList();

                if (func is PythonNone)
                {
                    // filter(None, iterable) filters truthy values
                    foreach (var item in items)
                    {
                        if (IsTrue(item))
                            result.Items.Add(item);
                    }
                }
                else if (func is Function function)
                {
                    foreach (var item in items)
                    {
                        var filterResult = function.Call(new List<PythonTypeObject> { item });
                        if (IsTrue(filterResult))
                            result.Items.Add(item);
                    }
                }
                else
                {
                    throw new PythonException("TypeError", "filter() first argument must be None or callable");
                }

                return result;
            }));

            env.SetVariable("sorted", new BuiltinFunction("sorted", args =>
            {
                if (args.Count < 1 || args.Count > 2) throw new PythonException("TypeError", "sorted() takes 1 or 2 arguments");
                var iterable = args[0];
                var keyFunc = args.Count > 1 ? args[1] : null;

                List<PythonTypeObject> items;
                if (iterable is PythonList list)
                    items = new List<PythonTypeObject>(list.Items);
                else if (iterable is PythonTuple tuple)
                    items = new List<PythonTypeObject>(tuple.Items);
                else if (iterable is PythonString str)
                {
                    items = new List<PythonTypeObject>();
                    foreach (char c in str.Value)
                        items.Add(new PythonString(c.ToString()));
                }
                else
                    throw new PythonException("TypeError", "sorted() argument must be iterable");

                if (keyFunc == null || keyFunc is PythonNone)
                {
                    // Default sorting
                    items.Sort((a, b) =>
                    {
                        if (NumberHelper.IsNumber(a) && NumberHelper.IsNumber(b))
                            return NumberHelper.ToDouble(a).CompareTo(NumberHelper.ToDouble(b));
                        if (a is PythonString sa && b is PythonString sb) return string.Compare(sa.Value, sb.Value);
                        return 0;
                    });
                }
                else if (keyFunc is Function function)
                {
                    // Sort with key function
                    var keyed = items.Select(item => new { Item = item, Key = function.Call(new List<PythonTypeObject> { item }) }).ToList();
                    keyed.Sort((a, b) =>
                    {
                        if (NumberHelper.IsNumber(a.Key) && NumberHelper.IsNumber(b.Key))
                            return NumberHelper.ToDouble(a.Key).CompareTo(NumberHelper.ToDouble(b.Key));
                        if (a.Key is PythonString sa && b.Key is PythonString sb) return string.Compare(sa.Value, sb.Value);
                        return 0;
                    });
                    items = keyed.Select(x => x.Item).ToList();
                }
                else
                {
                    throw new PythonException("TypeError", "sorted() key argument must be callable");
                }

                var result = new PythonList();
                result.Items.AddRange(items);
                return result;
            }));

            env.SetVariable("any", new BuiltinFunction("any", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "any() takes exactly one argument");
                var iterable = args[0];

                List<PythonTypeObject> items;
                if (iterable is PythonList list)
                    items = list.Items;
                else if (iterable is PythonTuple tuple)
                    items = tuple.Items;
                else if (iterable is PythonString str)
                {
                    items = new List<PythonTypeObject>();
                    foreach (char c in str.Value)
                        items.Add(new PythonString(c.ToString()));
                }
                else
                    throw new PythonException("TypeError", "any() argument must be iterable");

                foreach (var item in items)
                {
                    if (IsTrue(item))
                        return new PythonBool(true);
                }
                return new PythonBool(false);
            }));

            env.SetVariable("all", new BuiltinFunction("all", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "all() takes exactly one argument");
                var iterable = args[0];

                List<PythonTypeObject> items;
                if (iterable is PythonList list)
                    items = list.Items;
                else if (iterable is PythonTuple tuple)
                    items = tuple.Items;
                else if (iterable is PythonString str)
                {
                    items = new List<PythonTypeObject>();
                    foreach (char c in str.Value)
                        items.Add(new PythonString(c.ToString()));
                }
                else
                    throw new PythonException("TypeError", "all() argument must be iterable");

                foreach (var item in items)
                {
                    if (!IsTrue(item))
                        return new PythonBool(false);
                }
                return new PythonBool(true);
            }));

            // Modified globals() function - returns the environment's dictionary representation
            env.SetVariable("globals", new BuiltinFunction("globals", args =>
            {
                if (args.Count != 0) throw new PythonException("TypeError", "globals() takes no arguments");

                // Return the current environment's dictionary representation
                return env.ToDict();
            }));

            // ADD open() function for with statement support
            env.SetVariable("open", new BuiltinFunction("open", args =>
            {
                if (args.Count < 1 || args.Count > 2)
                    throw new PythonException("TypeError", "open() takes 1 or 2 arguments");

                string path;
                if (args[0] is PythonString ps)
                    path = ps.Value;
                else
                    throw new PythonException("TypeError", "open() argument 1 must be a string");

                string mode = "r";
                if (args.Count > 1)
                {
                    if (args[1] is PythonString ms)
                        mode = ms.Value;
                    else
                        throw new PythonException("TypeError", "open() argument 2 must be a string");
                }

                try
                {
                    return new FileObject(path, mode);
                }
                catch (FileNotFoundException)
                {
                    throw new PythonException("FileNotFoundError", $"No such file or directory: '{path}'");
                }
                catch (DirectoryNotFoundException)
                {
                    throw new PythonException("FileNotFoundError", $"No such file or directory: '{path}'");
                }
                catch (IOException ex)
                {
                    throw new PythonException("IOError", $"Cannot open file '{path}': {ex.Message}");
                }
                catch (Exception ex)
                {
                    throw new PythonException("IOError", $"Error opening file '{path}': {ex.Message}");
                }
            }));

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

            // Constants
            env.SetVariable("None", PythonNone.Instance);
            env.SetVariable("True", new PythonBool(true));
            env.SetVariable("False", new PythonBool(false));

            // dir attr
            env.SetVariable("dir", new BuiltinFunction("dir", args =>
            {
                if (args.Count > 1) 
                    throw new PythonException("TypeError", "dir() takes at most 1 argument");
                
                var result = new PythonList();
                
                if (args.Count == 0)
                {
                    // No arguments - return names in current scope
                    var allVars = env.GetAllVariables();
                    var names = allVars.Keys.OrderBy(k => k).ToList();
                    foreach (var name in names)
                    {
                        result.Items.Add(new PythonString(name));
                    }
                }
                else
                {
                    // With argument - return attributes/methods of the object
                    var obj = args[0];
                    var attributes = new HashSet<string>();
                    
                    // Get built-in methods for the object type
                    var methodNames = obj.GetMethodNames();
                    foreach (var method in methodNames)
                    {
                        attributes.Add(method);
                    }
                    
                    // Add type-specific attributes
                    switch (obj)
                    {
                        case PythonList list:
                            // List doesn't have additional attributes beyond methods
                            break;
                            
                        case PythonDict dict:
                            // Dict doesn't have additional attributes beyond methods
                            break;
                            
                        case PythonTuple tuple:
                            // Tuple doesn't have additional attributes beyond methods
                            break;
                            
                        case PythonString str:
                            // String doesn't have additional attributes beyond methods
                            break;
                            
                        case PythonInstance instance:
                            // 인스턴스 변수만 추가 (전역 제외)
                            foreach (var key in instance.InstanceEnv.variables.Keys)
                            {
                                attributes.Add(key);
                            }
                            
                            // 클래스 메서드 추가
                            PythonClass currentClass = instance.Class;
                            while (currentClass != null)
                            {
                                foreach (var key in currentClass.ClassEnv.variables.Keys)
                                {
                                    attributes.Add(key);
                                }
                                currentClass = currentClass.ParentClass;
                            }
                            break;
                            
                        case PythonClass cls:
                            // For classes, add all class variables and methods
                            var classVars = cls.ClassEnv.GetAllVariables();
                            foreach (var kvp in classVars)
                            {
                                attributes.Add(kvp.Key);
                            }
                            break;
                            
                        case PythonModule module:
                            // For modules, add all module attributes
                            var moduleVars = module.ModuleEnv.GetAllVariables();
                            foreach (var kvp in moduleVars)
                            {
                                attributes.Add(kvp.Key);
                            }
                            break;
                            
                        case Function func:
                            // Functions have standard attributes
                            attributes.Add("__name__");
                            attributes.Add("__doc__");
                            if (func is UserFunction userFunc)
                            {
                                attributes.Add("__code__");
                                attributes.Add("__defaults__");
                            }
                            break;
                            
                        case PythonInt:
                        case PythonFloat:
                            // Numeric types - add common numeric methods
                            attributes.Add("__add__");
                            attributes.Add("__sub__");
                            attributes.Add("__mul__");
                            attributes.Add("__div__");
                            attributes.Add("__mod__");
                            attributes.Add("__pow__");
                            attributes.Add("__neg__");
                            attributes.Add("__abs__");
                            attributes.Add("__eq__");
                            attributes.Add("__ne__");
                            attributes.Add("__lt__");
                            attributes.Add("__le__");
                            attributes.Add("__gt__");
                            attributes.Add("__ge__");
                            break;
                            
                        case PythonBool:
                            // Bool inherits from int
                            attributes.Add("__and__");
                            attributes.Add("__or__");
                            attributes.Add("__xor__");
                            attributes.Add("__not__");
                            break;
                            
                        case PythonNone:
                            // None has minimal attributes
                            attributes.Add("__eq__");
                            attributes.Add("__ne__");
                            attributes.Add("__repr__");
                            attributes.Add("__str__");
                            break;
                    }
                    
                    // Add common attributes all objects have
                    attributes.Add("__class__");
                    attributes.Add("__repr__");
                    attributes.Add("__str__");
                    attributes.Add("__hash__");
                    attributes.Add("__eq__");
                    
                    // Sort and add to result
                    var sortedAttrs = attributes.OrderBy(a => a).ToList();
                    foreach (var attr in sortedAttrs)
                    {
                        result.Items.Add(new PythonString(attr));
                    }
                }
                
                return result;
            }));

            // Also add hasattr() function for completeness
            env.SetVariable("hasattr", new BuiltinFunction("hasattr", args =>
            {
                if (args.Count != 2) 
                    throw new PythonException("TypeError", "hasattr() takes exactly 2 arguments");
                
                var obj = args[0];
                if (!(args[1] is PythonString attrName))
                    throw new PythonException("TypeError", "hasattr() attribute name must be a string");
                
                var name = attrName.Value;
                
                try
                {
                    // Try to get the attribute
                    switch (obj)
                    {
                        case PythonInstance instance:
                            instance.GetAttribute(name);
                            return new PythonBool(true);
                            
                        case PythonClass cls:
                            cls.ClassEnv.GetVariable(name);
                            return new PythonBool(true);
                            
                        case PythonModule module:
                            module.GetAttribute(name);
                            return new PythonBool(true);
                            
                        case PythonList:
                        case PythonDict:
                        case PythonTuple:
                        case PythonString:
                            // Check if method exists
                            try
                            {
                                obj.GetMethod(name);
                                return new PythonBool(true);
                            }
                            catch
                            {
                                return new PythonBool(false);
                            }
                            
                        default:
                            // Check for built-in attributes
                            var builtinAttrs = new HashSet<string> { "__class__", "__repr__", "__str__", "__hash__", "__eq__" };
                            return new PythonBool(builtinAttrs.Contains(name));
                    }
                }
                catch
                {
                    return new PythonBool(false);
                }
            }));

            // Add getattr() function
            env.SetVariable("getattr", new BuiltinFunction("getattr", args =>
            {
                if (args.Count < 2 || args.Count > 3) 
                    throw new PythonException("TypeError", "getattr() takes 2 or 3 arguments");
                
                var obj = args[0];
                if (!(args[1] is PythonString attrName))
                    throw new PythonException("TypeError", "getattr() attribute name must be a string");
                
                var name = attrName.Value;
                var defaultValue = args.Count == 3 ? args[2] : null;
                
                try
                {
                    switch (obj)
                    {
                        case PythonInstance instance:
                            return instance.GetAttribute(name);
                            
                        case PythonClass cls:
                            return cls.ClassEnv.GetVariable(name);
                            
                        case PythonModule module:
                            return module.GetAttribute(name);
                            
                        case PythonList list:
                            return list.GetMethod(name);
                            
                        case PythonDict dict:
                            return dict.GetMethod(name);
                            
                        case PythonTuple tuple:
                            return tuple.GetMethod(name);
                            
                        case PythonString str:
                            return str.GetMethod(name);
                            
                        case Function func when name == "__name__":
                            return new PythonString(func.Name);
                            
                        default:
                            // Try to get built-in attributes
                            if (name == "__class__")
                                return new PythonString(GetTypeName(obj));
                            if (name == "__repr__" || name == "__str__")
                                return new BuiltinFunction(name, _ => new PythonString(obj.ToPythonString()));
                                
                            if (defaultValue != null)
                                return defaultValue;
                            throw new PythonException("AttributeError", $"'{GetTypeName(obj)}' object has no attribute '{name}'");
                    }
                }
                catch (PythonException ex) when (ex.Type == "AttributeError" && defaultValue != null)
                {
                    return defaultValue;
                }
            }));

            // Add setattr() function
            env.SetVariable("setattr", new BuiltinFunction("setattr", args =>
            {
                if (args.Count != 3) 
                    throw new PythonException("TypeError", "setattr() takes exactly 3 arguments");
                
                var obj = args[0];
                if (!(args[1] is PythonString attrName))
                    throw new PythonException("TypeError", "setattr() attribute name must be a string");
                
                var name = attrName.Value;
                var value = args[2];
                
                switch (obj)
                {
                    case PythonInstance instance:
                        instance.SetAttribute(name, value);
                        return PythonNone.Instance;
                        
                    case PythonClass cls:
                        cls.ClassEnv.SetVariable(name, value);
                        return PythonNone.Instance;
                        
                    case PythonModule module:
                        module.SetAttribute(name, value);
                        return PythonNone.Instance;
                        
                    default:
                        throw new PythonException("AttributeError", 
                            $"'{GetTypeName(obj)}' object attribute '{name}' is read-only");
                }
            }));

            // Add delattr() function
            env.SetVariable("delattr", new BuiltinFunction("delattr", args =>
            {
                if (args.Count != 2) 
                    throw new PythonException("TypeError", "delattr() takes exactly 2 arguments");
                
                var obj = args[0];
                if (!(args[1] is PythonString attrName))
                    throw new PythonException("TypeError", "delattr() attribute name must be a string");
                
                var name = attrName.Value;
                
                switch (obj)
                {
                    case PythonInstance instance:
                        instance.InstanceEnv.DeleteVariable(name);
                        return PythonNone.Instance;
                        
                    case PythonClass cls:
                        cls.ClassEnv.DeleteVariable(name);
                        return PythonNone.Instance;
                        
                    case PythonModule module:
                        module.ModuleEnv.DeleteVariable(name);
                        return PythonNone.Instance;
                        
                    default:
                        throw new PythonException("AttributeError", 
                            $"'{GetTypeName(obj)}' object attribute '{name}' cannot be deleted");
                }
            }));
        }

        static private bool IsTrue(PythonTypeObject obj)
        {
            if (obj == null) return false;
            return obj.IsTrue();
        }
        
        static private int CompareValues(PythonTypeObject a, PythonTypeObject b)
        {
            if (NumberHelper.IsNumber(a) && NumberHelper.IsNumber(b))
                return NumberHelper.ToDouble(a).CompareTo(NumberHelper.ToDouble(b));
            if (a is PythonString sa && b is PythonString sb)
                return string.Compare(sa.Value, sb.Value);
            return 0;
        }

        static private string GetTypeName(PythonTypeObject obj)
        {
            return obj switch
            {
                PythonNone => "NoneType",
                PythonBool => "bool",
                PythonInt => "int",
                PythonFloat => "float",
                PythonString => "str",
                PythonList => "list",
                PythonTuple => "tuple",
                PythonDict => "dict",
                Function => "function",
                PythonClass => "class",
                PythonInstance instance => instance.Class.Name,
                PythonModule module => $"module '{module.Name}'",
                _ => obj.GetType().Name
            };
        }

        public void SetVariable(string name, PythonTypeObject value) => variables[name] = value;

        public PythonTypeObject GetVariable(string name)
        {
            if (variables.ContainsKey(name))
                return variables[name];
            if (parent != null)
                return parent.GetVariable(name);
            throw new PythonException("NameError", $"Name '{name}' is not defined");
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

        public bool HasVariable(string name) => variables.ContainsKey(name) || (parent?.HasVariable(name) ?? false);
        
        public Dictionary<string, PythonTypeObject> GetAllVariables()
        {
            var result = new Dictionary<string, PythonTypeObject>();
            if (parent != null)
            {
                foreach (var kvp in parent.GetAllVariables())
                    result[kvp.Key] = kvp.Value;
            }
            foreach (var kvp in variables)
                result[kvp.Key] = kvp.Value;
            return result;
        }

        public override bool IsTrue()
        {
            throw new NotImplementedException();
        }

        public override string ToPythonString()
        {
            throw new NotImplementedException();
        }

        public override bool Equals(PythonTypeObject other)
        {
            throw new NotImplementedException();
        }

        public override object GetRawValue()
        {
            throw new NotImplementedException();
        }
    }
}