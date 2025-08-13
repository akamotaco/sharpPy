using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace PurePythonInterpreter
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
        Number, String, Boolean, None, List, Dict, Tuple, Function, Class, Instance, Module
    }

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
                PythonType.Number => value is double,
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
                PythonType.Number => "int",
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

    // Exception Classes
    public class PythonException : Exception
    {
        public string Type { get; }
        public PythonException(string type, string message) : base(message) => Type = type;
    }

    public class ReturnException : Exception
    {
        public object Value { get; }
        public ReturnException(object value) => Value = value;
    }

    public class BreakException : Exception { }
    public class ContinueException : Exception { }

    // Token Class
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
}