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
        NUMBER, STRING, BOOLEAN, NONE, IDENTIFIER,
        // Keywords
        DEF, CLASS, IF, ELSE, ELIF, FOR, WHILE, IN, IS, BREAK, CONTINUE,
        TRY, EXCEPT, FINALLY, RAISE, IMPORT, FROM, AS, RETURN, AND, OR, NOT,
        // Operators
        OPERATOR, ASSIGN,
        // Delimiters
        LPAREN, RPAREN, LBRACKET, RBRACKET, LBRACE, RBRACE, COLON, COMMA, DOT,
        // Special
        NEWLINE, EOF, INDENT, DEDENT
    }

    public enum PythonType
    {
        Int, Float, String, Boolean, None, List, Dict, Tuple, Function, Class, Instance, Module
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
        public object Value { get; }
        public ReturnException(object value) => Value = value;
    }

    public class BreakException : Exception { }
    public class ContinueException : Exception { }

    // Type Hint System
    public abstract class TypeHint
    {
        public abstract bool IsCompatible(object value);
        public abstract override string ToString();
    }

    public class SimpleTypeHint : TypeHint
    {
        public PythonType Type { get; }
        
        public SimpleTypeHint(PythonType type) => Type = type;

        public override bool IsCompatible(object value)
        {
            return Type switch
            {
                PythonType.Int => value is int,
                PythonType.Float => value is double,
                PythonType.String => value is string,
                PythonType.Boolean => value is bool,
                PythonType.None => value == null,
                PythonType.List => value is PythonList,
                PythonType.Dict => value is PythonDict,
                PythonType.Tuple => value is PythonTuple,
                PythonType.Function => value is Function,
                PythonType.Class => value is PythonClass,
                PythonType.Instance => value is PythonInstance,
                _ => true
            };
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

        public override bool IsCompatible(object value)
        {
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

    // Helper class for number operations
    public static class NumberHelper
    {
        public static bool IsNumber(object obj) => obj is int || obj is double;
        
        public static bool IsInteger(object obj) => obj is int;
        
        public static bool IsFloat(object obj) => obj is double;

        public static double ToDouble(object obj)
        {
            return obj switch
            {
                int i => (double)i,
                double d => d,
                _ => throw new ArgumentException("Not a number")
            };
        }

        public static int ToInt(object obj)
        {
            return obj switch
            {
                int i => i,
                double d => (int)Math.Truncate(d),
                _ => throw new ArgumentException("Not a number")
            };
        }

        // Python-like division: returns float for true division
        public static object Divide(object left, object right)
        {
            var leftD = ToDouble(left);
            var rightD = ToDouble(right);
            
            if (rightD == 0) throw new PythonException("ZeroDivisionError", "Division by zero");
            return leftD / rightD;
        }

        // Python-like arithmetic: preserves int when possible
        public static object Add(object left, object right)
        {
            if (left is int li && right is int ri)
                return li + ri;
            
            return ToDouble(left) + ToDouble(right);
        }

        public static object Subtract(object left, object right)
        {
            if (left is int li && right is int ri)
                return li - ri;
            
            return ToDouble(left) - ToDouble(right);
        }

        public static object Multiply(object left, object right)
        {
            if (left is int li && right is int ri)
                return li * ri;
            
            return ToDouble(left) * ToDouble(right);
        }

        public static object Modulo(object left, object right)
        {
            if (left is int li && right is int ri)
            {
                if (ri == 0) throw new PythonException("ZeroDivisionError", "Modulo by zero");
                return li % ri;
            }
            
            var leftD = ToDouble(left);
            var rightD = ToDouble(right);
            if (rightD == 0) throw new PythonException("ZeroDivisionError", "Modulo by zero");
            return leftD % rightD;
        }

        public static object Power(object left, object right)
        {
            var leftD = ToDouble(left);
            var rightD = ToDouble(right);
            var result = Math.Pow(leftD, rightD);
            
            // If both operands are int and result is a whole number, return int
            if (left is int && right is int && result == Math.Truncate(result) && result >= int.MinValue && result <= int.MaxValue)
                return (int)result;
            
            return result;
        }

        public static object Negate(object operand)
        {
            return operand switch
            {
                int i => -i,
                double d => -d,
                _ => throw new ArgumentException("Cannot negate non-number")
            };
        }
    }
}