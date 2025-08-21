// enhanced_core_types.cs
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SharpPy
{

    public enum PythonType : byte // Changed to byte for memory optimization
    {
        Int, Float, String, Boolean, None, List, Dict, Tuple, Set, Function, Class, Instance, Module, Environment, Super
    }

    // Base class for all Python objects
    public abstract class PythonTypeObject
    {
        public abstract PythonType Type { get; }
        public abstract bool IsTrue();
        public abstract string ToPythonString();
        public abstract bool Equals(PythonTypeObject other);
        public abstract object GetRawValue();

        public override string ToString() => ToPythonString();

        // Virtual methods with default implementations
        public virtual bool IsNumber() => false;
        public virtual bool IsSequence() => false;
        public virtual bool IsCallable() => false;

        public virtual PythonInt ToInt() =>
            throw new PythonException("TypeError", $"Cannot convert {Type} to int");

        public virtual PythonFloat ToFloat() =>
            throw new PythonException("TypeError", $"Cannot convert {Type} to float");

        public virtual PythonString ToStr() => new PythonString(ToPythonString());
        public virtual PythonBool ToBool() => new PythonBool(IsTrue());

        // Virtual method to get all available method names
        public virtual List<string> GetMethodNames()
        {
            return new List<string>();
        }

        // Virtual method to get a method by name
        public virtual BuiltinFunction GetMethod(string name)
        {
            return null;
        }

        public virtual PythonTypeObject GetAttribute(string name)
        {
            return null;
        }
    }

    // Optimized Python Integer type with value caching
    public sealed class PythonInt : PythonTypeObject
    {
        // Cache small integers for better performance
        private static readonly Dictionary<int, PythonInt> SmallIntCache = new Dictionary<int, PythonInt>();
        private const int CacheMin = -128;
        private const int CacheMax = 256;

        static PythonInt()
        {
            for (int i = CacheMin; i <= CacheMax; i++)
                SmallIntCache[i] = new PythonInt(i, false);
        }

        public int Value { get; }

        private PythonInt(int value, bool bypassCache) => Value = value;

        public PythonInt(int value)
        {
            if (value >= CacheMin && value <= CacheMax)
            {
                var cached = SmallIntCache[value];
                Value = cached.Value;
            }
            else
            {
                Value = value;
            }
        }

        public static PythonInt Create(int value)
        {
            if (value >= CacheMin && value <= CacheMax)
                return SmallIntCache[value];
            return new PythonInt(value, false);
        }

        public override PythonType Type => PythonType.Int;
        public override bool IsTrue() => Value != 0;
        public override string ToPythonString() => Value.ToString();
        public override object GetRawValue() => Value;
        public override bool IsNumber() => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(PythonTypeObject other) => other switch
        {
            PythonInt pi => Value == pi.Value,
            PythonFloat pf => Value == pf.Value,
            PythonBool pb => Value == (pb.Value ? 1 : 0),
            _ => false
        };

        public override PythonInt ToInt() => this;
        public override PythonFloat ToFloat() => new PythonFloat(Value);
        public override int GetHashCode() => Value.GetHashCode();

        // Optimized arithmetic operations
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PythonTypeObject Add(PythonTypeObject other) => other switch
        {
            PythonInt pi => Create(Value + pi.Value),
            PythonFloat pf => new PythonFloat(Value + pf.Value),
            PythonBool pb => Create(Value + (pb.Value ? 1 : 0)),
            _ => throw new PythonException("TypeError", $"unsupported operand type(s) for +: 'int' and '{other.Type}'")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PythonTypeObject Subtract(PythonTypeObject other) => other switch
        {
            PythonInt pi => Create(Value - pi.Value),
            PythonFloat pf => new PythonFloat(Value - pf.Value),
            PythonBool pb => Create(Value - (pb.Value ? 1 : 0)),
            _ => throw new PythonException("TypeError", $"unsupported operand type(s) for -: 'int' and '{other.Type}'")
        };

        public PythonTypeObject Multiply(PythonTypeObject other) => other switch
        {
            PythonInt pi => Create(Value * pi.Value),
            PythonFloat pf => new PythonFloat(Value * pf.Value),
            PythonBool pb => Create(Value * (pb.Value ? 1 : 0)),
            PythonString ps => ps.Repeat(Value),
            PythonList pl => pl.Repeat(Value),
            PythonTuple pt => pt.Repeat(Value),
            _ => throw new PythonException("TypeError", $"unsupported operand type(s) for *: 'int' and '{other.Type}'")
        };

        public PythonTypeObject Divide(PythonTypeObject other)
        {
            var divisor = other switch
            {
                PythonInt pi => pi.Value,
                PythonFloat pf => pf.Value,
                PythonBool pb => pb.Value ? 1.0 : 0.0,
                _ => throw new PythonException("TypeError", $"unsupported operand type(s) for /: 'int' and '{other.Type}'")
            };

            if (divisor == 0) throw new PythonException("ZeroDivisionError", "division by zero");
            return new PythonFloat(Value / divisor);
        }

        public PythonTypeObject Modulo(PythonTypeObject other)
        {
            if (other is PythonInt pi)
            {
                if (pi.Value == 0) throw new PythonException("ZeroDivisionError", "integer modulo by zero");
                return Create(Value % pi.Value);
            }
            if (other is PythonFloat pf)
            {
                if (pf.Value == 0) throw new PythonException("ZeroDivisionError", "float modulo");
                return new PythonFloat(Value % pf.Value);
            }
            throw new PythonException("TypeError", $"unsupported operand type(s) for %: 'int' and '{other.Type}'");
        }

        public PythonTypeObject Power(PythonTypeObject other)
        {
            if (other is PythonInt pi)
            {
                var result = Math.Pow(Value, pi.Value);
                if (result == Math.Truncate(result) && result >= int.MinValue && result <= int.MaxValue)
                    return Create((int)result);
                return new PythonFloat(result);
            }
            if (other is PythonFloat pf) return new PythonFloat(Math.Pow(Value, pf.Value));
            throw new PythonException("TypeError", $"unsupported operand type(s) for **: 'int' and '{other.Type}'");
        }

        public PythonInt Negate() => Create(-Value);
    }

    // Python Float type
    public sealed class PythonFloat : PythonTypeObject
    {
        public double Value { get; }

        public PythonFloat(double value) => Value = value;

        public override PythonType Type => PythonType.Float;
        public override bool IsTrue() => Value != 0;
        public override string ToPythonString() => Value.ToString();
        public override object GetRawValue() => Value;
        public override bool IsNumber() => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(PythonTypeObject other) => other switch
        {
            PythonFloat pf => Value == pf.Value,
            PythonInt pi => Value == pi.Value,
            PythonBool pb => Value == (pb.Value ? 1 : 0),
            _ => false
        };

        public override PythonInt ToInt() => PythonInt.Create((int)Math.Truncate(Value));
        public override PythonFloat ToFloat() => this;
        public override int GetHashCode() => Value.GetHashCode();

        // Arithmetic operations
        public PythonTypeObject Add(PythonTypeObject other) => other switch
        {
            PythonFloat pf => new PythonFloat(Value + pf.Value),
            PythonInt pi => new PythonFloat(Value + pi.Value),
            PythonBool pb => new PythonFloat(Value + (pb.Value ? 1 : 0)),
            _ => throw new PythonException("TypeError", $"unsupported operand type(s) for +: 'float' and '{other.Type}'")
        };

        public PythonTypeObject Subtract(PythonTypeObject other) => other switch
        {
            PythonFloat pf => new PythonFloat(Value - pf.Value),
            PythonInt pi => new PythonFloat(Value - pi.Value),
            PythonBool pb => new PythonFloat(Value - (pb.Value ? 1 : 0)),
            _ => throw new PythonException("TypeError", $"unsupported operand type(s) for -: 'float' and '{other.Type}'")
        };

        public PythonTypeObject Multiply(PythonTypeObject other) => other switch
        {
            PythonFloat pf => new PythonFloat(Value * pf.Value),
            PythonInt pi => new PythonFloat(Value * pi.Value),
            PythonBool pb => new PythonFloat(Value * (pb.Value ? 1 : 0)),
            _ => throw new PythonException("TypeError", $"unsupported operand type(s) for *: 'float' and '{other.Type}'")
        };

        public PythonTypeObject Divide(PythonTypeObject other)
        {
            var divisor = other switch
            {
                PythonFloat pf => pf.Value,
                PythonInt pi => (double)pi.Value,
                PythonBool pb => pb.Value ? 1.0 : 0.0,
                _ => throw new PythonException("TypeError", $"unsupported operand type(s) for /: 'float' and '{other.Type}'")
            };

            if (divisor == 0) throw new PythonException("ZeroDivisionError", "float division by zero");
            return new PythonFloat(Value / divisor);
        }

        public PythonTypeObject Modulo(PythonTypeObject other)
        {
            var divisor = other switch
            {
                PythonFloat pf => pf.Value,
                PythonInt pi => (double)pi.Value,
                _ => throw new PythonException("TypeError", $"unsupported operand type(s) for %: 'float' and '{other.Type}'")
            };

            if (divisor == 0) throw new PythonException("ZeroDivisionError", "float modulo");
            return new PythonFloat(Value % divisor);
        }

        public PythonTypeObject Power(PythonTypeObject other) => other switch
        {
            PythonFloat pf => new PythonFloat(Math.Pow(Value, pf.Value)),
            PythonInt pi => new PythonFloat(Math.Pow(Value, pi.Value)),
            _ => throw new PythonException("TypeError", $"unsupported operand type(s) for **: 'float' and '{other.Type}'")
        };

        public PythonFloat Negate() => new PythonFloat(-Value);
    }

    // Optimized Boolean with singleton pattern
    public sealed class PythonBool : PythonTypeObject
    {
        public static readonly PythonBool True = new PythonBool(true);
        public static readonly PythonBool False = new PythonBool(false);

        public bool Value { get; }

        public PythonBool(bool value) => Value = value;

        public static PythonBool Create(bool value) => value ? True : False;

        public override PythonType Type => PythonType.Boolean;
        public override bool IsTrue() => Value;
        public override string ToPythonString() => Value ? "True" : "False";
        public override object GetRawValue() => Value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(PythonTypeObject other) => other switch
        {
            PythonBool pb => Value == pb.Value,
            PythonInt pi => (Value ? 1 : 0) == pi.Value,
            PythonFloat pf => (Value ? 1 : 0) == pf.Value,
            _ => false
        };

        public override PythonInt ToInt() => PythonInt.Create(Value ? 1 : 0);
        public override PythonFloat ToFloat() => new PythonFloat(Value ? 1.0 : 0.0);
        public override PythonBool ToBool() => this;
        public override int GetHashCode() => Value.GetHashCode();
    }

    // Optimized String type
    public sealed class PythonString : PythonTypeObject
    {
        private static readonly PythonString EmptyString = new PythonString("");

        public string Value { get; }
        public int Length => Value.Length;
        private static Dictionary<string, Func<PythonString, List<PythonTypeObject>, PythonTypeObject>> methodRegistry;

        static PythonString()
        {
            InitializeMethodRegistry();
        }

        public PythonString(string value) => Value = value ?? "";

        public static PythonString Create(string value) =>
            string.IsNullOrEmpty(value) ? EmptyString : new PythonString(value);

        private static void InitializeMethodRegistry()
        {
            methodRegistry = new Dictionary<string, Func<PythonString, List<PythonTypeObject>, PythonTypeObject>>
            {
                ["upper"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "upper() takes no arguments");
                    return new PythonString(self.Value.ToUpper());
                },
                ["lower"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "lower() takes no arguments");
                    return new PythonString(self.Value.ToLower());
                },
                ["strip"] = (self, args) =>
                {
                    if (args.Count > 1) throw new PythonException("TypeError", "strip() takes at most 1 argument");
                    if (args.Count == 0) return new PythonString(self.Value.Trim());
                    if (args[0] is PythonString chars)
                        return new PythonString(self.Value.Trim(chars.Value.ToCharArray()));
                    throw new PythonException("TypeError", "strip() argument must be a string");
                },
                ["split"] = (self, args) =>
                {
                    if (args.Count > 2) throw new PythonException("TypeError", "split() takes at most 2 arguments");
                    string separator = args.Count > 0 && args[0] is PythonString sep ? sep.Value : " ";
                    int maxSplit = args.Count > 1 && NumberHelper.IsNumber(args[1]) ? NumberHelper.ToInt(args[1]) : -1;

                    var parts = maxSplit < 0
                        ? self.Value.Split(new[] { separator }, StringSplitOptions.None)
                        : self.Value.Split(new[] { separator }, maxSplit + 1, StringSplitOptions.None);

                    var result = new PythonList(parts.Length);
                    foreach (var part in parts)
                        result.Items.Add(new PythonString(part));
                    return result;
                },
                ["join"] = (self, args) =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "join() takes exactly one argument");
                    if (!(args[0] is PythonList list || args[0] is PythonTuple tuple))
                        throw new PythonException("TypeError", "join() argument must be iterable");

                    var items = args[0] is PythonList l ? l.Items : ((PythonTuple)args[0]).Items;
                    var strings = new List<string>();
                    foreach (var item in items)
                    {
                        if (!(item is PythonString str))
                            throw new PythonException("TypeError", "join() requires string items");
                        strings.Add(str.Value);
                    }
                    return new PythonString(string.Join(self.Value, strings));
                },
                ["replace"] = (self, args) =>
                {
                    if (args.Count < 2 || args.Count > 3)
                        throw new PythonException("TypeError", "replace() takes 2 or 3 arguments");
                    if (!(args[0] is PythonString oldStr) || !(args[1] is PythonString newStr))
                        throw new PythonException("TypeError", "replace() requires string arguments");

                    if (args.Count == 3)
                    {
                        if (!NumberHelper.IsNumber(args[2]))
                            throw new PythonException("TypeError", "replace() count must be a number");
                        int count = NumberHelper.ToInt(args[2]);
                        var result = self.Value;
                        for (int i = 0; i < count && result.Contains(oldStr.Value); i++)
                        {
                            int index = result.IndexOf(oldStr.Value);
                            result = result.Remove(index, oldStr.Value.Length).Insert(index, newStr.Value);
                        }
                        return new PythonString(result);
                    }
                    return new PythonString(self.Value.Replace(oldStr.Value, newStr.Value));
                },
                ["startswith"] = (self, args) =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "startswith() takes exactly one argument");
                    if (!(args[0] is PythonString prefix))
                        throw new PythonException("TypeError", "startswith() requires a string argument");
                    return new PythonBool(self.Value.StartsWith(prefix.Value));
                },
                ["endswith"] = (self, args) =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "endswith() takes exactly one argument");
                    if (!(args[0] is PythonString suffix))
                        throw new PythonException("TypeError", "endswith() requires a string argument");
                    return new PythonBool(self.Value.EndsWith(suffix.Value));
                },
                ["find"] = (self, args) =>
                {
                    if (args.Count < 1 || args.Count > 3)
                        throw new PythonException("TypeError", "find() takes 1 to 3 arguments");
                    if (!(args[0] is PythonString substr))
                        throw new PythonException("TypeError", "find() requires a string as first argument");

                    int start = args.Count > 1 && NumberHelper.IsNumber(args[1]) ? NumberHelper.ToInt(args[1]) : 0;
                    int end = args.Count > 2 && NumberHelper.IsNumber(args[2]) ? NumberHelper.ToInt(args[2]) : self.Value.Length;

                    if (start < 0) start = Math.Max(0, self.Value.Length + start);
                    if (end < 0) end = Math.Max(0, self.Value.Length + end);
                    end = Math.Min(end, self.Value.Length);

                    if (start >= end) return PythonInt.Create(-1);

                    int index = self.Value.IndexOf(substr.Value, start, end - start);
                    return PythonInt.Create(index);
                },
                ["count"] = (self, args) =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "count() takes exactly one argument");
                    if (!(args[0] is PythonString substr))
                        throw new PythonException("TypeError", "count() requires a string argument");

                    int count = 0;
                    int index = 0;
                    while ((index = self.Value.IndexOf(substr.Value, index)) != -1)
                    {
                        count++;
                        index += substr.Value.Length;
                    }
                    return PythonInt.Create(count);
                },
                ["isdigit"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "isdigit() takes no arguments");
                    return new PythonBool(self.Value.Length > 0 && self.Value.All(char.IsDigit));
                },
                ["isalpha"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "isalpha() takes no arguments");
                    return new PythonBool(self.Value.Length > 0 && self.Value.All(char.IsLetter));
                },
                ["isspace"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "isspace() takes no arguments");
                    return new PythonBool(self.Value.Length > 0 && self.Value.All(char.IsWhiteSpace));
                }
            };
        }

        public override PythonType Type => PythonType.String;
        public override bool IsTrue() => !string.IsNullOrEmpty(Value);
        public override string ToPythonString() => Value;
        public override object GetRawValue() => Value;
        public override bool IsSequence() => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(PythonTypeObject other) =>
            other is PythonString ps && Value == ps.Value;

        public override PythonInt ToInt()
        {
            if (int.TryParse(Value, out var result))
                return PythonInt.Create(result);
            throw new PythonException("ValueError", $"invalid literal for int() with base 10: '{Value}'");
        }

        public override PythonFloat ToFloat()
        {
            if (double.TryParse(Value, out var result))
                return new PythonFloat(result);
            throw new PythonException("ValueError", $"could not convert string to float: '{Value}'");
        }

        public override PythonString ToStr() => this;
        public override int GetHashCode() => Value.GetHashCode();

        public PythonString Add(PythonTypeObject other)
        {
            if (other is PythonString ps) return new PythonString(Value + ps.Value);
            return new PythonString(Value + other.ToPythonString());
        }

        public PythonString Repeat(int times)
        {
            if (times <= 0) return EmptyString;
            if (times == 1) return this;

            var sb = new StringBuilder(Value.Length * times);
            for (int i = 0; i < times; i++)
                sb.Append(Value);
            return new PythonString(sb.ToString());
        }

        public PythonString GetItem(int index)
        {
            if (index < 0) index += Value.Length;
            if (index < 0 || index >= Value.Length)
                throw new PythonException("IndexError", "string index out of range");
            return new PythonString(Value[index].ToString());
        }

        public PythonString Slice(int? start, int? stop, int? step)
        {
            int actualStep = step ?? 1;
            if (actualStep == 0)
                throw new PythonException("ValueError", "slice step cannot be zero");

            int length = Value.Length;
            int actualStart = start ?? (actualStep > 0 ? 0 : length - 1);
            int actualStop = stop ?? (actualStep > 0 ? length : -1);

            if (actualStart < 0) actualStart += length;
            if (actualStop < 0) actualStop += length;

            actualStart = Math.Max(0, Math.Min(actualStart, length));
            actualStop = Math.Max(-1, Math.Min(actualStop, length));

            var result = new StringBuilder();

            if (actualStep > 0)
            {
                for (int i = actualStart; i < actualStop; i += actualStep)
                {
                    if (i >= 0 && i < length)
                        result.Append(Value[i]);
                }
            }
            else
            {
                for (int i = actualStart; i > actualStop; i += actualStep)
                {
                    if (i >= 0 && i < length)
                        result.Append(Value[i]);
                }
            }

            return new PythonString(result.ToString());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(PythonTypeObject other) =>
            other is PythonString ps && Value.Contains(ps.Value);


        public override BuiltinFunction GetMethod(string name)
        {
            if (methodRegistry.TryGetValue(name, out var method))
            {
                return new BuiltinFunction(name, args => method(this, args));
            }
            throw new PythonException("AttributeError", $"'str' object has no attribute '{name}'");
        }

        public override List<string> GetMethodNames()
        {
            return methodRegistry.Keys.OrderBy(k => k).ToList();
        }
    }

    // Optimized None singleton
    public sealed class PythonNone : PythonTypeObject
    {
        public static readonly PythonNone Instance = new PythonNone();

        private PythonNone() { }

        public override PythonType Type => PythonType.None;
        public override bool IsTrue() => false;
        public override string ToPythonString() => "None";
        public override object GetRawValue() => null;
        public override bool Equals(PythonTypeObject other) => other is PythonNone;
        public override int GetHashCode() => 0;
    }

    public class StackFrame
    {
        public string FileName { get; set; }
        public string FunctionName { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string SourceLine { get; set; }  // 실제 소스 코드 라인

        public StackFrame(string fileName, string functionName, int line, int column)
        {
            FileName = fileName;
            FunctionName = functionName;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return $"  File \"{FileName}\", line {Line}, in {FunctionName}";
        }
    }

    // Enhanced Exception Classes
    public class PythonException : Exception
    {
        public string Type { get; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string FileName { get; set; }
        public List<StackFrame> CallStack { get; }

        public PythonException(string type, string message, int line = 0, int column = 0, string fileName = null)
            : base(message)
        {
            Type = type;
            Line = line;
            Column = column;
            FileName = fileName ?? "<string>";
            CallStack = new List<StackFrame>();
        }

        public void AddStackFrame(StackFrame frame)
        {
            CallStack.Add(frame);
        }

        public string GetTraceback()
        {
            if (CallStack.Count == 0)
            {
                return $"  File \"{FileName}\", line {Line}, column {Column}\n{Type}: {Message}";
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Traceback (most recent call last):");

            // 스택을 역순으로 출력 (호출 순서대로)
            for (int i = CallStack.Count - 1; i >= 0; i--)
            {
                sb.AppendLine(CallStack[i].ToString());
                if (!string.IsNullOrEmpty(CallStack[i].SourceLine))
                {
                    sb.AppendLine($"    {CallStack[i].SourceLine}");
                }
            }

            sb.Append($"{Type}: {Message}");
            return sb.ToString();
        }
    }

    public static class ExecutionContext
    {
        private static Stack<StackFrame> callStack = new Stack<StackFrame>();
        
        public static void PushFrame(StackFrame frame)
        {
            callStack.Push(frame);
        }
        
        public static void PopFrame()
        {
            if (callStack.Count > 0)
                callStack.Pop();
        }
        
        public static StackFrame CurrentFrame => callStack.Count > 0 ? callStack.Peek() : null;
        
        public static List<StackFrame> GetCallStack()
        {
            return callStack.ToList();
        }
        
        public static void Clear()
        {
            callStack.Clear();
        }
    }

    public class ReturnException : Exception
    {
        public PythonTypeObject Value { get; }
        public int Line { get; }
        public int Column { get; }
        public string FileName { get; }

        public ReturnException(PythonTypeObject value, int line = 0, int column = 0, string fileName = null)
        {
            Value = value;
            Line = line;
            Column = column;
            FileName = fileName;
        }
    }

    public sealed class BreakException : Exception { }
    public sealed class ContinueException : Exception { }

    // Type Hint System
    public abstract class TypeHint
    {
        public abstract bool IsCompatible(PythonTypeObject value);
    }

    public sealed class SimpleTypeHint : TypeHint
    {
        private static readonly Dictionary<PythonType, SimpleTypeHint> Cache = new Dictionary<PythonType, SimpleTypeHint>();

        public PythonType Type { get; }

        private SimpleTypeHint(PythonType type) => Type = type;

        public static SimpleTypeHint Create(PythonType type)
        {
            if (!Cache.TryGetValue(type, out var hint))
            {
                hint = new SimpleTypeHint(type);
                Cache[type] = hint;
            }
            return hint;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool IsCompatible(PythonTypeObject value) =>
            value == null ? Type == PythonType.None : value.Type == Type;

        public override string ToString() => Type switch
        {
            PythonType.Int => "int",
            PythonType.Float => "float",
            PythonType.String => "str",
            PythonType.Boolean => "bool",
            PythonType.None => "None",
            PythonType.List => "list",
            PythonType.Dict => "dict",
            PythonType.Tuple => "tuple",
            _ => "Any"
        };
    }

    public sealed class AnyTypeHint : TypeHint
    {
        public override bool IsCompatible(PythonTypeObject value) => true;
        public override string ToString() => "Any";
    }

    public sealed class GenericTypeHint : TypeHint
    {
        public PythonType BaseType { get; }
        public List<TypeHint> GenericArgs { get; }

        public GenericTypeHint(PythonType baseType, List<TypeHint> genericArgs)
        {
            BaseType = baseType;
            GenericArgs = genericArgs ?? new List<TypeHint>();
        }

        public override bool IsCompatible(PythonTypeObject value)
        {
            if (value == null) return false;

            switch (BaseType)
            {
                case PythonType.List when value is PythonList list:
                    return GenericArgs.Count == 0 || list.Items.All(item => GenericArgs[0].IsCompatible(item));

                case PythonType.Dict when value is PythonDict dict:
                    if (GenericArgs.Count < 2) return true;
                    var keyType = GenericArgs[0];
                    var valueType = GenericArgs[1];
                    return dict.Items.All(kvp => keyType.IsCompatible(kvp.Key) && valueType.IsCompatible(kvp.Value));

                case PythonType.Tuple when value is PythonTuple tuple:
                    if (GenericArgs.Count == 0) return true;
                    if (GenericArgs.Count != tuple.Items.Count) return false;
                    for (int i = 0; i < GenericArgs.Count; i++)
                    {
                        if (!GenericArgs[i].IsCompatible(tuple.Items[i]))
                            return false;
                    }
                    return true;

                default:
                    return SimpleTypeHint.Create(BaseType).IsCompatible(value);
            }
        }

        public override string ToString()
        {
            var baseStr = SimpleTypeHint.Create(BaseType).ToString();
            return GenericArgs.Count == 0 ? baseStr : $"{baseStr}[{string.Join(", ", GenericArgs)}]";
        }
    }

    // 문자열로 된 클래스 타입 힌트를 위한 클래스 추가
    public sealed class ClassTypeHint : TypeHint
    {
        public string ClassName { get; }

        public ClassTypeHint(string className)
        {
            ClassName = className;
        }

        public override bool IsCompatible(PythonTypeObject value)
        {
            switch (value)
            {
                case PythonInstance instance:
                    // 인스턴스의 MRO에서 ClassName 찾기
                    return IsInMRO(instance.Class, ClassName);

                case PythonClass cls:
                    // 클래스 자체의 MRO에서 ClassName 찾기
                    return IsInMRO(cls, ClassName);

                default:
                    return false;
            }
        }

        private bool IsInMRO(PythonClass cls, string targetClassName)
        {
            // GetMRO()는 이미 전체 상속 체인을 C3 알고리즘으로 계산함
            var mro = cls.GetMRO();
            return mro.Any(c => c.Name == targetClassName);
        }

        public override string ToString() => ClassName;
    }

    // Union 타입을 위한 클래스도 추가 (Optional 등을 위해)
    public sealed class UnionTypeHint : TypeHint
    {
        public List<TypeHint> Types { get; }

        public UnionTypeHint(List<TypeHint> types)
        {
            Types = types ?? new List<TypeHint>();
        }

        public override bool IsCompatible(PythonTypeObject value)
        {
        // 디버깅 코드 추가
            Console.WriteLine($"[DEBUG] UnionTypeHint checking value type: {value?.Type}");
            foreach (var t in Types)
            {
                Console.WriteLine($"[DEBUG] Checking against: {t}");
                bool compatible = t.IsCompatible(value);
                Console.WriteLine($"[DEBUG] Compatible: {compatible}");
                if (compatible) return true;
            }
            return false;
        }

        public override string ToString()
        {
            // Python 3.10+ 스타일: int | None
            // 이전 스타일: Union[int, None]
            return string.Join(" | ", Types.Select(t => t.ToString()));
        }
    }

    public enum ParameterKind
    {
        Normal,      // 일반 매개변수
        VarArgs,     // *args
        KwArgs       // **kwargs
    }

    public sealed class Parameter
    {
        public string Name { get; }
        public TypeHint TypeHint { get; }
        public ASTNode DefaultValue { get; }
        public ParameterKind Kind { get; }  // 추가

        public Parameter(string name, TypeHint typeHint = null, ASTNode defaultValue = null, ParameterKind kind = ParameterKind.Normal)
        {
            Name = name;
            TypeHint = typeHint;
            DefaultValue = defaultValue;
            Kind = kind;
        }

        public bool HasDefault => DefaultValue != null;
    }

    // Optimized Helper class for number operations
    public static class NumberHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNumber(PythonTypeObject obj) =>
            obj is PythonInt || obj is PythonFloat || obj is PythonBool;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInteger(PythonTypeObject obj) => obj is PythonInt;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFloat(PythonTypeObject obj) => obj is PythonFloat;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ToDouble(PythonTypeObject obj) => obj switch
        {
            PythonInt pi => pi.Value,
            PythonFloat pf => pf.Value,
            PythonBool pb => pb.Value ? 1.0 : 0.0,
            _ => throw new ArgumentException("Not a number")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToInt(PythonTypeObject obj) => obj switch
        {
            PythonInt pi => pi.Value,
            PythonFloat pf => (int)Math.Truncate(pf.Value),
            PythonBool pb => pb.Value ? 1 : 0,
            _ => throw new ArgumentException("Not a number")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PythonTypeObject Divide(PythonTypeObject left, PythonTypeObject right) => left switch
        {
            PythonInt li => li.Divide(right),
            PythonFloat lf => lf.Divide(right),
            _ => throw new PythonException("TypeError", "unsupported operand type(s) for /")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PythonTypeObject Add(PythonTypeObject left, PythonTypeObject right) => left switch
        {
            PythonInt li => li.Add(right),
            PythonFloat lf => lf.Add(right),
            _ => throw new PythonException("TypeError", "unsupported operand type(s) for +")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PythonTypeObject Subtract(PythonTypeObject left, PythonTypeObject right) => left switch
        {
            PythonInt li => li.Subtract(right),
            PythonFloat lf => lf.Subtract(right),
            _ => throw new PythonException("TypeError", "unsupported operand type(s) for -")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PythonTypeObject Multiply(PythonTypeObject left, PythonTypeObject right) => left switch
        {
            PythonInt li => li.Multiply(right),
            PythonFloat lf => lf.Multiply(right),
            _ => throw new PythonException("TypeError", "unsupported operand type(s) for *")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PythonTypeObject Modulo(PythonTypeObject left, PythonTypeObject right) => left switch
        {
            PythonInt li => li.Modulo(right),
            PythonFloat lf => lf.Modulo(right),
            _ => throw new PythonException("TypeError", "unsupported operand type(s) for %")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PythonTypeObject Power(PythonTypeObject left, PythonTypeObject right) => left switch
        {
            PythonInt li => li.Power(right),
            PythonFloat lf => lf.Power(right),
            _ => throw new PythonException("TypeError", "unsupported operand type(s) for **")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PythonTypeObject Negate(PythonTypeObject operand) => operand switch
        {
            PythonInt pi => pi.Negate(),
            PythonFloat pf => pf.Negate(),
            _ => throw new ArgumentException("Cannot negate non-number")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PythonTypeObject ToPythonObject(object obj) => obj switch
        {
            null => PythonNone.Instance,
            PythonTypeObject pto => pto,
            int i => PythonInt.Create(i),
            double d => new PythonFloat(d),
            bool b => PythonBool.Create(b),
            string s => new PythonString(s),
            _ => throw new ArgumentException($"Cannot convert {obj.GetType()} to Python type")
        };
    }

    // Function 타입 추가
    public enum FunctionType
    {
        Normal,
        Static,
        Class
    }
public sealed class StaticMethod : PythonTypeObject
{
    public Function Method { get; }
    
    public StaticMethod(Function method)
    {
        Method = method;
    }
    
    public override PythonType Type => PythonType.Function;
    public override bool IsTrue() => true;
    public override string ToPythonString() => $"<staticmethod object>";
    public override object GetRawValue() => this;
    public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
}

    public sealed class ClassMethod : PythonTypeObject
    {
        public Function Method { get; }

        public ClassMethod(Function method)
        {
            Method = method;
        }

        public override PythonType Type => PythonType.Function;
        public override bool IsTrue() => true;
        public override string ToPythonString() => $"<classmethod object>";
        public override object GetRawValue() => this;
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
    }
}

// enhanced_core_types.cs