namespace PurePythonInterpreter
{
    // Module System
    public class PythonModule
    {
        public string Name { get; }
        public Environment ModuleEnv { get; }

        public PythonModule(string name)
        {
            Name = name;
            ModuleEnv = new Environment();
        }

        public object GetAttribute(string name) => ModuleEnv.GetVariable(name);
        public void SetAttribute(string name, object value) => ModuleEnv.SetVariable(name, value);
        
        public override string ToString() => $"<module '{Name}'>";
    }

    public static class ModuleSystem
    {
        private static Dictionary<string, PythonModule> loadedModules = new Dictionary<string, PythonModule>();

        public static PythonModule ImportModule(string name)
        {
            if (loadedModules.ContainsKey(name))
                return loadedModules[name];

            var module = CreateBuiltinModule(name);
            if (module != null)
            {
                loadedModules[name] = module;
                return module;
            }

            // Try to load from file
            try
            {
                string filename = name + ".py";
                if (File.Exists(filename))
                {
                    string code = File.ReadAllText(filename);
                    module = new PythonModule(name);
                    
                    var interpreter = new PythonInterpreter();
                    interpreter.SetGlobalEnv(module.ModuleEnv);
                    interpreter.Execute(code);
                    
                    loadedModules[name] = module;
                    return module;
                }
            }
            catch (Exception ex)
            {
                throw new PythonException("ImportError", $"Failed to import module '{name}': {ex.Message}");
            }

            throw new PythonException("ImportError", $"No module named '{name}'");
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
            
            module.SetAttribute("sqrt", new BuiltinFunction("sqrt", args => {
                if (args.Count != 1 || !(args[0] is double)) 
                    throw new PythonException("TypeError", "sqrt() takes exactly one numeric argument");
                return Math.Sqrt((double)args[0]);
            }));
            
            module.SetAttribute("pow", new BuiltinFunction("pow", args => {
                if (args.Count != 2 || !(args[0] is double) || !(args[1] is double)) 
                    throw new PythonException("TypeError", "pow() takes exactly two numeric arguments");
                return Math.Pow((double)args[0], (double)args[1]);
            }));
            
            module.SetAttribute("sin", new BuiltinFunction("sin", args => {
                if (args.Count != 1 || !(args[0] is double)) 
                    throw new PythonException("TypeError", "sin() takes exactly one numeric argument");
                return Math.Sin((double)args[0]);
            }));
            
            module.SetAttribute("cos", new BuiltinFunction("cos", args => {
                if (args.Count != 1 || !(args[0] is double)) 
                    throw new PythonException("TypeError", "cos() takes exactly one numeric argument");
                return Math.Cos((double)args[0]);
            }));
            
            module.SetAttribute("tan", new BuiltinFunction("tan", args => {
                if (args.Count != 1 || !(args[0] is double)) 
                    throw new PythonException("TypeError", "tan() takes exactly one numeric argument");
                return Math.Tan((double)args[0]);
            }));
            
            module.SetAttribute("floor", new BuiltinFunction("floor", args => {
                if (args.Count != 1 || !(args[0] is double)) 
                    throw new PythonException("TypeError", "floor() takes exactly one numeric argument");
                return Math.Floor((double)args[0]);
            }));
            
            module.SetAttribute("ceil", new BuiltinFunction("ceil", args => {
                if (args.Count != 1 || !(args[0] is double)) 
                    throw new PythonException("TypeError", "ceil() takes exactly one numeric argument");
                return Math.Ceiling((double)args[0]);
            }));
            
            return module;
        }

        private static PythonModule CreateRandomModule()
        {
            var module = new PythonModule("random");
            var random = new Random();
            
            module.SetAttribute("random", new BuiltinFunction("random", args => {
                if (args.Count != 0) throw new PythonException("TypeError", "random() takes no arguments");
                return random.NextDouble();
            }));
            
            module.SetAttribute("randint", new BuiltinFunction("randint", args => {
                if (args.Count != 2 || !(args[0] is double) || !(args[1] is double)) 
                    throw new PythonException("TypeError", "randint() takes exactly two integer arguments");
                return (double)random.Next((int)(double)args[0], (int)(double)args[1] + 1);
            }));
            
            module.SetAttribute("choice", new BuiltinFunction("choice", args => {
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
            
            module.SetAttribute("shuffle", new BuiltinFunction("shuffle", args => {
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
            
            module.SetAttribute("getcwd", new BuiltinFunction("getcwd", args => {
                if (args.Count != 0) throw new PythonException("TypeError", "getcwd() takes no arguments");
                return Directory.GetCurrentDirectory();
            }));
            
            module.SetAttribute("listdir", new BuiltinFunction("listdir", args => {
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
            
            module.SetAttribute("exists", new BuiltinFunction("exists", args => {
                if (args.Count != 1) throw new PythonException("TypeError", "exists() takes exactly one argument");
                string path = args[0]?.ToString() ?? "";
                return File.Exists(path) || Directory.Exists(path);
            }));
            
            module.SetAttribute("isfile", new BuiltinFunction("isfile", args => {
                if (args.Count != 1) throw new PythonException("TypeError", "isfile() takes exactly one argument");
                string path = args[0]?.ToString() ?? "";
                return File.Exists(path);
            }));
            
            module.SetAttribute("isdir", new BuiltinFunction("isdir", args => {
                if (args.Count != 1) throw new PythonException("TypeError", "isdir() takes exactly one argument");
                string path = args[0]?.ToString() ?? "";
                return Directory.Exists(path);
            }));
            
            return module;
        }
    }

    // Environment Class (Variable Scope Management)
    public class Environment
    {
        private Dictionary<string, object> variables = new Dictionary<string, object>();
        private Environment parent;

        public Environment(Environment parent = null)
        {
            this.parent = parent;
            if (parent == null) SetupBuiltins();
        }

        private void SetupBuiltins()
        {
            SetVariable("print", new BuiltinFunction("print", args =>
            {
                var output = string.Join(" ", args.Select(arg => {
                    if (arg == null) return "None";
                    if (arg is string s) return s;
                    if (arg is bool b) return b ? "True" : "False";
                    return arg.ToString();
                }));
                Console.WriteLine(output);
                return null;
            }));

            SetVariable("len", new BuiltinFunction("len", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "len() takes exactly one argument");
                var obj = args[0];
                if (obj is string s) return (double)s.Length;
                if (obj is PythonList list) return (double)list.Items.Count;
                if (obj is PythonTuple tuple) return (double)tuple.Items.Count;
                if (obj is PythonDict dict) return (double)dict.Items.Count;
                throw new PythonException("TypeError", $"object of type '{GetTypeName(obj)}' has no len()");
            }));

            SetVariable("str", new BuiltinFunction("str", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "str() takes exactly one argument");
                var obj = args[0];
                if (obj == null) return "None";
                if (obj is bool b) return b ? "True" : "False";
                return obj.ToString();
            }));

            SetVariable("int", new BuiltinFunction("int", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "int() takes exactly one argument");
                if (args[0] is double d) return Math.Truncate(d);
                if (args[0] is bool b) return b ? 1.0 : 0.0;
                if (double.TryParse(args[0]?.ToString(), out var result)) return Math.Truncate(result);
                throw new PythonException("ValueError", $"invalid literal for int(): '{args[0]}'");
            }));

            SetVariable("float", new BuiltinFunction("float", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "float() takes exactly one argument");
                if (args[0] is double d) return d;
                if (args[0] is bool b) return b ? 1.0 : 0.0;
                if (double.TryParse(args[0]?.ToString(), out var result)) return result;
                throw new PythonException("ValueError", $"invalid literal for float(): '{args[0]}'");
            }));

            SetVariable("bool", new BuiltinFunction("bool", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "bool() takes exactly one argument");
                return IsTrue(args[0]);
            }));

            SetVariable("list", new BuiltinFunction("list", args =>
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

            SetVariable("dict", new BuiltinFunction("dict", args => new PythonDict()));

            SetVariable("tuple", new BuiltinFunction("tuple", args =>
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

            SetVariable("range", new BuiltinFunction("range", args =>
            {
                if (args.Count < 1 || args.Count > 3) throw new PythonException("TypeError", "range() takes 1 to 3 arguments");
                
                int start = 0, stop, step = 1;
                
                if (args.Count == 1)
                    stop = (int)(double)args[0];
                else if (args.Count == 2)
                {
                    start = (int)(double)args[0];
                    stop = (int)(double)args[1];
                }
                else
                {
                    start = (int)(double)args[0];
                    stop = (int)(double)args[1];
                    step = (int)(double)args[2];
                    if (step == 0) throw new PythonException("ValueError", "range() step argument must not be zero");
                }
                
                var list = new PythonList();
                if (step > 0)
                    for (int i = start; i < stop; i += step)
                        list.Items.Add((double)i);
                else
                    for (int i = start; i > stop; i += step)
                        list.Items.Add((double)i);
                
                return list;
            }));

            SetVariable("input", new BuiltinFunction("input", args =>
            {
                if (args.Count > 1) throw new PythonException("TypeError", "input() takes at most 1 argument");
                if (args.Count == 1) Console.Write(args[0]);
                return Console.ReadLine() ?? "";
            }));

            SetVariable("abs", new BuiltinFunction("abs", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "abs() takes exactly one argument");
                if (args[0] is double d) return Math.Abs(d);
                throw new PythonException("TypeError", "abs() argument must be a number");
            }));

            SetVariable("max", new BuiltinFunction("max", args =>
            {
                if (args.Count == 0) throw new PythonException("TypeError", "max expected at least 1 argument, got 0");
                if (args.Count == 1 && args[0] is PythonList list)
                {
                    if (list.Items.Count == 0) throw new PythonException("ValueError", "max() arg is an empty sequence");
                    return list.Items.Cast<double>().Max();
                }
                return args.Cast<double>().Max();
            }));

            SetVariable("min", new BuiltinFunction("min", args =>
            {
                if (args.Count == 0) throw new PythonException("TypeError", "min expected at least 1 argument, got 0");
                if (args.Count == 1 && args[0] is PythonList list)
                {
                    if (list.Items.Count == 0) throw new PythonException("ValueError", "min() arg is an empty sequence");
                    return list.Items.Cast<double>().Min();
                }
                return args.Cast<double>().Min();
            }));

            SetVariable("sum", new BuiltinFunction("sum", args =>
            {
                if (args.Count < 1 || args.Count > 2) throw new PythonException("TypeError", "sum() takes 1 or 2 arguments");
                double start = args.Count == 2 ? (double)args[1] : 0;
                
                if (args[0] is PythonList list)
                    return list.Items.Cast<double>().Sum() + start;
                throw new PythonException("TypeError", "sum() argument must be a sequence of numbers");
            }));

            SetVariable("enumerate", new BuiltinFunction("enumerate", args =>
            {
                if (args.Count < 1 || args.Count > 2) throw new PythonException("TypeError", "enumerate() takes 1 or 2 arguments");
                int start = args.Count == 2 ? (int)(double)args[1] : 0;
                
                var result = new PythonList();
                if (args[0] is PythonList list)
                {
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        var tuple = new PythonTuple();
                        tuple.Items.Add((double)(start + i));
                        tuple.Items.Add(list.Items[i]);
                        result.Items.Add(tuple);
                    }
                }
                else if (args[0] is PythonTuple sourceTuple)
                {
                    for (int i = 0; i < sourceTuple.Items.Count; i++)
                    {
                        var tuple = new PythonTuple();
                        tuple.Items.Add((double)(start + i));
                        tuple.Items.Add(sourceTuple.Items[i]);
                        result.Items.Add(tuple);
                    }
                }
                else if (args[0] is string str)
                {
                    for (int i = 0; i < str.Length; i++)
                    {
                        var tuple = new PythonTuple();
                        tuple.Items.Add((double)(start + i));
                        tuple.Items.Add(str[i].ToString());
                        result.Items.Add(tuple);
                    }
                }
                return result;
            }));

            SetVariable("zip", new BuiltinFunction("zip", args =>
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

            SetVariable("type", new BuiltinFunction("type", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "type() takes exactly one argument");
                return GetTypeName(args[0]);
            }));
        }

        private bool IsTrue(object obj)
        {
            if (obj == null) return false;
            if (obj is bool b) return b;
            if (obj is double d) return d != 0;
            if (obj is string s) return !string.IsNullOrEmpty(s);
            if (obj is PythonList l) return l.Items.Count > 0;
            if (obj is PythonTuple t) return t.Items.Count > 0;
            if (obj is PythonDict dict) return dict.Items.Count > 0;
            return true;
        }

        private string GetTypeName(object obj)
        {
            return obj switch
            {
                null => "NoneType",
                bool => "bool",
                double => "int",
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