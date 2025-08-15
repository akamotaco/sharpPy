// enhanced_core_types.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace SharpPy
{
    // Core Enums
    public enum TokenType
    {
        // Literals
        NUMBER, STRING, BOOLEAN, NONE, IDENTIFIER, FSTRING, // Added FSTRING
        // Keywords
        DEF, CLASS, IF, ELSE, ELIF, FOR, WHILE, IN, IS, BREAK, CONTINUE,
        TRY, EXCEPT, FINALLY, RAISE, IMPORT, FROM, AS, RETURN, AND, OR, NOT, LAMBDA,
        WITH, DEL, // Added WITH and DEL for with statement and del keyword
        // Operators
        OPERATOR, ASSIGN,
        COMPOUND_ASSIGN, // Added for +=, -=, *=, /=, etc.
        // Delimiters
        LPAREN, RPAREN, LBRACKET, RBRACKET, LBRACE, RBRACE, COLON, COMMA, DOT,
        // Special
        NEWLINE, EOF, INDENT, DEDENT
    }

    public enum PythonType
    {
        Int, Float, String, Boolean, None, List, Dict, Tuple, Function, Class, Instance, Module
    }

    // Base class for all Python objects
    public abstract class PythonTypeObject
    {
        public abstract PythonType Type { get; }
        public abstract bool IsTrue();
        public abstract string ToPythonString();
        public abstract bool Equals(PythonTypeObject other);
        
        // Helper method to get C# value for interop
        public abstract object GetRawValue();
        
        public override string ToString() => ToPythonString();
        
        // Type checking helpers
        public virtual bool IsNumber() => false;
        public virtual bool IsSequence() => false;
        public virtual bool IsCallable() => false;
        
        // Conversion methods
        public virtual PythonInt ToInt()
        {
            throw new PythonException("TypeError", $"Cannot convert {Type} to int");
        }
        
        public virtual PythonFloat ToFloat()
        {
            throw new PythonException("TypeError", $"Cannot convert {Type} to float");
        }
        
        public virtual PythonString ToStr()
        {
            return new PythonString(ToPythonString());
        }
        
        public virtual PythonBool ToBool()
        {
            return new PythonBool(IsTrue());
        }
    }

    // Python Integer type
    public class PythonInt : PythonTypeObject
    {
        public int Value { get; }
        
        public PythonInt(int value) => Value = value;
        
        public override PythonType Type => PythonType.Int;
        public override bool IsTrue() => Value != 0;
        public override string ToPythonString() => Value.ToString();
        public override object GetRawValue() => Value;
        public override bool IsNumber() => true;
        
        public override bool Equals(PythonTypeObject other)
        {
            if (other is PythonInt pi) return Value == pi.Value;
            if (other is PythonFloat pf) return Value == pf.Value;
            if (other is PythonBool pb) return Value == (pb.Value ? 1 : 0);
            return false;
        }
        
        public override PythonInt ToInt() => this;
        
        public override PythonFloat ToFloat() => new PythonFloat(Value);
        
        public override int GetHashCode() => Value.GetHashCode();
        
        // Arithmetic operations
        public PythonTypeObject Add(PythonTypeObject other)
        {
            if (other is PythonInt pi) return new PythonInt(Value + pi.Value);
            if (other is PythonFloat pf) return new PythonFloat(Value + pf.Value);
            if (other is PythonBool pb) return new PythonInt(Value + (pb.Value ? 1 : 0));
            throw new PythonException("TypeError", $"unsupported operand type(s) for +: 'int' and '{other.Type}'");
        }
        
        public PythonTypeObject Subtract(PythonTypeObject other)
        {
            if (other is PythonInt pi) return new PythonInt(Value - pi.Value);
            if (other is PythonFloat pf) return new PythonFloat(Value - pf.Value);
            if (other is PythonBool pb) return new PythonInt(Value - (pb.Value ? 1 : 0));
            throw new PythonException("TypeError", $"unsupported operand type(s) for -: 'int' and '{other.Type}'");
        }
        
        public PythonTypeObject Multiply(PythonTypeObject other)
        {
            if (other is PythonInt pi) return new PythonInt(Value * pi.Value);
            if (other is PythonFloat pf) return new PythonFloat(Value * pf.Value);
            if (other is PythonBool pb) return new PythonInt(Value * (pb.Value ? 1 : 0));
            if (other is PythonString ps) return ps.Repeat(Value);
            if (other is PythonList pl) return pl.Repeat(Value);
            if (other is PythonTuple pt) return pt.Repeat(Value);
            throw new PythonException("TypeError", $"unsupported operand type(s) for *: 'int' and '{other.Type}'");
        }
        
        public PythonTypeObject Divide(PythonTypeObject other)
        {
            if (other is PythonInt pi)
            {
                if (pi.Value == 0) throw new PythonException("ZeroDivisionError", "division by zero");
                return new PythonFloat((double)Value / pi.Value);
            }
            if (other is PythonFloat pf)
            {
                if (pf.Value == 0) throw new PythonException("ZeroDivisionError", "division by zero");
                return new PythonFloat(Value / pf.Value);
            }
            if (other is PythonBool pb)
            {
                if (!pb.Value) throw new PythonException("ZeroDivisionError", "division by zero");
                return new PythonFloat(Value);
            }
            throw new PythonException("TypeError", $"unsupported operand type(s) for /: 'int' and '{other.Type}'");
        }
        
        public PythonTypeObject Modulo(PythonTypeObject other)
        {
            if (other is PythonInt pi)
            {
                if (pi.Value == 0) throw new PythonException("ZeroDivisionError", "integer modulo by zero");
                return new PythonInt(Value % pi.Value);
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
                    return new PythonInt((int)result);
                return new PythonFloat(result);
            }
            if (other is PythonFloat pf) return new PythonFloat(Math.Pow(Value, pf.Value));
            throw new PythonException("TypeError", $"unsupported operand type(s) for **: 'int' and '{other.Type}'");
        }
        
        public PythonInt Negate() => new PythonInt(-Value);
    }

    // Python Float type
    public class PythonFloat : PythonTypeObject
    {
        public double Value { get; }
        
        public PythonFloat(double value) => Value = value;
        
        public override PythonType Type => PythonType.Float;
        public override bool IsTrue() => Value != 0;
        public override string ToPythonString() => Value.ToString();
        public override object GetRawValue() => Value;
        public override bool IsNumber() => true;
        
        public override bool Equals(PythonTypeObject other)
        {
            if (other is PythonFloat pf) return Value == pf.Value;
            if (other is PythonInt pi) return Value == pi.Value;
            if (other is PythonBool pb) return Value == (pb.Value ? 1 : 0);
            return false;
        }
        
        public override PythonInt ToInt() => new PythonInt((int)Math.Truncate(Value));
        
        public override PythonFloat ToFloat() => this;
        
        public override int GetHashCode() => Value.GetHashCode();
        
        // Arithmetic operations
        public PythonTypeObject Add(PythonTypeObject other)
        {
            if (other is PythonFloat pf) return new PythonFloat(Value + pf.Value);
            if (other is PythonInt pi) return new PythonFloat(Value + pi.Value);
            if (other is PythonBool pb) return new PythonFloat(Value + (pb.Value ? 1 : 0));
            throw new PythonException("TypeError", $"unsupported operand type(s) for +: 'float' and '{other.Type}'");
        }
        
        public PythonTypeObject Subtract(PythonTypeObject other)
        {
            if (other is PythonFloat pf) return new PythonFloat(Value - pf.Value);
            if (other is PythonInt pi) return new PythonFloat(Value - pi.Value);
            if (other is PythonBool pb) return new PythonFloat(Value - (pb.Value ? 1 : 0));
            throw new PythonException("TypeError", $"unsupported operand type(s) for -: 'float' and '{other.Type}'");
        }
        
        public PythonTypeObject Multiply(PythonTypeObject other)
        {
            if (other is PythonFloat pf) return new PythonFloat(Value * pf.Value);
            if (other is PythonInt pi) return new PythonFloat(Value * pi.Value);
            if (other is PythonBool pb) return new PythonFloat(Value * (pb.Value ? 1 : 0));
            throw new PythonException("TypeError", $"unsupported operand type(s) for *: 'float' and '{other.Type}'");
        }
        
        public PythonTypeObject Divide(PythonTypeObject other)
        {
            if (other is PythonFloat pf)
            {
                if (pf.Value == 0) throw new PythonException("ZeroDivisionError", "float division by zero");
                return new PythonFloat(Value / pf.Value);
            }
            if (other is PythonInt pi)
            {
                if (pi.Value == 0) throw new PythonException("ZeroDivisionError", "float division by zero");
                return new PythonFloat(Value / pi.Value);
            }
            if (other is PythonBool pb)
            {
                if (!pb.Value) throw new PythonException("ZeroDivisionError", "float division by zero");
                return new PythonFloat(Value);
            }
            throw new PythonException("TypeError", $"unsupported operand type(s) for /: 'float' and '{other.Type}'");
        }
        
        public PythonTypeObject Modulo(PythonTypeObject other)
        {
            if (other is PythonFloat pf)
            {
                if (pf.Value == 0) throw new PythonException("ZeroDivisionError", "float modulo");
                return new PythonFloat(Value % pf.Value);
            }
            if (other is PythonInt pi)
            {
                if (pi.Value == 0) throw new PythonException("ZeroDivisionError", "float modulo");
                return new PythonFloat(Value % pi.Value);
            }
            throw new PythonException("TypeError", $"unsupported operand type(s) for %: 'float' and '{other.Type}'");
        }
        
        public PythonTypeObject Power(PythonTypeObject other)
        {
            if (other is PythonFloat pf) return new PythonFloat(Math.Pow(Value, pf.Value));
            if (other is PythonInt pi) return new PythonFloat(Math.Pow(Value, pi.Value));
            throw new PythonException("TypeError", $"unsupported operand type(s) for **: 'float' and '{other.Type}'");
        }
        
        public PythonFloat Negate() => new PythonFloat(-Value);
    }

    // Python Boolean type
    public class PythonBool : PythonTypeObject
    {
        public bool Value { get; }
        
        public PythonBool(bool value) => Value = value;
        
        public override PythonType Type => PythonType.Boolean;
        public override bool IsTrue() => Value;
        public override string ToPythonString() => Value ? "True" : "False";
        public override object GetRawValue() => Value;
        
        public override bool Equals(PythonTypeObject other)
        {
            if (other is PythonBool pb) return Value == pb.Value;
            if (other is PythonInt pi) return (Value ? 1 : 0) == pi.Value;
            if (other is PythonFloat pf) return (Value ? 1 : 0) == pf.Value;
            return false;
        }
        
        public override PythonInt ToInt() => new PythonInt(Value ? 1 : 0);
        
        public override PythonFloat ToFloat() => new PythonFloat(Value ? 1.0 : 0.0);
        
        public override PythonBool ToBool() => this;
        
        public override int GetHashCode() => Value.GetHashCode();
    }

    // Python String type
    public class PythonString : PythonTypeObject
    {
        public string Value { get; }
        
        public PythonString(string value) => Value = value ?? "";
        
        public override PythonType Type => PythonType.String;
        public override bool IsTrue() => !string.IsNullOrEmpty(Value);
        public override string ToPythonString() => Value;
        public override object GetRawValue() => Value;
        public override bool IsSequence() => true;
        
        public override bool Equals(PythonTypeObject other)
        {
            if (other is PythonString ps) return Value == ps.Value;
            return false;
        }
        
        public override PythonInt ToInt()
        {
            if (int.TryParse(Value, out var result))
                return new PythonInt(result);
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
        
        // String operations
        public PythonString Add(PythonTypeObject other)
        {
            if (other is PythonString ps) return new PythonString(Value + ps.Value);
            return new PythonString(Value + other.ToPythonString());
        }
        
        public PythonString Repeat(int times)
        {
            if (times < 0) times = 0;
            return new PythonString(string.Concat(Enumerable.Repeat(Value, times)));
        }
        
        public int Length => Value.Length;
        
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
        
        public bool Contains(PythonTypeObject other)
        {
            if (other is PythonString ps)
                return Value.Contains(ps.Value);
            return false;
        }
    }

    // Python None type
    public class PythonNone : PythonTypeObject
    {
        private static PythonNone _instance;
        public static PythonNone Instance => _instance ??= new PythonNone();
        
        private PythonNone() { }
        
        public override PythonType Type => PythonType.None;
        public override bool IsTrue() => false;
        public override string ToPythonString() => "None";
        public override object GetRawValue() => null;
        
        public override bool Equals(PythonTypeObject other) => other is PythonNone;
        
        public override int GetHashCode() => 0;
    }

    // Enhanced Exception Classes with Line/Column Information
    public class PythonException : Exception
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

        public override string ToString()
        {
            if (Line > 0)
                return $"  File \"{FileName}\", line {Line}, column {Column}\n{Type}: {Message}";
            else
                return $"  File \"{FileName}\"\n{Type}: {Message}";
        }
    }

    public class ReturnException : Exception
    {
        public PythonTypeObject Value { get; }
        public ReturnException(PythonTypeObject value) => Value = value;
    }

    public class BreakException : Exception { }
    public class ContinueException : Exception { }

    // Type Hint System
    public abstract class TypeHint
    {
        public abstract bool IsCompatible(PythonTypeObject value);
        public abstract override string ToString();
    }

    public class SimpleTypeHint : TypeHint
    {
        public PythonType Type { get; }
        
        public SimpleTypeHint(PythonType type) => Type = type;

        public override bool IsCompatible(PythonTypeObject value)
        {
            if (value == null) return Type == PythonType.None;
            return value.Type == Type;
        }

        public override string ToString()
        {
            return Type switch
            {
                PythonType.Int => "int",
                PythonType.Float => "float",
                PythonType.String => "str",
                PythonType.Boolean => "bool",
                PythonType.None => "None",
                PythonType.List => "list",
                PythonType.Dict => "dict",
                PythonType.Tuple => "tuple",
                PythonType.Function => "function",
                PythonType.Class => "class",
                PythonType.Instance => "object",
                _ => "Any"
            };
        }
    }

    public class GenericTypeHint : TypeHint
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
            
            if (BaseType == PythonType.List && value is PythonList list)
            {
                if (GenericArgs.Count == 0) return true;
                var elementType = GenericArgs[0];
                return list.Items.All(item => elementType.IsCompatible(item));
            }
            if (BaseType == PythonType.Dict && value is PythonDict dict)
            {
                if (GenericArgs.Count < 2) return true;
                var keyType = GenericArgs[0];
                var valueType = GenericArgs[1];
                return dict.Items.All(kvp => keyType.IsCompatible(kvp.Key) && valueType.IsCompatible(kvp.Value));
            }
            if (BaseType == PythonType.Tuple && value is PythonTuple tuple)
            {
                if (GenericArgs.Count == 0) return true;
                if (GenericArgs.Count != tuple.Items.Count) return false;
                for (int i = 0; i < GenericArgs.Count; i++)
                {
                    if (!GenericArgs[i].IsCompatible(tuple.Items[i]))
                        return false;
                }
                return true;
            }
            return new SimpleTypeHint(BaseType).IsCompatible(value);
        }

        public override string ToString()
        {
            var baseStr = new SimpleTypeHint(BaseType).ToString();
            if (GenericArgs.Count == 0) return baseStr;
            return $"{baseStr}[{string.Join(", ", GenericArgs.Select(g => g.ToString()))}]";
        }
    }

    public class Parameter
    {
        public string Name { get; }
        public TypeHint TypeHint { get; }

        public Parameter(string name, TypeHint typeHint = null)
        {
            Name = name;
            TypeHint = typeHint;
        }
    }

    // Enhanced Token Class with Line/Column Information
    public class Token
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

    // Helper class for number operations - Updated to work with PythonTypeObject
    public static class NumberHelper
    {
        public static bool IsNumber(PythonTypeObject obj) 
            => obj is PythonInt || obj is PythonFloat || obj is PythonBool;
        
        public static bool IsInteger(PythonTypeObject obj) => obj is PythonInt;
        
        public static bool IsFloat(PythonTypeObject obj) => obj is PythonFloat;

        public static double ToDouble(PythonTypeObject obj)
        {
            return obj switch
            {
                PythonInt pi => pi.Value,
                PythonFloat pf => pf.Value,
                PythonBool pb => pb.Value ? 1.0 : 0.0,
                _ => throw new ArgumentException("Not a number")
            };
        }

        public static int ToInt(PythonTypeObject obj)
        {
            return obj switch
            {
                PythonInt pi => pi.Value,
                PythonFloat pf => (int)Math.Truncate(pf.Value),
                PythonBool pb => pb.Value ? 1 : 0,
                _ => throw new ArgumentException("Not a number")
            };
        }

        // Python-like division: returns float for true division
        public static PythonTypeObject Divide(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li)
                return li.Divide(right);
            if (left is PythonFloat lf)
                return lf.Divide(right);
            throw new PythonException("TypeError", "unsupported operand type(s) for /");
        }

        // Python-like arithmetic: preserves int when possible
        public static PythonTypeObject Add(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li)
                return li.Add(right);
            if (left is PythonFloat lf)
                return lf.Add(right);
            throw new PythonException("TypeError", "unsupported operand type(s) for +");
        }

        public static PythonTypeObject Subtract(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li)
                return li.Subtract(right);
            if (left is PythonFloat lf)
                return lf.Subtract(right);
            throw new PythonException("TypeError", "unsupported operand type(s) for -");
        }

        public static PythonTypeObject Multiply(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li)
                return li.Multiply(right);
            if (left is PythonFloat lf)
                return lf.Multiply(right);
            throw new PythonException("TypeError", "unsupported operand type(s) for *");
        }

        public static PythonTypeObject Modulo(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li)
                return li.Modulo(right);
            if (left is PythonFloat lf)
                return lf.Modulo(right);
            throw new PythonException("TypeError", "unsupported operand type(s) for %");
        }

        public static PythonTypeObject Power(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li)
                return li.Power(right);
            if (left is PythonFloat lf)
                return lf.Power(right);
            throw new PythonException("TypeError", "unsupported operand type(s) for **");
        }

        public static PythonTypeObject Negate(PythonTypeObject operand)
        {
            return operand switch
            {
                PythonInt pi => pi.Negate(),
                PythonFloat pf => pf.Negate(),
                _ => throw new ArgumentException("Cannot negate non-number")
            };
        }
        
        // Convert C# native types to Python types
        public static PythonTypeObject ToPythonObject(object obj)
        {
            return obj switch
            {
                null => PythonNone.Instance,
                PythonTypeObject pto => pto,
                int i => new PythonInt(i),
                double d => new PythonFloat(d),
                bool b => new PythonBool(b),
                string s => new PythonString(s),
                _ => throw new ArgumentException($"Cannot convert {obj.GetType()} to Python type")
            };
        }
    }
}