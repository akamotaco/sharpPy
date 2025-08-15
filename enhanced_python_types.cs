// enhanced_python_types.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    // Python Data Types (Updated to inherit from PythonTypeObject)
    public class PythonList : PythonTypeObject
    {
        public List<PythonTypeObject> Items { get; } = new List<PythonTypeObject>();
        
        public override PythonType Type => PythonType.List;
        public override bool IsTrue() => Items.Count > 0;
        public override bool IsSequence() => true;
        public override object GetRawValue() => this;
        
        public override string ToPythonString()
        {
            return "[" + string.Join(", ", Items.Select(FormatItem)) + "]";
        }
        
        public override bool Equals(PythonTypeObject other)
        {
            if (other is PythonList pl)
            {
                if (Items.Count != pl.Items.Count) return false;
                for (int i = 0; i < Items.Count; i++)
                {
                    if (!Items[i].Equals(pl.Items[i])) return false;
                }
                return true;
            }
            return false;
        }
        
        public override int GetHashCode()
        {
            // Lists are mutable and shouldn't be hashed
            throw new PythonException("TypeError", "unhashable type: 'list'");
        }

        public BuiltinFunction GetMethod(string name)
        {
            return name switch
            {
                "append" => new BuiltinFunction("append", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "append() takes exactly one argument");
                    Items.Add(args[0]);
                    return PythonNone.Instance;
                }),
                "extend" => new BuiltinFunction("extend", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "extend() takes exactly one argument");
                    if (args[0] is PythonList other)
                        Items.AddRange(other.Items);
                    else if (args[0] is PythonTuple tuple)
                        Items.AddRange(tuple.Items);
                    else if (args[0] is PythonString str)
                    {
                        foreach (char c in str.Value)
                            Items.Add(new PythonString(c.ToString()));
                    }
                    else throw new PythonException("TypeError", "extend() argument must be iterable");
                    return PythonNone.Instance;
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
                    return PythonNone.Instance;
                }),
                "remove" => new BuiltinFunction("remove", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "remove() takes exactly one argument");
                    for (int i = 0; i < Items.Count; i++)
                    {
                        if (Items[i].Equals(args[0]))
                        {
                            Items.RemoveAt(i);
                            return PythonNone.Instance;
                        }
                    }
                    throw new PythonException("ValueError", "list.remove(x): x not in list");
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
                    return PythonNone.Instance;
                }),
                "index" => new BuiltinFunction("index", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "index() takes exactly one argument");
                    for (int i = 0; i < Items.Count; i++)
                    {
                        if (Items[i].Equals(args[0]))
                            return new PythonInt(i);
                    }
                    throw new PythonException("ValueError", $"{args[0]} is not in list");
                }),
                "count" => new BuiltinFunction("count", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "count() takes exactly one argument");
                    return new PythonInt(Items.Count(item => item.Equals(args[0])));
                }),
                "sort" => new BuiltinFunction("sort", args =>
                {
                    if (args.Count > 1) throw new PythonException("TypeError", "sort() takes at most 1 argument");
                    Items.Sort((a, b) =>
                    {
                        if (NumberHelper.IsNumber(a) && NumberHelper.IsNumber(b))
                            return NumberHelper.ToDouble(a).CompareTo(NumberHelper.ToDouble(b));
                        if (a is PythonString sa && b is PythonString sb) 
                            return string.Compare(sa.Value, sb.Value);
                        return 0;
                    });
                    return PythonNone.Instance;
                }),
                "reverse" => new BuiltinFunction("reverse", args =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "reverse() takes no arguments");
                    Items.Reverse();
                    return PythonNone.Instance;
                }),
                _ => throw new PythonException("AttributeError", $"'list' object has no attribute '{name}'")
            };
        }

        private string FormatItem(PythonTypeObject item)
        {
            if (item is PythonString s) return $"'{s.Value}'";
            if (item is PythonNone) return "None";
            return item.ToPythonString();
        }
        
        public PythonList Repeat(int times)
        {
            if (times < 0) times = 0;
            var result = new PythonList();
            for (int i = 0; i < times; i++)
                result.Items.AddRange(Items);
            return result;
        }
        
        public PythonTypeObject GetItem(int index)
        {
            if (index < 0) index += Items.Count;
            if (index < 0 || index >= Items.Count)
                throw new PythonException("IndexError", "list index out of range");
            return Items[index];
        }
        
        public void SetItem(int index, PythonTypeObject value)
        {
            if (index < 0) index += Items.Count;
            if (index < 0 || index >= Items.Count)
                throw new PythonException("IndexError", "list assignment index out of range");
            Items[index] = value;
        }
    }

    public class PythonTuple : PythonTypeObject
    {
        public List<PythonTypeObject> Items { get; } = new List<PythonTypeObject>();
        
        public override PythonType Type => PythonType.Tuple;
        public override bool IsTrue() => Items.Count > 0;
        public override bool IsSequence() => true;
        public override object GetRawValue() => this;
        
        public override string ToPythonString()
        {
            if (Items.Count == 0) return "()";
            if (Items.Count == 1) return $"({FormatItem(Items[0])},)";
            return "(" + string.Join(", ", Items.Select(FormatItem)) + ")";
        }
        
        public override bool Equals(PythonTypeObject other)
        {
            if (other is PythonTuple pt)
            {
                if (Items.Count != pt.Items.Count) return false;
                for (int i = 0; i < Items.Count; i++)
                {
                    if (!Items[i].Equals(pt.Items[i])) return false;
                }
                return true;
            }
            return false;
        }
        
        public override int GetHashCode()
        {
            int hash = 17;
            foreach (var item in Items)
            {
                hash = hash * 31 + item.GetHashCode();
            }
            return hash;
        }

        public BuiltinFunction GetMethod(string name)
        {
            return name switch
            {
                "count" => new BuiltinFunction("count", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "count() takes exactly one argument");
                    return new PythonInt(Items.Count(item => item.Equals(args[0])));
                }),
                "index" => new BuiltinFunction("index", args =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "index() takes exactly one argument");
                    for (int i = 0; i < Items.Count; i++)
                    {
                        if (Items[i].Equals(args[0]))
                            return new PythonInt(i);
                    }
                    throw new PythonException("ValueError", $"{args[0]} is not in tuple");
                }),
                _ => throw new PythonException("AttributeError", $"'tuple' object has no attribute '{name}'")
            };
        }

        private string FormatItem(PythonTypeObject item)
        {
            if (item is PythonString s) return $"'{s.Value}'";
            if (item is PythonNone) return "None";
            return item.ToPythonString();
        }
        
        public PythonTuple Repeat(int times)
        {
            if (times < 0) times = 0;
            var result = new PythonTuple();
            for (int i = 0; i < times; i++)
                result.Items.AddRange(Items);
            return result;
        }
        
        public PythonTypeObject GetItem(int index)
        {
            if (index < 0) index += Items.Count;
            if (index < 0 || index >= Items.Count)
                throw new PythonException("IndexError", "tuple index out of range");
            return Items[index];
        }
    }

    public class PythonDict : PythonTypeObject
    {
        public Dictionary<PythonTypeObject, PythonTypeObject> Items { get; } = new Dictionary<PythonTypeObject, PythonTypeObject>();
        
        public override PythonType Type => PythonType.Dict;
        public override bool IsTrue() => Items.Count > 0;
        public override object GetRawValue() => this;
        
        public override string ToPythonString()
        {
            if (Items.Count == 0) return "{}";
            var pairs = Items.Select(kvp => $"{FormatItem(kvp.Key)}: {FormatItem(kvp.Value)}");
            return "{" + string.Join(", ", pairs) + "}";
        }
        
        public override bool Equals(PythonTypeObject other)
        {
            if (other is PythonDict pd)
            {
                if (Items.Count != pd.Items.Count) return false;
                foreach (var kvp in Items)
                {
                    // if (!pd.Items.TryGetValue(kvp.Key, out var value) || !value.Equals(kvp.Value))
                    //     return false;
                    bool found = false;
                    foreach (var pdKvp in pd.Items)
                    {
                        if (pdKvp.Key.Equals(kvp.Key))
                        {
                            if (!pdKvp.Value.Equals(kvp.Value))
                                return false; // 값이 다르면 바로 false
                            found = true;
                            break;
                        }
                    }
                    if (!found) // 키를 못 찾았으면
                        return false;
                }
                return true;
            }
            return false;
        }
        
        public override int GetHashCode()
        {
            // Dicts are mutable and shouldn't be hashed
            throw new PythonException("TypeError", "unhashable type: 'dict'");
        }

        public BuiltinFunction GetMethod(string name)
        {
            return name switch
            {
                "get" => new BuiltinFunction("get", args =>
                {
                    if (args.Count < 1 || args.Count > 2) throw new PythonException("TypeError", "get() takes 1 or 2 arguments");
                    var key = args[0];
                    var defaultValue = args.Count == 2 ? args[1] : PythonNone.Instance;
                    // return Items.TryGetValue(key, out var value) ? value : defaultValue;
                    var res = defaultValue;
                    foreach (var kvp in Items)
                    {
                        if (kvp.Key.Equals(key))
                        {
                            res = kvp.Value;
                            break;
                        }
                    }
                    return res;
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
                    if (Items.TryGetValue(key, out var value))
                    {
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
                    return PythonNone.Instance;
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
                    return PythonNone.Instance;
                }),
                _ => throw new PythonException("AttributeError", $"'dict' object has no attribute '{name}'")
            };
        }

        private string FormatItem(PythonTypeObject item)
        {
            if (item is PythonString s) return $"'{s.Value}'";
            if (item is PythonNone) return "None";
            return item.ToPythonString();
        }
        
        public PythonTypeObject GetItem(PythonTypeObject key)
        {
            if (Items.TryGetValue(key, out var value))
                return value;
            throw new PythonException("KeyError", $"KeyError: {key}");
        }
        
        public void SetItem(PythonTypeObject key, PythonTypeObject value)
        {
            Items[key] = value;
        }
        
        public bool ContainsKey(PythonTypeObject key)
        {
            return Items.ContainsKey(key);
        }
    }

    // Function Classes (Enhanced with PythonTypeObject)
    public abstract class Function : PythonTypeObject
    {
        public string Name { get; }
        protected Function(string name) => Name = name;
        
        public abstract PythonTypeObject Call(List<PythonTypeObject> arguments);
        
        public override PythonType Type => PythonType.Function;
        public override bool IsTrue() => true;
        public override bool IsCallable() => true;
        public override string ToPythonString() => $"<function {Name}>";
        public override object GetRawValue() => this;
        
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
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

        public override PythonTypeObject Call(List<PythonTypeObject> arguments)
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
                PythonTypeObject result = PythonNone.Instance;
                foreach (var stmt in Body)
                    result = stmt.Evaluate(funcEnv);

                // Type checking for return value
                if (ReturnTypeHint != null && !ReturnTypeHint.IsCompatible(result))
                    throw new PythonException("TypeError", $"Return value expected {ReturnTypeHint}, got {GetValueType(result)}");

                return result;
            }
            catch (ReturnException ex)
            {
                // Type checking for return value
                if (ReturnTypeHint != null && !ReturnTypeHint.IsCompatible(ex.Value))
                    throw new PythonException("TypeError", $"Return value expected {ReturnTypeHint}, got {GetValueType(ex.Value)}");
                return ex.Value;
            }
        }

        private string GetValueType(PythonTypeObject value)
        {
            if (value == null || value is PythonNone) return "None";
            return value.Type.ToString().ToLower();
        }
    }

    public class LambdaFunction : Function
    {
        public List<Parameter> Parameters { get; }
        public ASTNode Body { get; }
        public Environment ClosureEnv { get; }

        public LambdaFunction(List<Parameter> parameters, ASTNode body, Environment closureEnv)
            : base("<lambda>")
        {
            Parameters = parameters;
            Body = body;
            ClosureEnv = closureEnv;
        }

        public override PythonTypeObject Call(List<PythonTypeObject> arguments)
        {
            if (arguments.Count != Parameters.Count)
                throw new PythonException("TypeError", $"Lambda function expects {Parameters.Count} arguments, got {arguments.Count}");

            var funcEnv = new Environment(ClosureEnv);

            // Bind parameters to arguments
            for (int i = 0; i < Parameters.Count; i++)
            {
                var param = Parameters[i];
                var arg = arguments[i];

                // Type checking for parameters (if type hints are provided)
                if (param.TypeHint != null && !param.TypeHint.IsCompatible(arg))
                    throw new PythonException("TypeError", $"Argument {i + 1} for parameter '{param.Name}' expected {param.TypeHint}, got {GetValueType(arg)}");

                funcEnv.SetVariable(param.Name, arg);
            }

            // Evaluate the lambda body (single expression)
            return Body.Evaluate(funcEnv);
        }

        private string GetValueType(PythonTypeObject value)
        {
            if (value == null || value is PythonNone) return "None";
            return value.Type.ToString().ToLower();
        }

        public override string ToPythonString() => "<lambda>";
    }
    
    public class BuiltinFunction : Function
    {
        private Func<List<PythonTypeObject>, PythonTypeObject> implementation;

        public BuiltinFunction(string name, Func<List<PythonTypeObject>, PythonTypeObject> impl) : base(name)
        {
            implementation = impl;
        }

        public override PythonTypeObject Call(List<PythonTypeObject> arguments) => implementation(arguments);
    }

    // Bound Method Class (for instance methods)
    public class BoundMethod : Function
    {
        private UserFunction method;
        private PythonTypeObject instance;

        public BoundMethod(string name, UserFunction method, PythonTypeObject instance) : base(name)
        {
            this.method = method;
            this.instance = instance;
        }

        public override PythonTypeObject Call(List<PythonTypeObject> arguments)
        {
            // Prepend 'self' to the arguments
            var newArgs = new List<PythonTypeObject> { instance };
            newArgs.AddRange(arguments);
            return method.Call(newArgs);
        }

        public override string ToPythonString() => $"<bound method {Name}>";
    }

    // Class System
    public class PythonClass : PythonTypeObject
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
        
        public override PythonType Type => PythonType.Class;
        public override bool IsTrue() => true;
        public override bool IsCallable() => true;
        public override string ToPythonString() => $"<class '{Name}'>";
        public override object GetRawValue() => this;
        
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();

        public PythonInstance CreateInstance(List<PythonTypeObject> args = null)
        {
            var instance = new PythonInstance(this);

            // __init__ 메서드가 있다면 호출
            if (ClassEnv.HasVariable("__init__"))
            {
                var initMethod = ClassEnv.GetVariable("__init__") as Function;
                if (initMethod != null)
                {
                    var initArgs = new List<PythonTypeObject> { instance };
                    if (args != null) initArgs.AddRange(args);
                    initMethod.Call(initArgs);
                }
            }

            return instance;
        }
    }

    public class PythonInstance : PythonTypeObject
    {
        public PythonClass Class { get; }
        public Environment InstanceEnv { get; }

        public PythonInstance(PythonClass pythonClass)
        {
            Class = pythonClass;
            InstanceEnv = new Environment(pythonClass.ClassEnv);
        }
        
        public override PythonType Type => PythonType.Instance;
        public override bool IsTrue() => true;
        public override string ToPythonString() => $"<{Class.Name} object>";
        public override object GetRawValue() => this;
        
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();

        public PythonTypeObject GetAttribute(string name)
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

        public void SetAttribute(string name, PythonTypeObject value) => InstanceEnv.SetVariable(name, value);
    }
    
    public class PythonModule : PythonTypeObject
    {
        public string Name { get; }
        public Environment ModuleEnv { get; }

        public PythonModule(string name)
        {
            Name = name;
            ModuleEnv = new Environment();
        }
        
        public override PythonType Type => PythonType.Module;
        public override bool IsTrue() => true;
        public override string ToPythonString() => $"<module '{Name}'>";
        public override object GetRawValue() => this;
        
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();

        public PythonTypeObject GetAttribute(string name)
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

        public void SetAttribute(string name, PythonTypeObject value) => ModuleEnv.SetVariable(name, value);
    }
}