// enhanced_modules_environment.cs (일부)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpPy
{
    // Enhanced Environment Class with PythonTypeObject
    public class Environment
    {
        private Dictionary<string, PythonTypeObject> variables = new Dictionary<string, PythonTypeObject>();
        public Environment parent;
        public List<string> SearchPaths { get; set; }

        public Environment(Environment parent = null)
        {
            this.parent = parent;
            if (parent == null) 
            {
                SetupBuiltins(this);
                SearchPaths = new List<string> { "." }; // Default search path
            }
            else
            {
                SearchPaths = parent.SearchPaths; // Inherit search paths from parent
            }
        }

        static private void SetupBuiltins(Environment env)
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
                        tuple.Items.Add(start + i);
                        tuple.Items.Add(list.Items[i]);
                        result.Items.Add(tuple);
                    }
                }
                else if (args[0] is PythonTuple sourceTuple)
                {
                    for (int i = 0; i < sourceTuple.Items.Count; i++)
                    {
                        var tuple = new PythonTuple();
                        tuple.Items.Add(start + i);
                        tuple.Items.Add(sourceTuple.Items[i]);
                        result.Items.Add(tuple);
                    }
                }
                else if (args[0] is string str)
                {
                    for (int i = 0; i < str.Length; i++)
                    {
                        var tuple = new PythonTuple();
                        tuple.Items.Add(start + i);
                        tuple.Items.Add(str[i].ToString());
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
                    else if (arg is string str)
                    {
                        var strList = new PythonList();
                        strList.Items.AddRange(str.Select(c => c.ToString()));
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

            // Add more builtin functions here...
        }


            env.SetVariable("map", new BuiltinFunction("map", args =>
            {
                if (args.Count != 2) throw new PythonException("TypeError", "map() takes exactly 2 arguments");
                var func = args[0];
                var iterable = args[1];

                if (!(func is Function function))
                    throw new PythonException("TypeError", "map() first argument must be callable");

                List<object> items;
                if (iterable is PythonList list)
                    items = list.Items;
                else if (iterable is PythonTuple tuple)
                    items = tuple.Items;
                else if (iterable is string str)
                    items = str.Select(c => c.ToString()).Cast<object>().ToList();
                else
                    throw new PythonException("TypeError", "map() second argument must be iterable");

                var result = new PythonList();
                foreach (var item in items)
                {
                    var mappedValue = function.Call(new List<object> { item });
                    result.Items.Add(mappedValue);
                }
                return result;
            }));

            env.SetVariable("filter", new BuiltinFunction("filter", args =>
            {
                if (args.Count != 2) throw new PythonException("TypeError", "filter() takes exactly 2 arguments");
                var func = args[0];
                var iterable = args[1];

                List<object> items;
                if (iterable is PythonList list)
                    items = list.Items;
                else if (iterable is PythonTuple tuple)
                    items = tuple.Items;
                else if (iterable is string str)
                    items = str.Select(c => c.ToString()).Cast<object>().ToList();
                else
                    throw new PythonException("TypeError", "filter() second argument must be iterable");

                var result = new PythonList();
                
                if (func == null)
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
                        var filterResult = function.Call(new List<object> { item });
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

                List<object> items;
                if (iterable is PythonList list)
                    items = new List<object>(list.Items);
                else if (iterable is PythonTuple tuple)
                    items = new List<object>(tuple.Items);
                else if (iterable is string str)
                    items = str.Select(c => c.ToString()).Cast<object>().ToList();
                else
                    throw new PythonException("TypeError", "sorted() argument must be iterable");

                if (keyFunc == null)
                {
                    // Default sorting
                    items.Sort((a, b) =>
                    {
                        if (NumberHelper.IsNumber(a) && NumberHelper.IsNumber(b))
                            return NumberHelper.ToDouble(a).CompareTo(NumberHelper.ToDouble(b));
                        if (a is string sa && b is string sb) return sa.CompareTo(sb);
                        return 0;
                    });
                }
                else if (keyFunc is Function function)
                {
                    // Sort with key function
                    var keyed = items.Select(item => new { Item = item, Key = function.Call(new List<object> { item }) }).ToList();
                    keyed.Sort((a, b) =>
                    {
                        if (NumberHelper.IsNumber(a.Key) && NumberHelper.IsNumber(b.Key))
                            return NumberHelper.ToDouble(a.Key).CompareTo(NumberHelper.ToDouble(b.Key));
                        if (a.Key is string sa && b.Key is string sb) return sa.CompareTo(sb);
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

                List<object> items;
                if (iterable is PythonList list)
                    items = list.Items;
                else if (iterable is PythonTuple tuple)
                    items = tuple.Items;
                else if (iterable is string str)
                    items = str.Select(c => c.ToString()).Cast<object>().ToList();
                else
                    throw new PythonException("TypeError", "any() argument must be iterable");

                return items.Any(IsTrue);
            }));

            env.SetVariable("all", new BuiltinFunction("all", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "all() takes exactly one argument");
                var iterable = args[0];

                List<object> items;
                if (iterable is PythonList list)
                    items = list.Items;
                else if (iterable is PythonTuple tuple)
                    items = tuple.Items;
                else if (iterable is string str)
                    items = str.Select(c => c.ToString()).Cast<object>().ToList();
                else
                    throw new PythonException("TypeError", "all() argument must be iterable");

                return items.All(IsTrue);
            }));

            // ADD globals() function - returns current environment variables as dict
            env.SetVariable("globals", new BuiltinFunction("globals", args =>
            {
                if (args.Count != 0) throw new PythonException("TypeError", "globals() takes no arguments");
                
                var globalsDict = new PythonDict();
                var allVars = env.GetAllVariables();
                
                foreach (var kvp in allVars)
                {
                    globalsDict.Items[kvp.Key] = kvp.Value;
                }
                
                return globalsDict;
            }));

            // ADD open() function for with statement support
            env.SetVariable("open", new BuiltinFunction("open", args =>
            {
                if (args.Count < 1 || args.Count > 2) 
                    throw new PythonException("TypeError", "open() takes 1 or 2 arguments");
                
                string path = args[0]?.ToString();
                if (string.IsNullOrEmpty(path))
                    throw new PythonException("TypeError", "open() argument 1 must be a string");
                
                string mode = args.Count > 1 ? args[1]?.ToString() ?? "r" : "r";
                
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
                if (!(expression is string exprStr))
                    throw new PythonException("TypeError", "eval() first argument must be a string");
                
                var globals = args.Count > 1 && args[1] != null ? args[1] as Environment : env;
                var locals = args.Count > 2 && args[2] != null ? args[2] as Environment : globals;
                
                if (globals == null) globals = env;
                if (locals == null) locals = globals;
                
                try
                {
                    return PythonCompiler.Eval(exprStr, globals, locals);
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
                if (!(source is string sourceStr))
                    throw new PythonException("TypeError", "exec() first argument must be a string");
                
                var globals = args.Count > 1 && args[1] != null ? args[1] as Environment : env;
                var locals = args.Count > 2 && args[2] != null ? args[2] as Environment : globals;
                
                if (globals == null) globals = env;
                if (locals == null) locals = globals;
                
                try
                {
                    PythonCompiler.Exec(sourceStr, globals, locals);
                    return null; // exec returns None
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
                
                if (!(source is string sourceStr))
                    throw new PythonException("TypeError", "compile() first argument must be a string");
                if (!(filename is string filenameStr))
                    throw new PythonException("TypeError", "compile() second argument must be a string");
                if (!(mode is string modeStr))
                    throw new PythonException("TypeError", "compile() third argument must be a string");
                
                if (modeStr != "exec" && modeStr != "eval")
                    throw new PythonException("ValueError", "compile() mode must be 'exec' or 'eval'");
                
                try
                {
                    return PythonCompiler.Compile(sourceStr, filenameStr, modeStr);
                }
                catch (Exception ex)
                {
                    throw new PythonException("SyntaxError", $"Error in compile(): {ex.Message}");
                }
            }));
        }

        static private bool IsTrue(object obj)
        {
            if (obj == null) return false;
            if (obj is bool b) return b;
            if (obj is int i) return i != 0;
            if (obj is double d) return d != 0;
            if (obj is string s) return !string.IsNullOrEmpty(s);
            if (obj is PythonList l) return l.Items.Count > 0;
            if (obj is PythonTuple t) return t.Items.Count > 0;
            if (obj is PythonDict dict) return dict.Items.Count > 0;
            return true;
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
    }
}