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

            env.SetVariable("type", new BuiltinFunction("type", args =>
            {
                if (args.Count != 1) throw new PythonException("TypeError", "type() takes exactly one argument");
                return new PythonString(GetTypeName(args[0]));
            }));

            // Add more builtin functions here...
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