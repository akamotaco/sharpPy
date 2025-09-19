using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SharpPy.Modules.Stdlib
{
    /// <summary>
    /// Python json 모듈 구현 - JSON 직렬화/역직렬화
    /// System.Text.Json을 활용한 고성능 구현
    /// </summary>
    public static class JsonModule
    {
        public static PyModule CreateJsonModule()
        {
            var module = new PyModule("json", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\json.py");

            // 핵심 직렬화/역직렬화 함수들
            module.ModuleDict["dumps"] = new PyBuiltinFunction("dumps", JsonDumps);
            module.ModuleDict["dump"] = new PyBuiltinFunction("dump", JsonDump);
            module.ModuleDict["loads"] = new PyBuiltinFunction("loads", JsonLoads);
            module.ModuleDict["load"] = new PyBuiltinFunction("load", JsonLoad);

            // 예외 클래스들
            module.ModuleDict["JSONDecodeError"] = new PyJsonExceptionType("JSONDecodeError");

            // 엔코더/디코더 클래스들 (간단한 구현)
            module.ModuleDict["JSONEncoder"] = new PyJsonEncoderType("JSONEncoder");
            module.ModuleDict["JSONDecoder"] = new PyJsonDecoderType("JSONDecoder");

            return module;
        }

        #region JSON 직렬화 (Python → JSON)

        public static PyObject JsonDumps(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("dumps() missing 1 required positional argument: 'obj'");

            var obj = args[0];
            
            // 선택적 매개변수들 (키워드 인자는 간단화)
            bool ensureAscii = true;
            int? indent = null;
            bool sortKeys = false;

            // 간단한 키워드 인자 처리
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] is PyBool boolArg)
                    sortKeys = boolArg.Value; // 마지막 bool을 sort_keys로 처리
                else if (args[i] is PyInt intArg)
                    indent = intArg.Value; // 정수는 indent로 처리
            }

            try
            {
                var jsonString = SerializePyObjectToJson(obj, indent, sortKeys, ensureAscii);
                return new PyString(jsonString);
            }
            catch (Exception ex)
            {
                throw PyTypeError.Create($"Object of type '{obj.GetTypeName()}' is not JSON serializable: {ex.Message}");
            }
        }

        private static PyObject JsonDump(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create("dump() missing required positional arguments");

            var obj = args[0];
            var file = args[1];

            // 선택적 매개변수
            int? indent = null;
            bool sortKeys = false;

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] is PyBool boolArg)
                    sortKeys = boolArg.Value;
                else if (args[i] is PyInt intArg)
                    indent = intArg.Value;
            }

            try
            {
                var jsonString = SerializePyObjectToJson(obj, indent, sortKeys, true);
                
                // 파일 객체에 쓰기 (간단화된 구현)
                if (file is PyString fileName)
                {
                    File.WriteAllText(fileName.Value, jsonString, Encoding.UTF8);
                }
                else
                {
                    // 파일-like 객체의 write 메서드 호출
                    var writeMethod = file.GetAttribute("write");
                    writeMethod.Call(new PyObject[] { new PyString(jsonString) }, null);
                }

                return PyNone.Instance;
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"JSON dump failed: {ex.Message}");
            }
        }

        #endregion

        #region JSON 역직렬화 (JSON → Python)

        public static PyObject JsonLoads(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("loads() missing 1 required positional argument: 's'");

            var jsonString = args[0].ToStr();

            try
            {
                return DeserializeJsonToPyObject(jsonString);
            }
            catch (JsonException ex)
            {
                throw PyJsonDecodeError.Create($"JSON decode error: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw PyValueError.Create($"Invalid JSON: {ex.Message}");
            }
        }

        private static PyObject JsonLoad(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("load() missing 1 required positional argument: 'fp'");

            var file = args[0];
            string jsonContent;

            try
            {
                // 파일 객체에서 읽기
                if (file is PyString fileName)
                {
                    jsonContent = File.ReadAllText(fileName.Value, Encoding.UTF8);
                }
                else
                {
                    // 파일-like 객체의 read 메서드 호출
                    var readMethod = file.GetAttribute("read");
                    var content = readMethod.Call(new PyObject[] {  }, null);
                    jsonContent = content.ToStr();
                }

                return DeserializeJsonToPyObject(jsonContent);
            }
            catch (JsonException ex)
            {
                throw PyJsonDecodeError.Create($"JSON decode error: {ex.Message}");
            }
            catch (FileNotFoundException)
            {
                throw PyFileNotFoundError.Create($"No such file: '{file.ToStr()}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"JSON load failed: {ex.Message}");
            }
        }

        #endregion

        #region 직렬화 구현

        private static string SerializePyObjectToJson(PyObject obj, int? indent, bool sortKeys, bool ensureAscii)
        {
            var options = new JsonWriterOptions
            {
                Indented = indent.HasValue,
                Encoder = ensureAscii ? System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping : null
            };

            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, options);
            
            WritePyObjectToJson(writer, obj, sortKeys);
            writer.Flush();

            var jsonBytes = stream.ToArray();
            var jsonString = Encoding.UTF8.GetString(jsonBytes);

            // 수동 인덴트 처리 (System.Text.Json의 제한적 인덴트 지원 보완)
            if (indent.HasValue && indent.Value > 0)
            {
                jsonString = FormatJsonWithIndent(jsonString, indent.Value);
            }

            return jsonString;
        }

        private static void WritePyObjectToJson(Utf8JsonWriter writer, PyObject obj, bool sortKeys)
        {
            switch (obj)
            {
                case PyNone:
                    writer.WriteNullValue();
                    break;

                case PyBool pyBool:
                    writer.WriteBooleanValue(pyBool.Value);
                    break;

                case PyInt pyInt:
                    writer.WriteNumberValue(pyInt.Value);
                    break;

                case PyFloat pyFloat:
                    writer.WriteNumberValue(pyFloat.Value);
                    break;

                case PyString pyString:
                    writer.WriteStringValue(pyString.Value);
                    break;

                case PyList pyList:
                    writer.WriteStartArray();
                    foreach (var item in pyList.Items)
                    {
                        WritePyObjectToJson(writer, item, sortKeys);
                    }
                    writer.WriteEndArray();
                    break;

                case PyTuple pyTuple:
                    writer.WriteStartArray();
                    foreach (var item in pyTuple.Items)
                    {
                        WritePyObjectToJson(writer, item, sortKeys);
                    }
                    writer.WriteEndArray();
                    break;

                case PyDict pyDict:
                    writer.WriteStartObject();
                    
                    var keysList = pyDict.Keys();
                    var keys = keysList.Items.ToList();
                    if (sortKeys)
                    {
                        keys = keys.OrderBy(k => k.ToStr()).ToList();
                    }

                    foreach (var key in keys)
                    {
                        var keyString = key.ToStr();
                        var value = pyDict.GetItem(key);
                        
                        writer.WritePropertyName(keyString);
                        WritePyObjectToJson(writer, value, sortKeys);
                    }
                    writer.WriteEndObject();
                    break;

                default:
                    throw new JsonException($"Object of type '{obj.GetTypeName()}' is not JSON serializable");
            }
        }

        private static string FormatJsonWithIndent(string json, int indent)
        {
            // 간단한 JSON 포맷팅 (기본적인 들여쓰기)
            var result = new StringBuilder();
            int level = 0;
            bool inString = false;
            bool escaped = false;

            foreach (char c in json)
            {
                if (!inString)
                {
                    switch (c)
                    {
                        case '{':
                        case '[':
                            result.Append(c);
                            result.AppendLine();
                            level++;
                            result.Append(new string(' ', level * indent));
                            break;

                        case '}':
                        case ']':
                            result.AppendLine();
                            level--;
                            result.Append(new string(' ', level * indent));
                            result.Append(c);
                            break;

                        case ',':
                            result.Append(c);
                            result.AppendLine();
                            result.Append(new string(' ', level * indent));
                            break;

                        case ':':
                            result.Append(c);
                            result.Append(' ');
                            break;

                        default:
                            result.Append(c);
                            break;
                    }
                }
                else
                {
                    result.Append(c);
                }

                // 문자열 상태 추적
                if (c == '"' && !escaped)
                    inString = !inString;
                
                escaped = (c == '\\' && !escaped);
            }

            return result.ToString().Trim();
        }

        #endregion

        #region 역직렬화 구현

        private static PyObject DeserializeJsonToPyObject(string json)
        {
            using var document = JsonDocument.Parse(json);
            return ConvertJsonElementToPyObject(document.RootElement);
        }

        private static PyObject ConvertJsonElementToPyObject(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null:
                    return PyNone.Instance;

                case JsonValueKind.True:
                    return PyBool.True;

                case JsonValueKind.False:
                    return PyBool.False;

                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int intValue))
                        return new PyInt(intValue);
                    else if (element.TryGetDouble(out double doubleValue))
                        return new PyFloat(doubleValue);
                    else
                        throw new JsonException($"Number value out of range: {element.GetRawText()}");

                case JsonValueKind.String:
                    return new PyString(element.GetString());

                case JsonValueKind.Array:
                    var listItems = new List<PyObject>();
                    foreach (var item in element.EnumerateArray())
                    {
                        listItems.Add(ConvertJsonElementToPyObject(item));
                    }
                    return new PyList(listItems.ToArray());

                case JsonValueKind.Object:
                    var dict = new PyDict();
                    foreach (var property in element.EnumerateObject())
                    {
                        var key = new PyString(property.Name);
                        var value = ConvertJsonElementToPyObject(property.Value);
                        dict.SetItem(key, value);
                    }
                    return dict;

                default:
                    throw new JsonException($"Unsupported JSON value kind: {element.ValueKind}");
            }
        }

        #endregion
    }

    #region JSON 예외 클래스

    /// <summary>
    /// Python JSONDecodeError
    /// </summary>
    public class PyJsonDecodeError : PyValueError
    {
        public PyJsonDecodeError(string message) : base(message) { }
        public override string GetTypeName() => "JSONDecodeError";

        public new static PythonException Create(string message)
        {
            return new PythonException(new PyJsonDecodeError(message));
        }
    }

    #endregion

    #region JSON 타입 시스템

    /// <summary>
    /// JSONDecodeError 예외 타입
    /// </summary>
    public class PyJsonExceptionType : PyType
    {
        public PyJsonExceptionType(string name) : base(name, new PyType[] { PyType.ValueErrorType })
        {
        }

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            string message = args.Length > 0 ? args[0].ToStr() : "JSON decode error";
            return new PyJsonDecodeError(message);
        }
    }

    /// <summary>
    /// JSONEncoder 클래스 타입
    /// </summary>
    public class PyJsonEncoderType : PyType
    {
        public PyJsonEncoderType(string name) : base(name, new PyType[0])
        {
        }

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            return new PyJsonEncoder();
        }
    }

    /// <summary>
    /// JSONDecoder 클래스 타입
    /// </summary>
    public class PyJsonDecoderType : PyType
    {
        public PyJsonDecoderType(string name) : base(name, new PyType[0])
        {
        }

        public override PyObject Call(PyObject[] args, PyDict kwargs = null)
        {
            return new PyJsonDecoder();
        }
    }

    /// <summary>
    /// JSONEncoder 인스턴스 (간단한 구현)
    /// </summary>
    public class PyJsonEncoder : PyObject
    {
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "JSONEncoder";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "encode":
                    return new PyBuiltinFunction("encode", Encode);
                case "default":
                    return new PyBuiltinFunction("default", Default);
                default:
                    return base.GetAttribute(name);
            }
        }

        private PyObject Encode(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("encode() missing 1 required positional argument: 'obj'");

            return JsonModule.JsonDumps(args);
        }

        private PyObject Default(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("default() missing 1 required positional argument: 'obj'");

            var obj = args[0];
            throw PyTypeError.Create($"Object of type '{obj.GetTypeName()}' is not JSON serializable");
        }
    }

    /// <summary>
    /// JSONDecoder 인스턴스 (간단한 구현)
    /// </summary>
    public class PyJsonDecoder : PyObject
    {
        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "JSONDecoder";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "decode":
                    return new PyBuiltinFunction("decode", Decode);
                default:
                    return base.GetAttribute(name);
            }
        }

        private PyObject Decode(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("decode() missing 1 required positional argument: 's'");

            return JsonModule.JsonLoads(args);
        }
    }

    #endregion
}