using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    // Core Enums
    public enum TokenType : byte // Changed to byte for memory optimization
    {
        // Literals
        NUMBER, STRING, BOOLEAN, NONE, IDENTIFIER, FSTRING,
        // Keywords
        DEF, CLASS, IF, ELSE, ELIF, FOR, WHILE, IN, IS, BREAK, CONTINUE,
        TRY, EXCEPT, FINALLY, RAISE, IMPORT, FROM, AS, RETURN, AND, OR, NOT, LAMBDA,
        WITH, DEL,
        // Operators
        OPERATOR, ASSIGN, COMPOUND_ASSIGN,
        // Delimiters
        LPAREN, RPAREN, LBRACKET, RBRACKET, LBRACE, RBRACE, COLON, COMMA, DOT,
        // Special
        NEWLINE, EOF, INDENT, DEDENT
    }

    public enum PythonType : byte // Changed to byte for memory optimization
    {
        Int, Float, String, Boolean, None, List, Dict, Tuple, Function, Class, Instance, Module, Environment
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

        public PythonString(string value) => Value = value ?? "";
        
        public static PythonString Create(string value) => 
            string.IsNullOrEmpty(value) ? EmptyString : new PythonString(value);

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

        public BuiltinFunction GetMethod(string name) => name switch
        {
            "upper" => new BuiltinFunction("upper", args =>
            {
                if (args.Count != 0) throw new PythonException("TypeError", "upper() takes no arguments");
                return new PythonString(Value.ToUpper());
            }),
            "lower" => new BuiltinFunction("lower", args =>
            {
                if (args.Count != 0) throw new PythonException("TypeError", "lower() takes no arguments");
                return new PythonString(Value.ToLower());
            }),
            _ => throw new PythonException("AttributeError", $"'str' object has no attribute '{name}'")
        };
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

    // Enhanced Exception Classes
    public sealed class PythonException : Exception
    {
        public string Type { get; }
        public int Line { get; }
        public int Column { get; }
        public string FileName { get; }

        public PythonException(string type, string message, int line = 0, int column = 0, string fileName = "<string>") 
            : base(message) 
        { 
            Type = type;
            Line = line;
            Column = column;
            FileName = fileName;
        }

        public override string ToString() => Line > 0 
            ? $"  File \"{FileName}\", line {Line}, column {Column}\n{Type}: {Message}"
            : $"  File \"{FileName}\"\n{Type}: {Message}";
    }

    public sealed class ReturnException : Exception
    {
        public PythonTypeObject Value { get; }
        public ReturnException(PythonTypeObject value) => Value = value;
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
        
        internal SimpleTypeHint(PythonType type) => Type = type;
        
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

    public sealed class Parameter
    {
        public string Name { get; }
        public TypeHint TypeHint { get; }

        public Parameter(string name, TypeHint typeHint = null)
        {
            Name = name;
            TypeHint = typeHint;
        }
    }

    // Enhanced Token Class
    public sealed class Token
    {
        public TokenType Type { get; }
        public string Value { get; }
        public int Line { get; }
        public int Column { get; }

        public Token(TokenType type, string value, int line = 1, int column = 1)
        {
            Type = type;
            Value = value;
            Line = line;
            Column = column;
        }

        public override string ToString() => $"Token({Type}, {Value}) at {Line}:{Column}";
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
}

// enhanced_core_types.cs