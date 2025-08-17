// sharppy_builtins.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace SharpPy
{
    /// <summary>
    /// Built-in functions module for SharpPy Python interpreter
    /// Provides all standard Python built-in functions and constants
    /// </summary>
    public static class Builtins
    {
        private static PythonModule _builtinsModule;
        private static readonly object _lock = new object();

        /// <summary>
        /// Get or create the __builtins__ module
        /// </summary>
        public static PythonModule GetBuiltinsModule()
        {
            if (_builtinsModule == null)
            {
                lock (_lock)
                {
                    if (_builtinsModule == null)
                    {
                        _builtinsModule = CreateBuiltinsModule();
                    }
                }
            }
            return _builtinsModule;
        }

        /// <summary>
        /// Setup builtins in the given environment
        /// </summary>
        public static void SetupBuiltins(Environment env)
        {
            var builtins = GetBuiltinsModule();
            
            // REMOVED: 직접 복사하는 부분 제거
            // Copy all builtins to the environment
            // foreach (var kvp in builtins.ModuleEnv.GetAllVariables())
            // {
            //     env.SetVariable(kvp.Key, kvp.Value);
            // }
            
            // Only set __builtins__ module
            env.SetVariable("__builtins__", builtins);
        }

        /// <summary>
        /// Create the __builtins__ module with all built-in functions and constants
        /// </summary>
        private static PythonModule CreateBuiltinsModule()
        {
            var module = new PythonModule("__builtins__");
            
            // Register all built-in functions
            RegisterIOFunctions(module);
            RegisterTypeFunctions(module);
            RegisterCollectionFunctions(module);
            RegisterMathFunctions(module);
            RegisterIteratorFunctions(module);
            RegisterIntrospectionFunctions(module);
            RegisterEvalFunctions(module);
            RegisterConstants(module);
            
            return module;
        }

        /// <summary>
        /// Register I/O related functions
        /// </summary>
        private static void RegisterIOFunctions(PythonModule module)
        {
            // print() function
            module.SetAttribute("print", new BuiltinFunction("print", args =>
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

            // input() function
            module.SetAttribute("input", new BuiltinFunction("input", args =>
            {
                if (args.Count > 1) 
                    throw new PythonException("TypeError", "input() takes at most 1 argument");
                
                if (args.Count == 1 && args[0] is PythonString prompt)
                    Console.Write(prompt.Value);
                
                return new PythonString(Console.ReadLine() ?? "");
            }));

            // open() function
            module.SetAttribute("open", new BuiltinFunction("open", args =>
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
        }

        /// <summary>
        /// Register type conversion and type checking functions
        /// </summary>
        private static void RegisterTypeFunctions(PythonModule module)
        {
            // str() function
            module.SetAttribute("str", new BuiltinFunction("str", args =>
            {
                if (args.Count != 1) 
                    throw new PythonException("TypeError", "str() takes exactly one argument");
                return args[0].ToStr();
            }));

            // int() function
            module.SetAttribute("int", new BuiltinFunction("int", args =>
            {
                if (args.Count != 1) 
                    throw new PythonException("TypeError", "int() takes exactly one argument");
                return args[0].ToInt();
            }));

            // float() function
            module.SetAttribute("float", new BuiltinFunction("float", args =>
            {
                if (args.Count != 1) 
                    throw new PythonException("TypeError", "float() takes exactly one argument");
                return args[0].ToFloat();
            }));

            // bool() function
            module.SetAttribute("bool", new BuiltinFunction("bool", args =>
            {
                if (args.Count != 1) 
                    throw new PythonException("TypeError", "bool() takes exactly one argument");
                return args[0].ToBool();
            }));

            // type() function
            module.SetAttribute("type", new BuiltinFunction("type", args =>
            {
                if (args.Count != 1) 
                    throw new PythonException("TypeError", "type() takes exactly one argument");
                return new PythonString(GetTypeName(args[0]));
            }));

            // isinstance() function
            module.SetAttribute("isinstance", new BuiltinFunction("isinstance", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "isinstance() takes exactly 2 arguments");
                
                var obj = args[0];
                var typeObj = args[1];
                
                // Handle type checking
                if (typeObj is PythonString typeStr)
                {
                    string objType = GetTypeName(obj);
                    return new PythonBool(objType == typeStr.Value);
                }
                else if (typeObj is PythonClass cls)
                {
                    if (obj is PythonInstance inst)
                    {
                        // Check if instance is of this class or derived class
                        PythonClass currentClass = inst.Class;
                        while (currentClass != null)
                        {
                            if (ReferenceEquals(currentClass, cls))
                                return PythonBool.True;
                            currentClass = currentClass.ParentClass;
                        }
                        return PythonBool.False;
                    }
                    return PythonBool.False;
                }
                
                throw new PythonException("TypeError", "isinstance() arg 2 must be a type or class");
            }));
        }

        /// <summary>
        /// Register collection-related functions
        /// </summary>
        private static void RegisterCollectionFunctions(PythonModule module)
        {
            // len() function
            module.SetAttribute("len", new BuiltinFunction("len", args =>
            {
                if (args.Count != 1) 
                    throw new PythonException("TypeError", "len() takes exactly one argument");
                
                var obj = args[0];
                return obj switch
                {
                    PythonString s => PythonInt.Create(s.Length),
                    PythonList list => PythonInt.Create(list.Items.Count),
                    PythonTuple tuple => PythonInt.Create(tuple.Items.Count),
                    PythonDict dict => PythonInt.Create(dict.Items.Count),
                    _ => throw new PythonException("TypeError", $"object of type '{obj.Type}' has no len()")
                };
            }));

            // list() function
            module.SetAttribute("list", new BuiltinFunction("list", args =>
            {
                var list = new PythonList();
                if (args.Count == 1)
                {
                    var obj = args[0];
                    switch (obj)
                    {
                        case PythonList sourceList:
                            list.Items.AddRange(sourceList.Items);
                            break;
                        case PythonTuple sourceTuple:
                            list.Items.AddRange(sourceTuple.Items);
                            break;
                        case PythonString str:
                            foreach (char c in str.Value)
                                list.Items.Add(new PythonString(c.ToString()));
                            break;
                        case PythonDict dict:
                            list.Items.AddRange(dict.Items.Keys);
                            break;
                    }
                }
                return list;
            }));

            // dict() function
            module.SetAttribute("dict", new BuiltinFunction("dict", args => new PythonDict()));

            // tuple() function
            module.SetAttribute("tuple", new BuiltinFunction("tuple", args =>
            {
                var tuple = new PythonTuple();
                if (args.Count == 1)
                {
                    var obj = args[0];
                    switch (obj)
                    {
                        case PythonList sourceList:
                            tuple.Items.AddRange(sourceList.Items);
                            break;
                        case PythonTuple sourceTuple:
                            tuple.Items.AddRange(sourceTuple.Items);
                            break;
                        case PythonString str:
                            foreach (char c in str.Value)
                                tuple.Items.Add(new PythonString(c.ToString()));
                            break;
                        case PythonDict dict:
                            tuple.Items.AddRange(dict.Items.Keys);
                            break;
                    }
                }
                return tuple;
            }));

            // set() function (basic implementation)
            module.SetAttribute("set", new BuiltinFunction("set", args =>
            {
                // For now, return a list with unique items
                var list = new PythonList();
                if (args.Count == 1)
                {
                    var seen = new HashSet<string>();
                    var items = new List<PythonTypeObject>();
                    
                    switch (args[0])
                    {
                        case PythonList sourceList:
                            items = sourceList.Items;
                            break;
                        case PythonTuple sourceTuple:
                            items = sourceTuple.Items;
                            break;
                        case PythonString str:
                            foreach (char c in str.Value)
                                items.Add(new PythonString(c.ToString()));
                            break;
                    }
                    
                    foreach (var item in items)
                    {
                        var key = item.ToPythonString();
                        if (!seen.Contains(key))
                        {
                            seen.Add(key);
                            list.Items.Add(item);
                        }
                    }
                }
                return list;
            }));

            // range() function
            module.SetAttribute("range", new BuiltinFunction("range", args =>
            {
                if (args.Count < 1 || args.Count > 3) 
                    throw new PythonException("TypeError", "range() takes 1 to 3 arguments");

                int start = 0, stop, step = 1;

                if (args.Count == 1)
                {
                    stop = NumberHelper.ToInt(args[0]);
                }
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
                    if (step == 0) 
                        throw new PythonException("ValueError", "range() step argument must not be zero");
                }

                var list = new PythonList();
                if (step > 0)
                {
                    for (int i = start; i < stop; i += step)
                        list.Items.Add(PythonInt.Create(i));
                }
                else
                {
                    for (int i = start; i > stop; i += step)
                        list.Items.Add(PythonInt.Create(i));
                }

                return list;
            }));
        }

        /// <summary>
        /// Register math-related functions
        /// </summary>
        private static void RegisterMathFunctions(PythonModule module)
        {
            // abs() function
            module.SetAttribute("abs", new BuiltinFunction("abs", args =>
            {
                if (args.Count != 1) 
                    throw new PythonException("TypeError", "abs() takes exactly one argument");
                
                if (!NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "abs() argument must be a number");

                return args[0] switch
                {
                    PythonInt pi => PythonInt.Create(Math.Abs(pi.Value)),
                    PythonFloat pf => new PythonFloat(Math.Abs(pf.Value)),
                    _ => throw new PythonException("TypeError", "abs() argument must be a number")
                };
            }));

            // max() function
            module.SetAttribute("max", new BuiltinFunction("max", args =>
            {
                if (args.Count == 0) 
                    throw new PythonException("TypeError", "max expected at least 1 argument, got 0");
                
                if (args.Count == 1 && args[0] is PythonList list)
                {
                    if (list.Items.Count == 0) 
                        throw new PythonException("ValueError", "max() arg is an empty sequence");

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

            // min() function
            module.SetAttribute("min", new BuiltinFunction("min", args =>
            {
                if (args.Count == 0) 
                    throw new PythonException("TypeError", "min expected at least 1 argument, got 0");
                
                if (args.Count == 1 && args[0] is PythonList list)
                {
                    if (list.Items.Count == 0) 
                        throw new PythonException("ValueError", "min() arg is an empty sequence");

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

            // sum() function
            module.SetAttribute("sum", new BuiltinFunction("sum", args =>
            {
                if (args.Count < 1 || args.Count > 2) 
                    throw new PythonException("TypeError", "sum() takes 1 or 2 arguments");
                
                PythonTypeObject start = args.Count == 2 ? args[1] : PythonInt.Create(0);

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

            // round() function
            module.SetAttribute("round", new BuiltinFunction("round", args =>
            {
                if (args.Count < 1 || args.Count > 2)
                    throw new PythonException("TypeError", "round() takes 1 or 2 arguments");
                
                if (!NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "round() first argument must be a number");
                
                double value = NumberHelper.ToDouble(args[0]);
                int digits = args.Count == 2 ? NumberHelper.ToInt(args[1]) : 0;
                
                double rounded = Math.Round(value, digits);
                
                if (digits == 0 && args[0] is PythonInt)
                    return PythonInt.Create((int)rounded);
                
                return new PythonFloat(rounded);
            }));

            // pow() function
            module.SetAttribute("pow", new BuiltinFunction("pow", args =>
            {
                if (args.Count < 2 || args.Count > 3)
                    throw new PythonException("TypeError", "pow() takes 2 or 3 arguments");
                
                if (!NumberHelper.IsNumber(args[0]) || !NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "pow() arguments must be numbers");
                
                var result = NumberHelper.Power(args[0], args[1]);
                
                if (args.Count == 3)
                {
                    if (!NumberHelper.IsNumber(args[2]))
                        throw new PythonException("TypeError", "pow() 3rd argument must be a number");
                    result = NumberHelper.Modulo(result, args[2]);
                }
                
                return result;
            }));
        }

        /// <summary>
        /// Register iterator and functional programming functions
        /// </summary>
        private static void RegisterIteratorFunctions(PythonModule module)
        {
            // enumerate() function
            module.SetAttribute("enumerate", new BuiltinFunction("enumerate", args =>
            {
                if (args.Count < 1 || args.Count > 2) 
                    throw new PythonException("TypeError", "enumerate() takes 1 or 2 arguments");
                
                int start = args.Count == 2 ? NumberHelper.ToInt(args[1]) : 0;
                var result = new PythonList();
                
                List<PythonTypeObject> items = args[0] switch
                {
                    PythonList list => list.Items,
                    PythonTuple tuple => tuple.Items,
                    PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
                    _ => throw new PythonException("TypeError", "enumerate() argument must be iterable")
                };

                for (int i = 0; i < items.Count; i++)
                {
                    var enumTuple = new PythonTuple();
                    enumTuple.Items.Add(PythonInt.Create(start + i));
                    enumTuple.Items.Add(items[i]);
                    result.Items.Add(enumTuple);
                }

                return result;
            }));

            // zip() function
            module.SetAttribute("zip", new BuiltinFunction("zip", args =>
            {
                if (args.Count == 0) return new PythonList();

                var iterables = new List<List<PythonTypeObject>>();
                
                foreach (var arg in args)
                {
                    List<PythonTypeObject> items = arg switch
                    {
                        PythonList list => list.Items,
                        PythonTuple tuple => tuple.Items,
                        PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
                        _ => throw new PythonException("TypeError", "zip argument must be iterable")
                    };
                    iterables.Add(items);
                }

                var result = new PythonList();
                int minLen = iterables.Min(it => it.Count);

                for (int i = 0; i < minLen; i++)
                {
                    var tuple = new PythonTuple();
                    foreach (var iterable in iterables)
                        tuple.Items.Add(iterable[i]);
                    result.Items.Add(tuple);
                }

                return result;
            }));

            // map() function
            module.SetAttribute("map", new BuiltinFunction("map", args =>
            {
                if (args.Count != 2) 
                    throw new PythonException("TypeError", "map() takes exactly 2 arguments");
                
                if (!(args[0] is Function function))
                    throw new PythonException("TypeError", "map() first argument must be callable");

                List<PythonTypeObject> items = args[1] switch
                {
                    PythonList list => list.Items,
                    PythonTuple tuple => tuple.Items,
                    PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
                    _ => throw new PythonException("TypeError", "map() second argument must be iterable")
                };

                var result = new PythonList();
                foreach (var item in items)
                {
                    var mappedValue = function.Call(new List<PythonTypeObject> { item });
                    result.Items.Add(mappedValue);
                }
                return result;
            }));

            // filter() function
            module.SetAttribute("filter", new BuiltinFunction("filter", args =>
            {
                if (args.Count != 2) 
                    throw new PythonException("TypeError", "filter() takes exactly 2 arguments");

                List<PythonTypeObject> items = args[1] switch
                {
                    PythonList list => list.Items,
                    PythonTuple tuple => tuple.Items,
                    PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
                    _ => throw new PythonException("TypeError", "filter() second argument must be iterable")
                };

                var result = new PythonList();

                if (args[0] is PythonNone)
                {
                    // filter(None, iterable) filters truthy values
                    foreach (var item in items)
                    {
                        if (item.IsTrue())
                            result.Items.Add(item);
                    }
                }
                else if (args[0] is Function function)
                {
                    foreach (var item in items)
                    {
                        var filterResult = function.Call(new List<PythonTypeObject> { item });
                        if (filterResult.IsTrue())
                            result.Items.Add(item);
                    }
                }
                else
                {
                    throw new PythonException("TypeError", "filter() first argument must be None or callable");
                }

                return result;
            }));

            // sorted() function
            module.SetAttribute("sorted", new BuiltinFunction("sorted", args =>
            {
                if (args.Count < 1 || args.Count > 2) 
                    throw new PythonException("TypeError", "sorted() takes 1 or 2 arguments");
                
                List<PythonTypeObject> items = args[0] switch
                {
                    PythonList list => new List<PythonTypeObject>(list.Items),
                    PythonTuple tuple => new List<PythonTypeObject>(tuple.Items),
                    PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
                    _ => throw new PythonException("TypeError", "sorted() argument must be iterable")
                };

                var keyFunc = args.Count > 1 ? args[1] : null;

                if (keyFunc == null || keyFunc is PythonNone)
                {
                    // Default sorting
                    items.Sort((a, b) =>
                    {
                        if (NumberHelper.IsNumber(a) && NumberHelper.IsNumber(b))
                            return NumberHelper.ToDouble(a).CompareTo(NumberHelper.ToDouble(b));
                        if (a is PythonString sa && b is PythonString sb) 
                            return string.Compare(sa.Value, sb.Value);
                        return 0;
                    });
                }
                else if (keyFunc is Function function)
                {
                    // Sort with key function
                    var keyed = items
                        .Select(item => new 
                        { 
                            Item = item, 
                            Key = function.Call(new List<PythonTypeObject> { item }) 
                        })
                        .ToList();
                    
                    keyed.Sort((a, b) =>
                    {
                        if (NumberHelper.IsNumber(a.Key) && NumberHelper.IsNumber(b.Key))
                            return NumberHelper.ToDouble(a.Key).CompareTo(NumberHelper.ToDouble(b.Key));
                        if (a.Key is PythonString sa && b.Key is PythonString sb) 
                            return string.Compare(sa.Value, sb.Value);
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

            // any() function
            module.SetAttribute("any", new BuiltinFunction("any", args =>
            {
                if (args.Count != 1) 
                    throw new PythonException("TypeError", "any() takes exactly one argument");

                List<PythonTypeObject> items = args[0] switch
                {
                    PythonList list => list.Items,
                    PythonTuple tuple => tuple.Items,
                    PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
                    _ => throw new PythonException("TypeError", "any() argument must be iterable")
                };

                foreach (var item in items)
                {
                    if (item.IsTrue())
                        return PythonBool.True;
                }
                return PythonBool.False;
            }));

            // all() function
            module.SetAttribute("all", new BuiltinFunction("all", args =>
            {
                if (args.Count != 1) 
                    throw new PythonException("TypeError", "all() takes exactly one argument");

                List<PythonTypeObject> items = args[0] switch
                {
                    PythonList list => list.Items,
                    PythonTuple tuple => tuple.Items,
                    PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
                    _ => throw new PythonException("TypeError", "all() argument must be iterable")
                };

                foreach (var item in items)
                {
                    if (!item.IsTrue())
                        return PythonBool.False;
                }
                return PythonBool.True;
            }));

            // reversed() function
            module.SetAttribute("reversed", new BuiltinFunction("reversed", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "reversed() takes exactly one argument");

                List<PythonTypeObject> items = args[0] switch
                {
                    PythonList list => new List<PythonTypeObject>(list.Items),
                    PythonTuple tuple => new List<PythonTypeObject>(tuple.Items),
                    PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
                    _ => throw new PythonException("TypeError", "reversed() argument must be a sequence")
                };

                items.Reverse();
                var result = new PythonList();
                result.Items.AddRange(items);
                return result;
            }));
        }

        /// <summary>
        /// Register introspection and attribute functions
        /// </summary>
        private static void RegisterIntrospectionFunctions(PythonModule module)
        {
            // globals() function
            module.SetAttribute("globals", new BuiltinFunction("globals", args =>
            {
                if (args.Count != 0) 
                    throw new PythonException("TypeError", "globals() takes no arguments");
                
                // This needs to be handled specially by the interpreter
                // For now, return an empty dict
                return new PythonDict();
            }));

            // locals() function
            module.SetAttribute("locals", new BuiltinFunction("locals", args =>
            {
                if (args.Count != 0) 
                    throw new PythonException("TypeError", "locals() takes no arguments");
                
                // This needs to be handled specially by the interpreter
                // For now, return an empty dict
                return new PythonDict();
            }));

            // dir() function
            module.SetAttribute("dir", new BuiltinFunction("dir", args =>
            {
                if (args.Count > 1) 
                    throw new PythonException("TypeError", "dir() takes at most 1 argument");
                
                var result = new PythonList();
                
                if (args.Count == 0)
                {
                    // Return current scope names - needs special handling
                    return result;
                }
                
                // Return attributes of the object
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
                    case PythonInstance instance:
                        foreach (var key in instance.InstanceEnv.variables.Keys)
                            attributes.Add(key);
                        
                        PythonClass currentClass = instance.Class;
                        while (currentClass != null)
                        {
                            foreach (var key in currentClass.ClassEnv.variables.Keys)
                                attributes.Add(key);
                            currentClass = currentClass.ParentClass;
                        }
                        break;
                        
                    case PythonClass cls:
                        var classVars = cls.ClassEnv.GetAllVariables();
                        foreach (var kvp in classVars)
                            attributes.Add(kvp.Key);
                        break;
                        
                    case PythonModule mod:
                        var moduleVars = mod.ModuleEnv.GetAllVariables();
                        foreach (var kvp in moduleVars)
                            attributes.Add(kvp.Key);
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
                
                return result;
            }));

            // hasattr() function
            module.SetAttribute("hasattr", new BuiltinFunction("hasattr", args =>
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
                            return PythonBool.True;
                            
                        case PythonClass cls:
                            cls.ClassEnv.GetVariable(name);
                            return PythonBool.True;
                            
                        case PythonModule mod:
                            mod.GetAttribute(name);
                            return PythonBool.True;
                            
                        case PythonList:
                        case PythonDict:
                        case PythonTuple:
                        case PythonString:
                            try
                            {
                                obj.GetMethod(name);
                                return PythonBool.True;
                            }
                            catch
                            {
                                return PythonBool.False;
                            }
                            
                        default:
                            var builtinAttrs = new HashSet<string> 
                            { 
                                "__class__", "__repr__", "__str__", "__hash__", "__eq__" 
                            };
                            return PythonBool.Create(builtinAttrs.Contains(name));
                    }
                }
                catch
                {
                    return PythonBool.False;
                }
            }));

            // getattr() function
            module.SetAttribute("getattr", new BuiltinFunction("getattr", args =>
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
                            
                        case PythonModule mod:
                            return mod.GetAttribute(name);
                            
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
                            if (name == "__class__")
                                return new PythonString(GetTypeName(obj));
                            if (name == "__repr__" || name == "__str__")
                                return new BuiltinFunction(name, _ => new PythonString(obj.ToPythonString()));
                                
                            if (defaultValue != null)
                                return defaultValue;
                            throw new PythonException("AttributeError", 
                                $"'{GetTypeName(obj)}' object has no attribute '{name}'");
                    }
                }
                catch (PythonException ex) when (ex.Type == "AttributeError" && defaultValue != null)
                {
                    return defaultValue;
                }
            }));

            // setattr() function
            module.SetAttribute("setattr", new BuiltinFunction("setattr", args =>
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
                        
                    case PythonModule mod:
                        mod.SetAttribute(name, value);
                        return PythonNone.Instance;
                        
                    default:
                        throw new PythonException("AttributeError", 
                            $"'{GetTypeName(obj)}' object attribute '{name}' is read-only");
                }
            }));

            // delattr() function
            module.SetAttribute("delattr", new BuiltinFunction("delattr", args =>
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
                        
                    case PythonModule mod:
                        mod.ModuleEnv.DeleteVariable(name);
                        return PythonNone.Instance;
                        
                    default:
                        throw new PythonException("AttributeError", 
                            $"'{GetTypeName(obj)}' object attribute '{name}' cannot be deleted");
                }
            }));

            // id() function
            module.SetAttribute("id", new BuiltinFunction("id", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "id() takes exactly one argument");
                
                // Return hash code as object id
                return PythonInt.Create(args[0].GetHashCode());
            }));

            // repr() function
            module.SetAttribute("repr", new BuiltinFunction("repr", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "repr() takes exactly one argument");
                
                return new PythonString(args[0].ToPythonString());
            }));
        }

        /// <summary>
        /// Register eval/exec/compile functions
        /// </summary>
        private static void RegisterEvalFunctions(PythonModule module)
        {
            // These functions need special handling by the interpreter
            // We'll register placeholder implementations

            // eval() function
            module.SetAttribute("eval", new BuiltinFunction("eval", args =>
            {
                throw new PythonException("NotImplementedError", 
                    "eval() must be implemented by the interpreter");
            }));

            // exec() function
            module.SetAttribute("exec", new BuiltinFunction("exec", args =>
            {
                throw new PythonException("NotImplementedError", 
                    "exec() must be implemented by the interpreter");
            }));

            // compile() function
            module.SetAttribute("compile", new BuiltinFunction("compile", args =>
            {
                throw new PythonException("NotImplementedError", 
                    "compile() must be implemented by the interpreter");
            }));
        }

        /// <summary>
        /// Register built-in constants
        /// </summary>
        private static void RegisterConstants(PythonModule module)
        {
            // None constant
            module.SetAttribute("None", PythonNone.Instance);
            
            // Boolean constants
            module.SetAttribute("True", PythonBool.True);
            module.SetAttribute("False", PythonBool.False);
            
            // Special constants
            module.SetAttribute("NotImplemented", new PythonString("NotImplemented"));
            module.SetAttribute("Ellipsis", new PythonString("..."));
            
            // Version info (mock)
            var versionTuple = new PythonTuple();
            versionTuple.Items.Add(PythonInt.Create(3));
            versionTuple.Items.Add(PythonInt.Create(10));
            versionTuple.Items.Add(PythonInt.Create(0));
            module.SetAttribute("__version__", versionTuple);
        }

        // Helper methods
        private static int CompareValues(PythonTypeObject a, PythonTypeObject b)
        {
            if (NumberHelper.IsNumber(a) && NumberHelper.IsNumber(b))
                return NumberHelper.ToDouble(a).CompareTo(NumberHelper.ToDouble(b));
            if (a is PythonString sa && b is PythonString sb)
                return string.Compare(sa.Value, sb.Value);
            return 0;
        }

        private static string GetTypeName(PythonTypeObject obj)
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
                PythonModule mod => $"module '{mod.Name}'",
                _ => obj.GetType().Name
            };
        }
    }
}
