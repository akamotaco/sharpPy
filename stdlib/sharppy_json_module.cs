// sharppy_json_module.cs
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Globalization;
using System.IO;

namespace SharpPy
{
    public static class JsonModule
    {
        public static PythonModule CreateJsonModule(List<string> searchPaths = null)
        {
            var module = new PythonModule("json", searchPaths);

            // json.loads(string) - Parse JSON string to Python object
            module.SetAttribute("loads", new BuiltinFunction("loads", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "loads() takes exactly 1 argument");
                
                if (!(args[0] is PythonString jsonStr))
                    throw new PythonException("TypeError", "loads() argument must be a string");

                try
                {
                    var parser = new JsonParser(jsonStr.Value);
                    return parser.Parse();
                }
                catch (Exception ex)
                {
                    throw new PythonException("JSONDecodeError", $"Invalid JSON: {ex.Message}");
                }
            }));

            // json.dumps(obj, indent=None, sort_keys=False) - Convert Python object to JSON string
            module.SetAttribute("dumps", new BuiltinFunction("dumps", args =>
            {
                if (args.Count < 1 || args.Count > 3)
                    throw new PythonException("TypeError", "dumps() takes 1 to 3 arguments");

                var obj = args[0];
                
                // Handle optional indent parameter
                int? indent = null;
                if (args.Count > 1 && !(args[1] is PythonNone))
                {
                    if (NumberHelper.IsNumber(args[1]))
                        indent = NumberHelper.ToInt(args[1]);
                    else
                        throw new PythonException("TypeError", "indent must be an integer or None");
                }

                // Handle optional sort_keys parameter
                bool sortKeys = false;
                if (args.Count > 2)
                {
                    if (args[2] is PythonBool sb)
                        sortKeys = sb.Value;
                    else
                        throw new PythonException("TypeError", "sort_keys must be a boolean");
                }

                try
                {
                    var serializer = new JsonSerializer(indent, sortKeys);
                    return new PythonString(serializer.Serialize(obj));
                }
                catch (Exception ex)
                {
                    throw new PythonException("TypeError", $"Object is not JSON serializable: {ex.Message}");
                }
            }));

            // json.load(file) - Load JSON from file object
            module.SetAttribute("load", new BuiltinFunction("load", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "load() takes exactly 1 argument");

                // Check if it's a file object
                if (args[0] is FileObject fileObj)
                {
                    try
                    {
                        var content = fileObj.Read();
                        var parser = new JsonParser(content.Value);
                        return parser.Parse();
                    }
                    catch (Exception ex)
                    {
                        throw new PythonException("JSONDecodeError", $"Invalid JSON in file: {ex.Message}");
                    }
                }
                else
                {
                    throw new PythonException("TypeError", "load() argument must be a file object");
                }
            }));

            // json.dump(obj, file, indent=None, sort_keys=False) - Write JSON to file
            module.SetAttribute("dump", new BuiltinFunction("dump", args =>
            {
                if (args.Count < 2 || args.Count > 4)
                    throw new PythonException("TypeError", "dump() takes 2 to 4 arguments");

                var obj = args[0];
                
                if (!(args[1] is FileObject fileObj))
                    throw new PythonException("TypeError", "dump() second argument must be a file object");

                // Handle optional indent parameter
                int? indent = null;
                if (args.Count > 2 && !(args[2] is PythonNone))
                {
                    if (NumberHelper.IsNumber(args[2]))
                        indent = NumberHelper.ToInt(args[2]);
                }

                // Handle optional sort_keys parameter
                bool sortKeys = false;
                if (args.Count > 3)
                {
                    if (args[3] is PythonBool sb)
                        sortKeys = sb.Value;
                }

                try
                {
                    var serializer = new JsonSerializer(indent, sortKeys);
                    string jsonStr = serializer.Serialize(obj);
                    fileObj.Write(jsonStr);
                    return PythonNone.Instance;
                }
                catch (Exception ex)
                {
                    throw new PythonException("TypeError", $"Object is not JSON serializable: {ex.Message}");
                }
            }));

            // json.JSONDecodeError exception class
            module.SetAttribute("JSONDecodeError", new PythonString("JSONDecodeError"));

            return module;
        }
    }

    // JSON Parser - converts JSON string to Python objects
    internal class JsonParser
    {
        private readonly string json;
        private int position;
        private readonly int length;

        public JsonParser(string json)
        {
            this.json = json ?? "";
            this.length = this.json.Length;
            this.position = 0;
        }

        public PythonTypeObject Parse()
        {
            SkipWhitespace();
            var result = ParseValue();
            SkipWhitespace();
            
            if (position < length)
                throw new Exception($"Unexpected character at position {position}");
            
            return result;
        }

        private PythonTypeObject ParseValue()
        {
            SkipWhitespace();
            
            if (position >= length)
                throw new Exception("Unexpected end of JSON");

            char current = json[position];
            
            switch (current)
            {
                case '{':
                    return ParseObject();
                case '[':
                    return ParseArray();
                case '"':
                    return ParseString();
                case 't':
                case 'f':
                    return ParseBoolean();
                case 'n':
                    return ParseNull();
                case '-':
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    return ParseNumber();
                default:
                    throw new Exception($"Unexpected character '{current}' at position {position}");
            }
        }

        private PythonDict ParseObject()
        {
            var dict = new PythonDict();
            position++; // Skip '{'
            
            SkipWhitespace();
            
            // Empty object
            if (position < length && json[position] == '}')
            {
                position++;
                return dict;
            }

            while (position < length)
            {
                SkipWhitespace();
                
                // Parse key (must be string)
                if (json[position] != '"')
                    throw new Exception($"Expected string key at position {position}");
                
                var key = ParseString();
                
                SkipWhitespace();
                
                // Expect colon
                if (position >= length || json[position] != ':')
                    throw new Exception($"Expected ':' at position {position}");
                position++;
                
                SkipWhitespace();
                
                // Parse value
                var value = ParseValue();
                
                dict.SetItem(key, value);
                
                SkipWhitespace();
                
                if (position >= length)
                    throw new Exception("Unexpected end of JSON in object");
                
                if (json[position] == '}')
                {
                    position++;
                    return dict;
                }
                
                if (json[position] != ',')
                    throw new Exception($"Expected ',' or '}}' at position {position}");
                position++;
            }
            
            throw new Exception("Unexpected end of JSON object");
        }

        private PythonList ParseArray()
        {
            var list = new PythonList();
            position++; // Skip '['
            
            SkipWhitespace();
            
            // Empty array
            if (position < length && json[position] == ']')
            {
                position++;
                return list;
            }

            while (position < length)
            {
                SkipWhitespace();
                
                // Parse value
                var value = ParseValue();
                list.Items.Add(value);
                
                SkipWhitespace();
                
                if (position >= length)
                    throw new Exception("Unexpected end of JSON in array");
                
                if (json[position] == ']')
                {
                    position++;
                    return list;
                }
                
                if (json[position] != ',')
                    throw new Exception($"Expected ',' or ']' at position {position}");
                position++;
            }
            
            throw new Exception("Unexpected end of JSON array");
        }

        private PythonString ParseString()
        {
            position++; // Skip opening quote
            var sb = new StringBuilder();
            
            while (position < length)
            {
                char c = json[position];
                
                if (c == '"')
                {
                    position++;
                    return new PythonString(sb.ToString());
                }
                
                if (c == '\\')
                {
                    position++;
                    if (position >= length)
                        throw new Exception("Unexpected end of string");
                    
                    char escaped = json[position];
                    switch (escaped)
                    {
                        case '"':
                            sb.Append('"');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case '/':
                            sb.Append('/');
                            break;
                        case 'b':
                            sb.Append('\b');
                            break;
                        case 'f':
                            sb.Append('\f');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'u':
                            // Unicode escape
                            position++;
                            if (position + 3 >= length)
                                throw new Exception("Invalid unicode escape");
                            string hex = json.Substring(position, 4);
                            if (int.TryParse(hex, NumberStyles.HexNumber, null, out int codePoint))
                            {
                                sb.Append((char)codePoint);
                                position += 3;
                            }
                            else
                            {
                                throw new Exception("Invalid unicode escape sequence");
                            }
                            break;
                        default:
                            throw new Exception($"Invalid escape sequence '\\{escaped}'");
                    }
                }
                else
                {
                    sb.Append(c);
                }
                
                position++;
            }
            
            throw new Exception("Unterminated string");
        }

        private PythonTypeObject ParseNumber()
        {
            int start = position;
            bool hasDecimal = false;
            bool hasExponent = false;
            
            // Handle negative sign
            if (json[position] == '-')
                position++;
            
            // Parse integer part
            if (position >= length || !char.IsDigit(json[position]))
                throw new Exception("Invalid number");
            
            if (json[position] == '0')
            {
                position++;
                // Leading zero must be followed by decimal point or end
                if (position < length && char.IsDigit(json[position]))
                    throw new Exception("Invalid number format");
            }
            else
            {
                while (position < length && char.IsDigit(json[position]))
                    position++;
            }
            
            // Parse decimal part
            if (position < length && json[position] == '.')
            {
                hasDecimal = true;
                position++;
                
                if (position >= length || !char.IsDigit(json[position]))
                    throw new Exception("Invalid number format");
                
                while (position < length && char.IsDigit(json[position]))
                    position++;
            }
            
            // Parse exponent
            if (position < length && (json[position] == 'e' || json[position] == 'E'))
            {
                hasExponent = true;
                position++;
                
                if (position < length && (json[position] == '+' || json[position] == '-'))
                    position++;
                
                if (position >= length || !char.IsDigit(json[position]))
                    throw new Exception("Invalid number format");
                
                while (position < length && char.IsDigit(json[position]))
                    position++;
            }
            
            string numberStr = json.Substring(start, position - start);
            
            if (hasDecimal || hasExponent)
            {
                if (double.TryParse(numberStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return new PythonFloat(d);
            }
            else
            {
                if (int.TryParse(numberStr, out int i))
                    return PythonInt.Create(i);
                if (double.TryParse(numberStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return new PythonFloat(d);
            }
            
            throw new Exception($"Invalid number: {numberStr}");
        }

        private PythonBool ParseBoolean()
        {
            if (position + 4 <= length && json.Substring(position, 4) == "true")
            {
                position += 4;
                return PythonBool.True;
            }
            
            if (position + 5 <= length && json.Substring(position, 5) == "false")
            {
                position += 5;
                return PythonBool.False;
            }
            
            throw new Exception($"Invalid boolean at position {position}");
        }

        private PythonNone ParseNull()
        {
            if (position + 4 <= length && json.Substring(position, 4) == "null")
            {
                position += 4;
                return PythonNone.Instance;
            }
            
            throw new Exception($"Invalid null at position {position}");
        }

        private void SkipWhitespace()
        {
            while (position < length && char.IsWhiteSpace(json[position]))
                position++;
        }
    }

    // JSON Serializer - converts Python objects to JSON string
    internal class JsonSerializer
    {
        private readonly int? indent;
        private readonly bool sortKeys;
        private readonly StringBuilder sb;
        private int currentIndentLevel;

        public JsonSerializer(int? indent = null, bool sortKeys = false)
        {
            this.indent = indent;
            this.sortKeys = sortKeys;
            this.sb = new StringBuilder();
            this.currentIndentLevel = 0;
        }

        public string Serialize(PythonTypeObject obj)
        {
            sb.Clear();
            currentIndentLevel = 0;
            SerializeValue(obj);
            return sb.ToString();
        }

        private void SerializeValue(PythonTypeObject obj)
        {
            switch (obj)
            {
                case PythonNone _:
                    sb.Append("null");
                    break;
                    
                case PythonBool b:
                    sb.Append(b.Value ? "true" : "false");
                    break;
                    
                case PythonInt i:
                    sb.Append(i.Value.ToString(CultureInfo.InvariantCulture));
                    break;
                    
                case PythonFloat f:
                    // Handle special float values
                    if (double.IsNaN(f.Value))
                        throw new Exception("Out of range float values are not JSON compliant");
                    if (double.IsInfinity(f.Value))
                        throw new Exception("Out of range float values are not JSON compliant");
                    sb.Append(f.Value.ToString(CultureInfo.InvariantCulture));
                    break;
                    
                case PythonString s:
                    SerializeString(s.Value);
                    break;
                    
                case PythonList list:
                    SerializeArray(list);
                    break;
                    
                case PythonTuple tuple:
                    // Tuples are serialized as arrays in JSON
                    SerializeTuple(tuple);
                    break;
                    
                case PythonDict dict:
                    SerializeObject(dict);
                    break;
                    
                default:
                    throw new Exception($"Object of type '{obj.Type}' is not JSON serializable");
            }
        }

        private void SerializeString(string str)
        {
            sb.Append('"');
            
            foreach (char c in str)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20 || c > 0x7F)
                        {
                            // Unicode escape
                            sb.Append($"\\u{(int)c:x4}");
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            
            sb.Append('"');
        }

        private void SerializeArray(PythonList list)
        {
            sb.Append('[');
            
            if (list.Items.Count > 0)
            {
                if (indent.HasValue)
                {
                    currentIndentLevel++;
                    sb.AppendLine();
                    AppendIndent();
                }
                
                for (int i = 0; i < list.Items.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                        if (indent.HasValue)
                        {
                            sb.AppendLine();
                            AppendIndent();
                        }
                        else
                        {
                            sb.Append(' ');
                        }
                    }
                    
                    SerializeValue(list.Items[i]);
                }
                
                if (indent.HasValue)
                {
                    currentIndentLevel--;
                    sb.AppendLine();
                    AppendIndent();
                }
            }
            
            sb.Append(']');
        }

        private void SerializeTuple(PythonTuple tuple)
        {
            sb.Append('[');
            
            if (tuple.Items.Count > 0)
            {
                if (indent.HasValue)
                {
                    currentIndentLevel++;
                    sb.AppendLine();
                    AppendIndent();
                }
                
                for (int i = 0; i < tuple.Items.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                        if (indent.HasValue)
                        {
                            sb.AppendLine();
                            AppendIndent();
                        }
                        else
                        {
                            sb.Append(' ');
                        }
                    }
                    
                    SerializeValue(tuple.Items[i]);
                }
                
                if (indent.HasValue)
                {
                    currentIndentLevel--;
                    sb.AppendLine();
                    AppendIndent();
                }
            }
            
            sb.Append(']');
        }

        private void SerializeObject(PythonDict dict)
        {
            sb.Append('{');
            
            if (dict.Items.Count > 0)
            {
                if (indent.HasValue)
                {
                    currentIndentLevel++;
                    sb.AppendLine();
                    AppendIndent();
                }
                
                // Get keys and optionally sort them
                var keys = dict.Items.Keys.ToList();
                if (sortKeys)
                {
                    // Sort keys if they are all strings
                    if (keys.All(k => k is PythonString))
                    {
                        keys = keys.OrderBy(k => ((PythonString)k).Value).ToList();
                    }
                }
                
                bool first = true;
                foreach (var key in keys)
                {
                    if (!first)
                    {
                        sb.Append(',');
                        if (indent.HasValue)
                        {
                            sb.AppendLine();
                            AppendIndent();
                        }
                        else
                        {
                            sb.Append(' ');
                        }
                    }
                    first = false;
                    
                    // JSON requires string keys
                    if (!(key is PythonString))
                    {
                        throw new Exception($"Keys must be strings, not {key.Type}");
                    }
                    
                    SerializeString(((PythonString)key).Value);
                    sb.Append(':');
                    if (indent.HasValue)
                        sb.Append(' ');
                    
                    SerializeValue(dict.Items[key]);
                }
                
                if (indent.HasValue)
                {
                    currentIndentLevel--;
                    sb.AppendLine();
                    AppendIndent();
                }
            }
            
            sb.Append('}');
        }

        private void AppendIndent()
        {
            if (indent.HasValue)
            {
                int spaces = indent.Value * currentIndentLevel;
                sb.Append(new string(' ', spaces));
            }
        }
    }
}