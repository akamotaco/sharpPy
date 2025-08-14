// enhanced_modules_environment.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpPy
{
    // Enhanced Module System with better error handling
    public class PythonModule
    {
        public string Name { get; }
        public Environment ModuleEnv { get; }

        public PythonModule(string name)
        {
            Name = name;
            ModuleEnv = new Environment();
        }

        public object GetAttribute(string name)
        {
            try
            {
                return ModuleEnv.GetVariable(name);
            }
            catch (PythonException)
            {
                throw new PythonException("AttributeError", $"module '{Name}' has no attribute '{name}'");
            }
        }

        public void SetAttribute(string name, object value) => ModuleEnv.SetVariable(name, value);

        public override string ToString() => $"<module '{Name}'>";
    }

    public static class ModuleSystem
    {
        private static Dictionary<string, PythonModule> loadedModules = new Dictionary<string, PythonModule>();

        public static PythonModule ImportModule(string name, List<string> searchPaths = null)
        {
            if (loadedModules.ContainsKey(name))
                return loadedModules[name];

            var module = CreateBuiltinModule(name);
            if (module != null)
            {
                loadedModules[name] = module;
                return module;
            }

            // Use default search paths if none provided
            if (searchPaths == null)
            {
                searchPaths = new List<string> { "." };
            }

            // Try to load from file system using search paths
            try
            {
                string foundPath = null;

                // Handle package.module notation (e.g., "folder.m")
                if (name.Contains('.'))
                {
                    var parts = name.Split('.');
                    var relativePath = string.Join(Path.DirectorySeparatorChar.ToString(), parts) + ".py";
                    
                    // Search in all paths
                    foreach (var searchPath in searchPaths)
                    {
                        var candidatePath = Path.Combine(searchPath, relativePath);
                        if (File.Exists(candidatePath))
                        {
                            foundPath = candidatePath;
                            break;
                        }
                        
                        // Try folder/module structure
                        var folderPath = Path.Combine(searchPath, parts[0], parts[1] + ".py");
                        if (File.Exists(folderPath))
                        {
                            foundPath = folderPath;
                            break;
                        }
                    }
                }
                else
                {
                    // Simple module name - could be a file or a package directory
                    foreach (var searchPath in searchPaths)
                    {
                        // First try as a .py file
                        var candidatePath = Path.Combine(searchPath, name + ".py");
                        if (File.Exists(candidatePath))
                        {
                            foundPath = candidatePath;
                            break;
                        }
                        
                        // Then try as a package directory
                        var packageDir = Path.Combine(searchPath, name);
                        if (Directory.Exists(packageDir))
                        {
                            // Create a package module (directory-based)
                            module = CreatePackageModule(name, packageDir);
                            if (module != null)
                            {
                                loadedModules[name] = module;
                                return module;
                            }
                        }
                    }
                }

                if (foundPath != null)
                {
                    string code = File.ReadAllText(foundPath);
                    module = new PythonModule(name);

                    var interpreter = new SharpPy.PythonInterpreter();
                    interpreter.SetGlobalEnv(module.ModuleEnv);
                    interpreter.Execute(code, foundPath);

                    loadedModules[name] = module;
                    return module;
                }
            }
            catch (PythonException ex)
            {
                throw new PythonException("ImportError", $"Failed to import module '{name}': {ex.Message}", ex.Line, ex.Column, ex.FileName);
            }
            catch (Exception ex)
            {
                throw new PythonException("ImportError", $"Failed to import module '{name}': {ex.Message}");
            }

            throw new PythonException("ImportError", $"No module named '{name}'");
        }

        private static PythonModule CreatePackageModule(string packageName, string packageDir)
        {
            try
            {
                var module = new PythonModule(packageName);
                
                // Check for __init__.py first (traditional Python package)
                var initFile = Path.Combine(packageDir, "__init__.py");
                if (File.Exists(initFile))
                {
                    string code = File.ReadAllText(initFile);
                    var interpreter = new SharpPy.PythonInterpreter();
                    interpreter.SetGlobalEnv(module.ModuleEnv);
                    interpreter.Execute(code, initFile);
                }
                
                // Add all .py files in the directory as submodules
                var pyFiles = Directory.GetFiles(packageDir, "*.py");
                foreach (var pyFile in pyFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(pyFile);
                    if (fileName != "__init__") // Skip __init__.py as it's already processed
                    {
                        try
                        {
                            var submodule = new PythonModule($"{packageName}.{fileName}");
                            string code = File.ReadAllText(pyFile);
                            var interpreter = new SharpPy.PythonInterpreter();
                            interpreter.SetGlobalEnv(submodule.ModuleEnv);
                            interpreter.Execute(code, pyFile);
                            
                            // Add submodule to package
                            module.SetAttribute(fileName, submodule);
                            
                            // Also cache the submodule for direct access
                            loadedModules[$"{packageName}.{fileName}"] = submodule;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Warning: Failed to load submodule {fileName}: {ex.Message}");
                        }
                    }
                }
                
                return module;
            }
            catch (Exception ex)
            {
                throw new PythonException("ImportError", $"Failed to create package module '{packageName}': {ex.Message}");
            }
        }
        
        private static PythonModule CreateBuiltinModule(string name)
        {
            return name switch
            {
                "math" => CreateMathModule(),
                "random" => CreateRandomModule(),
                "os" => CreateOsModule(),
                _ => null
            };
        }

        private static PythonModule CreateMathModule()
        {
            var module = new PythonModule("math");

            module.SetAttribute("pi", Math.PI);
            module.SetAttribute("e", Math.E);

            module.SetAttribute("sqrt", new BuiltinFunction("sqrt", args =>
            {
                if (args.Count != 1 || !NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "sqrt() takes exactly one numeric argument");
                return Math.Sqrt(NumberHelper.ToDouble(args[0]));
            }));

            module.SetAttribute("pow", new BuiltinFunction("pow", args =>
            {
                if (args.Count != 2 || !NumberHelper.IsNumber(args[0]) || !NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "pow() takes exactly two numeric arguments");
                return Math.Pow(NumberHelper.ToDouble(args[0]), NumberHelper.ToDouble(args[1]));
            }));

            module.SetAttribute("sin", new BuiltinFunction("sin", args =>
            {
                if (args.Count != 1 || !NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "sin() takes exactly one numeric argument");
                return Math.Sin(NumberHelper.ToDouble(args[0]));
            }));

            module.SetAttribute("cos", new BuiltinFunction("cos", args =>
            {
                if (args.Count != 1 || !NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "cos() takes exactly one numeric argument");
                return Math.Cos(NumberHelper.ToDouble(args[0]));
            }));

            module.SetAttribute("tan", new BuiltinFunction("tan", args =>
            {
                if (args.Count != 1 || !NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "tan() takes exactly one numeric argument");
                return Math.Tan(NumberHelper.ToDouble(args[0]));
            }));

            module.SetAttribute("floor", new BuiltinFunction("floor", args =>
            {
                if (args.Count != 1 || !NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "floor() takes exactly one numeric argument");
                return (int)Math.Floor(NumberHelper.ToDouble(args[0]));
            }));

            module.SetAttribute("ceil", new BuiltinFunction("ceil", args =>
            {
                if (args.Count != 1 || !NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "ceil() takes exactly one numeric argument");
                return (int)Math.Ceiling(NumberHelper.ToDouble(args[0]));
            }));

            return module;
        }

        private static PythonModule CreateRandomModule()
        {
            var module = new PythonModule("random");
            var random = new Random();

            module.SetAttribute("random", new BuiltinFunction("random", args =>
            {
                if (args.Count != 0) throw new PythonException("TypeError", "random() takes no arguments");
                return random.NextDouble();
            }));

            module.SetAttribute("randint", new BuiltinFunction("randint", args =>
            {
                if (args.Count != 2 || !NumberHelper.IsNumber(args[0]) || !NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "randint() takes exactly two integer arguments");
                return random.Next(NumberHelper.ToInt(args[0]), NumberHelper.ToInt(args[1]) + 1);
            }));

            module.SetAttribute("choice", new BuiltinFunction("choice", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "choice() takes exactly one argument");
                if (args[0] is PythonList list)
                {
                    if (list.Items.Count == 0) throw new PythonException("IndexError", "choice() from empty sequence");
                    return list.Items[random.Next(list.Items.Count)];
                }
                if (args[0] is PythonTuple tuple)
                {
                    if (tuple.Items.Count == 0) throw new PythonException("IndexError", "choice() from empty sequence");
                    return tuple.Items[random.Next(tuple.Items.Count)];
                }
                throw new PythonException("TypeError", "choice() argument must be a sequence");
            }));

            module.SetAttribute("shuffle", new BuiltinFunction("shuffle", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "shuffle() takes exactly one argument");
                if (args[0] is PythonList list)
                {
                    for (int i = list.Items.Count - 1; i > 0; i--)
                    {
                        int j = random.Next(i + 1);
                        (list.Items[i], list.Items[j]) = (list.Items[j], list.Items[i]);
                    }
                    return null;
                }
                throw new PythonException("TypeError", "shuffle() argument must be a list");
            }));

            return module;
        }

        private static PythonModule CreateOsModule()
        {
            var module = new PythonModule("os");

            module.SetAttribute("getcwd", new BuiltinFunction("getcwd", args =>
            {
                if (args.Count != 0) throw new PythonException("TypeError", "getcwd() takes no arguments");
                return Directory.GetCurrentDirectory();
            }));

            module.SetAttribute("listdir", new BuiltinFunction("listdir", args =>
            {
                string path = args.Count == 0 ? "." : args[0]?.ToString() ?? ".";
                var list = new PythonList();
                try
                {
                    foreach (var item in Directory.GetFileSystemEntries(path))
                        list.Items.Add(Path.GetFileName(item));
                }
                catch (Exception ex)
                {
                    throw new PythonException("OSError", $"listdir() error: {ex.Message}");
                }
                return list;
            }));

            module.SetAttribute("path", CreateOsPathModule());

            return module;
        }

        private static PythonModule CreateOsPathModule()
        {
            var module = new PythonModule("path");

            module.SetAttribute("exists", new BuiltinFunction("exists", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "exists() takes exactly one argument");
                string path = args[0]?.ToString() ?? "";
                return File.Exists(path) || Directory.Exists(path);
            }));

            module.SetAttribute("isfile", new BuiltinFunction("isfile", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "isfile() takes exactly one argument");
                string path = args[0]?.ToString() ?? "";
                return File.Exists(path);
            }));

            module.SetAttribute("isdir", new BuiltinFunction("isdir", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "isdir() takes exactly one argument");
                string path = args[0]?.ToString() ?? "";
                return Directory.Exists(path);
            }));

            return module;
        }
    }

    // Enhanced Environment Class with better error handling
    public class Environment
    {
        private Dictionary<string, object> variables = new Dictionary<string, object>();
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
                    if (arg == null) return "None";
                    if (arg is string s) return s;
                    if (arg is bool b) return b ? "True" : "False";
                    return arg.ToString();
                }));

                Console.WriteLine(output);
                return null;
            }));

            env.SetVariable("len", new BuiltinFunction("len", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "len() takes exactly one argument");
                var obj = args[0];
                if (obj is string s) return s.Length;
                if (obj is PythonList list) return list.Items.Count;
                if (obj is PythonTuple tuple) return tuple.Items.Count;
                if (obj is PythonDict dict) return dict.Items.Count;
                throw new PythonException("TypeError", $"object of type '{GetTypeName(obj)}' has no len()");
            }));

            env.SetVariable("str", new BuiltinFunction("str", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "str() takes exactly one argument");
                var obj = args[0];
                if (obj == null) return "None";
                if (obj is bool b) return b ? "True" : "False";
                return obj.ToString();
            }));

            env.SetVariable("int", new BuiltinFunction("int", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "int() takes exactly one argument");
                if (args[0] is int i) return i;
                if (args[0] is double d) return (int)Math.Truncate(d);
                if (args[0] is bool b) return b ? 1 : 0;
                if (int.TryParse(args[0]?.ToString(), out var result)) return result;
                throw new PythonException("ValueError", $"invalid literal for int(): '{args[0]}'");
            }));

            env.SetVariable("float", new BuiltinFunction("float", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "float() takes exactly one argument");
                if (args[0] is double d) return d;
                if (args[0] is int i) return (double)i;
                if (args[0] is bool b) return b ? 1.0 : 0.0;
                if (double.TryParse(args[0]?.ToString(), out var result)) return result;
                throw new PythonException("ValueError", $"invalid literal for float(): '{args[0]}'");
            }));

            env.SetVariable("bool", new BuiltinFunction("bool", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "bool() takes exactly one argument");
                return IsTrue(args[0]);
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
                    else if (obj is string str)
                        list.Items.AddRange(str.Select(c => c.ToString()));
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
                    else if (obj is string str)
                        tuple.Items.AddRange(str.Select(c => c.ToString()));
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
                        list.Items.Add(i);
                else
                    for (int i = start; i > stop; i += step)
                        list.Items.Add(i);

                return list;
            }));

            env.SetVariable("input", new BuiltinFunction("input", args =>
            {
                if (args.Count > 1) throw new PythonException("TypeError", "input() takes at most 1 argument");
                if (args.Count == 1) Console.Write(args[0]);
                return Console.ReadLine() ?? "";
            }));

            env.SetVariable("abs", new BuiltinFunction("abs", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "abs() takes exactly one argument");
                if (!NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "abs() argument must be a number");
                
                if (args[0] is int i) return Math.Abs(i);
                if (args[0] is double d) return Math.Abs(d);
                throw new PythonException("TypeError", "abs() argument must be a number");
            }));

            env.SetVariable("max", new BuiltinFunction("max", args =>
            {
                if (args.Count == 0) throw new PythonException("TypeError", "max expected at least 1 argument, got 0");
                if (args.Count == 1 && args[0] is PythonList list)
                {
                    if (list.Items.Count == 0) throw new PythonException("ValueError", "max() arg is an empty sequence");
                    return list.Items.Select(NumberHelper.ToDouble).Max();
                }
                return args.Select(NumberHelper.ToDouble).Max();
            }));

            env.SetVariable("min", new BuiltinFunction("min", args =>
            {
                if (args.Count == 0) throw new PythonException("TypeError", "min expected at least 1 argument, got 0");
                if (args.Count == 1 && args[0] is PythonList list)
                {
                    if (list.Items.Count == 0) throw new PythonException("ValueError", "min() arg is an empty sequence");
                    return list.Items.Select(NumberHelper.ToDouble).Min();
                }
                return args.Select(NumberHelper.ToDouble).Min();
            }));

            env.SetVariable("sum", new BuiltinFunction("sum", args =>
            {
                if (args.Count < 1 || args.Count > 2) throw new PythonException("TypeError", "sum() takes 1 or 2 arguments");
                double start = args.Count == 2 ? NumberHelper.ToDouble(args[1]) : 0;

                if (args[0] is PythonList list)
                {
                    var sum = list.Items.Select(NumberHelper.ToDouble).Sum() + start;
                    // Return int if all items are int and result is whole number
                    if (list.Items.All(item => item is int) && sum == Math.Truncate(sum) && args.Count < 2)
                        return (int)sum;
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
                return GetTypeName(args[0]);
            }));

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

        static private string GetTypeName(object obj)
        {
            return obj switch
            {
                null => "NoneType",
                bool => "bool",
                int => "int",
                double => "float",
                string => "str",
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

        public void SetVariable(string name, object value) => variables[name] = value;

        public object GetVariable(string name)
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

        public Dictionary<string, object> GetAllVariables()
        {
            var result = new Dictionary<string, object>();
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