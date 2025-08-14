// enhanced_python_types.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    // Python Data Types (Updated for int/float support)
    public class PythonList
    {
        public List<object> Items { get; } = new List<object>();

        public BuiltinFunction GetMethod(string name)
        {
            return name switch
            {
                "append" => new BuiltinFunction("append", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "append() takes exactly one argument");
                    Items.Add(args[0]);
                    return null;
                }),
                "extend" => new BuiltinFunction("extend", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "extend() takes exactly one argument");
                    if (args[0] is PythonList other)
                        Items.AddRange(other.Items);
                    else if (args[0] is PythonTuple tuple)
                        Items.AddRange(tuple.Items);
                    else if (args[0] is string str)
                        Items.AddRange(str.Select(c => c.ToString()));
                    else throw new PythonException("TypeError", "extend() argument must be iterable");
                    return null;
                }),
                "insert" => new BuiltinFunction("insert", args =>
                {
                    if (args.Count != 2) throw new PythonException("TypeError", "insert() takes exactly two arguments");
                    if (!NumberHelper.IsNumber(args[0]))
                        throw new PythonException("TypeError", "insert() first argument must be an integer");
                    
                    int i = NumberHelper.ToInt(args[0]);
                    var value = args[1];
                    if (i < 0) i = Math.Max(0, Items.Count + i);
                    if (i > Items.Count) i = Items.Count;
                    Items.Insert(i, value);
                    return null;
                }),
                "remove" => new BuiltinFunction("remove", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "remove() takes exactly one argument");
                    if (!Items.Remove(args[0]))
                        throw new PythonException("ValueError", "list.remove(x): x not in list");
                    return null;
                }),
                "pop" => new BuiltinFunction("pop", args =>
                {
                    if (args.Count > 1) throw new PythonException("TypeError", "pop() takes at most 1 argument");
                    if (Items.Count == 0) throw new PythonException("IndexError", "pop from empty list");

                    int index = args.Count == 0 ? Items.Count - 1 : NumberHelper.ToInt(args[0]);
                    if (index < 0) index += Items.Count;
                    if (index < 0 || index >= Items.Count) throw new PythonException("IndexError", "pop index out of range");

                    var item = Items[index];
                    Items.RemoveAt(index);
                    return item;
                }),
                "clear" => new BuiltinFunction("clear", args =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "clear() takes no arguments");
                    Items.Clear();
                    return null;
                }),
                "index" => new BuiltinFunction("index", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "index() takes exactly one argument");
                    int idx = Items.IndexOf(args[0]);
                    if (idx == -1) throw new PythonException("ValueError", $"{args[0]} is not in list");
                    return idx;
                }),
                "count" => new BuiltinFunction("count", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "count() takes exactly one argument");
                    return Items.Count(item => Equals(item, args[0]));
                }),
                "sort" => new BuiltinFunction("sort", args =>
                {
                    if (args.Count > 1) throw new PythonException("TypeError", "sort() takes at most 1 argument");
                    Items.Sort((a, b) =>
                    {
                        if (NumberHelper.IsNumber(a) && NumberHelper.IsNumber(b))
                            return NumberHelper.ToDouble(a).CompareTo(NumberHelper.ToDouble(b));
                        if (a is string sa && b is string sb) return sa.CompareTo(sb);
                        return 0;
                    });
                    return null;
                }),
                "reverse" => new BuiltinFunction("reverse", args =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "reverse() takes no arguments");
                    Items.Reverse();
                    return null;
                }),
                _ => throw new PythonException("AttributeError", $"'list' object has no attribute '{name}'")
            };
        }

        public override string ToString()
        {
            return "[" + string.Join(", ", Items.Select(FormatItem)) + "]";
        }

        private string FormatItem(object item)
        {
            if (item is string s) return $"'{s}'";
            if (item == null) return "None";
            if (item is bool b) return b ? "True" : "False";
            return item.ToString();
        }
    }

    public class PythonTuple
    {
        public List<object> Items { get; } = new List<object>();

        public BuiltinFunction GetMethod(string name)
        {
            return name switch
            {
                "count" => new BuiltinFunction("count", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "count() takes exactly one argument");
                    return Items.Count(item => Equals(item, args[0]));
                }),
                "index" => new BuiltinFunction("index", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "index() takes exactly one argument");
                    int idx = Items.IndexOf(args[0]);
                    if (idx == -1) throw new PythonException("ValueError", $"{args[0]} is not in tuple");
                    return idx;
                }),
                _ => throw new PythonException("AttributeError", $"'tuple' object has no attribute '{name}'")
            };
        }

        public override string ToString()
        {
            if (Items.Count == 0) return "()";
            if (Items.Count == 1) return $"({FormatItem(Items[0])},)";
            return "(" + string.Join(", ", Items.Select(FormatItem)) + ")";
        }

        private string FormatItem(object item)
        {
            if (item is string s) return $"'{s}'";
            if (item == null) return "None";
            if (item is bool b) return b ? "True" : "False";
            return item.ToString();
        }
    }

    public class PythonDict
    {
        public Dictionary<object, object> Items { get; } = new Dictionary<object, object>();

        public BuiltinFunction GetMethod(string name)
        {
            return name switch
            {
                "get" => new BuiltinFunction("get", args =>
                {
                    if (args.Count < 1 || args.Count > 2) throw new PythonException("TypeError", "get() takes 1 or 2 arguments");
                    var key = args[0];
                    var defaultValue = args.Count == 2 ? args[1] : null;
                    return Items.ContainsKey(key) ? Items[key] : defaultValue;
                }),
                "keys" => new BuiltinFunction("keys", args =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "keys() takes no arguments");
                    var list = new PythonList();
                    list.Items.AddRange(Items.Keys);
                    return list;
                }),
                "values" => new BuiltinFunction("values", args =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "values() takes no arguments");
                    var list = new PythonList();
                    list.Items.AddRange(Items.Values);
                    return list;
                }),
                "items" => new BuiltinFunction("items", args =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "items() takes no arguments");
                    var list = new PythonList();
                    foreach (var kvp in Items)
                    {
                        var tuple = new PythonTuple();
                        tuple.Items.Add(kvp.Key);
                        tuple.Items.Add(kvp.Value);
                        list.Items.Add(tuple);
                    }
                    return list;
                }),
                "pop" => new BuiltinFunction("pop", args =>
                {
                    if (args.Count < 1 || args.Count > 2) throw new PythonException("TypeError", "pop() takes 1 or 2 arguments");
                    var key = args[0];
                    if (Items.ContainsKey(key))
                    {
                        var value = Items[key];
                        Items.Remove(key);
                        return value;
                    }
                    if (args.Count == 2) return args[1];
                    throw new PythonException("KeyError", $"KeyError: {key}");
                }),
                "clear" => new BuiltinFunction("clear", args =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "clear() takes no arguments");
                    Items.Clear();
                    return null;
                }),
                "update" => new BuiltinFunction("update", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "update() takes exactly one argument");
                    if (args[0] is PythonDict other)
                    {
                        foreach (var kvp in other.Items)
                            Items[kvp.Key] = kvp.Value;
                    }
                    else
                    {
                        throw new PythonException("TypeError", "update() argument must be a dict");
                    }
                    return null;
                }),
                _ => throw new PythonException("AttributeError", $"'dict' object has no attribute '{name}'")
            };
        }

        public override string ToString()
        {
            if (Items.Count == 0) return "{}";
            var pairs = Items.Select(kvp => $"{FormatItem(kvp.Key)}: {FormatItem(kvp.Value)}");
            return "{" + string.Join(", ", pairs) + "}";
        }

        private string FormatItem(object item)
        {
            if (item is string s) return $"'{s}'";
            if (item == null) return "None";
            if (item is bool b) return b ? "True" : "False";
            return item.ToString();
        }
    }

    // Function Classes (Enhanced with better error handling)
    public abstract class Function
    {
        public string Name { get; }
        protected Function(string name) => Name = name;
        public abstract object Call(List<object> arguments);

        public override string ToString() => $"<function {Name}>";
    }

    public class UserFunction : Function
    {
        public List<Parameter> Parameters { get; }
        public List<ASTNode> Body { get; }
        public Environment ClosureEnv { get; }
        public TypeHint ReturnTypeHint { get; }

        public UserFunction(string name, List<Parameter> parameters, List<ASTNode> body, Environment closureEnv, TypeHint returnTypeHint = null)
            : base(name)
        {
            Parameters = parameters;
            Body = body;
            ClosureEnv = closureEnv;
            ReturnTypeHint = returnTypeHint;
        }

        public override object Call(List<object> arguments)
        {
            if (arguments.Count != Parameters.Count)
                throw new PythonException("TypeError", $"Function {Name} expects {Parameters.Count} arguments, got {arguments.Count}");

            var funcEnv = new Environment(ClosureEnv);

            // Type checking for parameters
            for (int i = 0; i < Parameters.Count; i++)
            {
                var param = Parameters[i];
                var arg = arguments[i];

                if (param.TypeHint != null && !param.TypeHint.IsCompatible(arg))
                    throw new PythonException("TypeError", $"Argument {i + 1} for parameter '{param.Name}' expected {param.TypeHint}, got {GetValueType(arg)}");

                funcEnv.SetVariable(param.Name, arg);
            }

            try
            {
                object result = null;
                foreach (var stmt in Body)
                    result = stmt.Evaluate(funcEnv);

                // Type checking for return value
                if (ReturnTypeHint != null && result != null && !ReturnTypeHint.IsCompatible(result))
                    throw new PythonException("TypeError", $"Return value expected {ReturnTypeHint}, got {GetValueType(result)}");

                return result;
            }
            catch (ReturnException ex)
            {
                // Type checking for return value
                if (ReturnTypeHint != null && ex.Value != null && !ReturnTypeHint.IsCompatible(ex.Value))
                    throw new PythonException("TypeError", $"Return value expected {ReturnTypeHint}, got {GetValueType(ex.Value)}");
                return ex.Value;
            }
        }

        private string GetValueType(object value)
        {
            return value switch
            {
                int => "int",
                double => "float",
                string => "str",
                bool => "bool",
                null => "None",
                PythonList => "list",
                PythonTuple => "tuple",
                PythonDict => "dict",
                Function => "function",
                PythonClass => "class",
                PythonInstance => "object",
                _ => value.GetType().Name
            };
        }
    }

    public class BuiltinFunction : Function
    {
        private Func<List<object>, object> implementation;

        public BuiltinFunction(string name, Func<List<object>, object> impl) : base(name)
        {
            implementation = impl;
        }

        public override object Call(List<object> arguments) => implementation(arguments);
    }

    // Bound Method Class (for instance methods)
    public class BoundMethod : Function
    {
        private UserFunction method;
        private object instance;

        public BoundMethod(string name, UserFunction method, object instance) : base(name)
        {
            this.method = method;
            this.instance = instance;
        }

        public override object Call(List<object> arguments)
        {
            // Prepend 'self' to the arguments
            var newArgs = new List<object> { instance };
            newArgs.AddRange(arguments);
            return method.Call(newArgs);
        }

        public override string ToString() => $"<bound method {Name}>";
    }

    // Class System
    public class PythonClass
    {
        public string Name { get; }
        public Environment ClassEnv { get; }
        public PythonClass ParentClass { get; }

        public PythonClass(string name, Environment classEnv, PythonClass parentClass = null)
        {
            Name = name;
            ClassEnv = classEnv;
            ParentClass = parentClass;
        }

        public PythonInstance CreateInstance(List<object> args = null)
        {
            var instance = new PythonInstance(this);

            // __init__ 메서드가 있다면 호출
            if (ClassEnv.HasVariable("__init__"))
            {
                var initMethod = ClassEnv.GetVariable("__init__") as Function;
                if (initMethod != null)
                {
                    var initArgs = new List<object> { instance };
                    if (args != null) initArgs.AddRange(args);
                    initMethod.Call(initArgs);
                }
            }

            return instance;
        }

        public override string ToString() => $"<class '{Name}'>";
    }

    public class PythonInstance
    {
        public PythonClass Class { get; }
        public Environment InstanceEnv { get; }

        public PythonInstance(PythonClass pythonClass)
        {
            Class = pythonClass;
            InstanceEnv = new Environment(pythonClass.ClassEnv);
        }

        public object GetAttribute(string name)
        {
            try
            {
                var value = InstanceEnv.GetVariable(name);

                // If it's a user function, bind it to this instance
                if (value is UserFunction userFunction)
                {
                    return new BoundMethod(name, userFunction, this);
                }

                return value;
            }
            catch (PythonException)
            {
                throw new PythonException("AttributeError", $"'{Class.Name}' object has no attribute '{name}'");
            }
        }

        public void SetAttribute(string name, object value) => InstanceEnv.SetVariable(name, value);

        public override string ToString() => $"<{Class.Name} object>";
    }
}