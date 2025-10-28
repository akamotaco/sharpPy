using System;
using System.Linq;
using System.Text;
using System.Globalization;

namespace SharpPy
{
    /// <summary>
    /// Python str 타입 구현 - C# string을 기반으로 한 문자열
    /// </summary>
    public class PyString : PyObject
    {
        static PyString()
        {
            InitializeStringDescriptors();
        }

        /// <summary>
        /// Initialize str type descriptors - called from BuiltinsModule
        /// CPython 호환: _PyType_Ready()와 유사하게 한 번만 초기화
        /// Phase 3: Directly populate TypeDict instead of Descriptors
        /// </summary>
        public static void InitializeStringDescriptors()
        {
            var strType = PyType.StrType;

            // join method descriptor - types.py:48에서 필요
            strType.TypeDict["join"] = new PyMethodDescriptor(
                "join", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"join() takes exactly one argument ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'join' requires a 'str' object but received a '{self.GetTypeName()}'");

                    // Use private Join method implementation
                    var iterable = args[0];
                    var items = new System.Collections.Generic.List<string>();

                    if (iterable is PyList list)
                    {
                        foreach (var item in list.Items)
                        {
                            if (item is PyString itemStr)
                                items.Add(itemStr.Value);
                            else
                                throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                        }
                    }
                    else if (iterable is PyTuple tuple)
                    {
                        foreach (var item in tuple.Items)
                        {
                            if (item is PyString itemStr)
                                items.Add(itemStr.Value);
                            else
                                throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                        }
                    }
                    else
                    {
                        throw PyTypeError.Create("can only join an iterable");
                    }

                    return new PyString(string.Join(str.Value, items));
                },
                minArgs: 1, maxArgs: 1
            );

            // 기타 주요 메서드들도 등록
            strType.TypeDict["upper"] = new PyMethodDescriptor(
                "upper", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"upper() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'upper' requires a 'str' object but received a '{self.GetTypeName()}'");
                    return new PyString(str.Value.ToUpperInvariant());
                },
                minArgs: 0, maxArgs: 0
            );

            strType.TypeDict["lower"] = new PyMethodDescriptor(
                "lower", strType,
                (self, args, kwargs) => {
                    if (args.Length != 0)
                        throw PyTypeError.Create($"lower() takes no arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'lower' requires a 'str' object but received a '{self.GetTypeName()}'");
                    return new PyString(str.Value.ToLowerInvariant());
                },
                minArgs: 0, maxArgs: 0
            );

            strType.TypeDict["split"] = new PyMethodDescriptor(
                "split", strType,
                (self, args, kwargs) => {
                    if (args.Length > 2)
                        throw PyTypeError.Create($"split() takes at most 2 arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'split' requires a 'str' object but received a '{self.GetTypeName()}'");

                    string sep = null;
                    int maxsplit = -1;
                    if (args.Length >= 1 && args[0] is PyString sepStr)
                        sep = sepStr.Value;
                    if (args.Length >= 2 && args[1] is PyInt maxsplitInt)
                        maxsplit = (int)maxsplitInt.Value;

                    return str.Split(sep, maxsplit);
                },
                minArgs: 0, maxArgs: 2
            );

            strType.TypeDict["strip"] = new PyMethodDescriptor(
                "strip", strType,
                (self, args, kwargs) => {
                    if (args.Length > 1)
                        throw PyTypeError.Create($"strip() takes at most 1 argument ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'strip' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args.Length == 0)
                        return new PyString(str.Value.Trim());

                    var chars = args[0] is PyString charsStr ? charsStr.Value.ToCharArray() : throw PyTypeError.Create("strip arg must be None or str");
                    return new PyString(str.Value.Trim(chars));
                },
                minArgs: 0, maxArgs: 1
            );

            strType.TypeDict["replace"] = new PyMethodDescriptor(
                "replace", strType,
                (self, args, kwargs) => {
                    if (args.Length < 2 || args.Length > 3)
                        throw PyTypeError.Create($"replace() takes 2 or 3 arguments ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'replace' requires a 'str' object but received a '{self.GetTypeName()}'");

                    var old = args[0] is PyString oldStr ? oldStr.Value : throw PyTypeError.Create("replace() old must be str");
                    var newStr = args[1] is PyString newPyStr ? newPyStr.Value : throw PyTypeError.Create("replace() new must be str");

                    return args.Length == 3
                        ? str.Replace(old, newStr, (int)((PyInt)args[2]).Value)
                        : str.Replace(old, newStr);
                },
                minArgs: 2, maxArgs: 3
            );

            // CPython 3.12: str.zfill(width) - pad string with zeros on the left
            strType.TypeDict["zfill"] = new PyMethodDescriptor(
                "zfill", strType,
                (self, args, kwargs) => {
                    if (args.Length != 1)
                        throw PyTypeError.Create($"zfill() takes exactly one argument ({args.Length} given)");
                    if (self is not PyString str)
                        throw PyTypeError.Create($"descriptor 'zfill' requires a 'str' object but received a '{self.GetTypeName()}'");

                    if (args[0] is not PyInt widthInt)
                        throw PyTypeError.Create("zfill() width must be an integer");

                    int width = (int)widthInt.Value;
                    string value = str.Value;

                    // If already long enough, return unchanged
                    if (value.Length >= width)
                        return str;

                    int fillCount = width - value.Length;

                    // Check if string starts with '+' or '-'
                    if (value.Length > 0 && (value[0] == '+' || value[0] == '-'))
                    {
                        // Move sign to beginning: sign + zeros + rest
                        return new PyString(value[0] + new string('0', fillCount) + value.Substring(1));
                    }
                    else
                    {
                        // Just prepend zeros
                        return new PyString(new string('0', fillCount) + value);
                    }
                },
                minArgs: 1, maxArgs: 1
            );
        }

        #region Core Properties

        public string Value { get; }

        public PyString(string value) => Value = value ?? "";

        public override PyType GetPyType() => PyType.StrType;
        public override string GetTypeName() => "str";

        #endregion

        #region String Representation

        // Python str() - PyString은 이미 문자열이므로 자신을 반환
        public override PyString ToStr() => this;

        public override PyString ToRepr()
        {
            // CPython-compatible repr() implementation
            bool hasSingleQuote = Value.Contains('\'');
            bool hasDoubleQuote = Value.Contains('"');

            char quoteChar;
            if (hasSingleQuote && !hasDoubleQuote)
            {
                // Contains single quotes but no double quotes -> use double quotes
                quoteChar = '"';
            }
            else
            {
                // Default to single quotes (covers: no quotes, only double quotes, or both)
                quoteChar = '\'';
            }

            // Efficient single-pass escape processing with StringBuilder
            var result = new System.Text.StringBuilder(Value.Length + 10); // Pre-allocate with some extra space
            result.Append(quoteChar);

            foreach (char c in Value)
            {
                // Handle special characters based on CPython patterns
                if (c == '\\')
                {
                    result.Append("\\\\");
                }
                else if (c == '\n')
                {
                    result.Append("\\n");
                }
                else if (c == '\t')
                {
                    result.Append("\\t");
                }
                else if (c == '\r')
                {
                    result.Append("\\r");
                }
                else if (c == '\'' && quoteChar == '\'')
                {
                    result.Append("\\'");  // Escape single quote only when using single quotes
                }
                else if (c == '"' && quoteChar == '"')
                {
                    result.Append("\\\""); // Escape double quote only when using double quotes
                }
                else if (!IsPrintableForRepr(c))
                {
                    // Non-printable characters → hex format (following CPython's isprintable() logic)
                    result.Append($"\\x{(int)c:x2}");
                }
                else
                {
                    // Printable ASCII characters (32-126) → as-is
                    result.Append(c);
                }
            }

            result.Append(quoteChar);
            return new PyString(result.ToString());
        }

        /// <summary>
        /// Determines if a character is printable for repr() output, following CPython's isprintable() logic
        /// </summary>
        private static bool IsPrintableForRepr(char c)
        {
            // CPython's isprintable() logic is quite specific
            // We need to match it exactly for compatibility

            var category = char.GetUnicodeCategory(c);

            // Control characters are not printable
            if (category == System.Globalization.UnicodeCategory.Control ||
                category == System.Globalization.UnicodeCategory.Format ||
                category == System.Globalization.UnicodeCategory.Surrogate ||
                category == System.Globalization.UnicodeCategory.PrivateUse ||
                category == System.Globalization.UnicodeCategory.OtherNotAssigned)
            {
                return false;
            }

            // Line and paragraph separators are not printable
            if (category == System.Globalization.UnicodeCategory.LineSeparator ||
                category == System.Globalization.UnicodeCategory.ParagraphSeparator)
            {
                return false;
            }

            // Special case: Non-breaking space (U+00A0) is not printable in CPython
            if (c == '\u00A0')
            {
                return false;
            }

            // Other space separators might have similar rules, but let's start with this specific case
            return true;
        }

        #endregion

        #region Hash and Equality

        public override int ToHash() => Value.GetHashCode();

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(Value == otherStr.Value),
                _ => PyBool.False
            };
        }

        #endregion

        #region Comparison Operations

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(string.Compare(Value, otherStr.Value, StringComparison.Ordinal) < 0),
                _ => throw PyTypeError.Create($"'<' not supported between instances of 'str' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(string.Compare(Value, otherStr.Value, StringComparison.Ordinal) <= 0),
                _ => throw PyTypeError.Create($"'<=' not supported between instances of 'str' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(string.Compare(Value, otherStr.Value, StringComparison.Ordinal) > 0),
                _ => throw PyTypeError.Create($"'>' not supported between instances of 'str' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                PyString otherStr => PyBool.FromBool(string.Compare(Value, otherStr.Value, StringComparison.Ordinal) >= 0),
                _ => throw PyTypeError.Create($"'>=' not supported between instances of 'str' and '{other.GetTypeName()}'")
            };
        }

        #endregion

        #region String Operations

        /// <summary>
        /// 문자열 연결 (+ 연산자)
        /// </summary>
        public override PyObject Add(PyObject other)
        {
            return other switch
            {
                PyString otherStr => new PyString(Value + otherStr.Value),
                _ => throw PyTypeError.Create($"can only concatenate str (not \"{other.GetTypeName()}\") to str")
            };
        }

        /// <summary>
        /// 문자열 반복 (* 연산자)
        /// </summary>
        public override PyObject Multiply(PyObject other)
        {
            return other switch
            {
                PyInt count => count.Value <= 0
                    ? new PyString("")
                    : new PyString(string.Concat(Enumerable.Repeat(Value, (int)count.Value))),
                _ => throw PyTypeError.Create($"can't multiply sequence by non-int of type '{other.GetTypeName()}'")
            };
        }

        /// <summary>
        /// 문자열 포맷팅 (% 연산자) - CPython 3.12 호환
        /// 예: "Hello %s" % "world" -> "Hello world"
        ///     "_%s__" % (name,) -> "_name__"
        /// </summary>
        public override PyObject Modulo(PyObject other)
        {
            // CPython 3.12: Support both single values and tuples
            PyObject[] values;

            if (other is PyTuple tuple)
            {
                // Tuple of values: "Hello %s %d" % ("world", 42)
                values = tuple.Items;
            }
            else
            {
                // Single value: "Hello %s" % "world"
                values = new PyObject[] { other };
            }

            try
            {
                // Simple % formatting - support %s, %d, %r
                var result = Value;
                int valueIndex = 0;

                for (int i = 0; i < result.Length; i++)
                {
                    if (result[i] == '%' && i + 1 < result.Length)
                    {
                        char formatChar = result[i + 1];

                        if (formatChar == '%')
                        {
                            // Escape: %% -> %
                            result = result.Remove(i, 1);
                            continue;
                        }

                        if (valueIndex >= values.Length)
                        {
                            throw PyTypeError.Create("not enough arguments for format string");
                        }

                        var value = values[valueIndex++];
                        string replacement;

                        switch (formatChar)
                        {
                            case 's': // String
                                replacement = value is PyString str ? str.Value : value.ToStr().Value;
                                break;
                            case 'r': // Repr
                                replacement = value.ToRepr().Value;
                                break;
                            case 'd': // Decimal integer
                            case 'i': // Integer
                                replacement = value is PyInt pyInt ? pyInt.Value.ToString() : value.ToStr().Value;
                                break;
                            case 'f': // Float
                                if (value is PyFloat pyFloat)
                                    replacement = pyFloat.Value.ToString(CultureInfo.InvariantCulture);
                                else if (value is PyInt pyIntForFloat)
                                    replacement = ((double)pyIntForFloat.Value).ToString(CultureInfo.InvariantCulture);
                                else
                                    replacement = value.ToStr().Value;
                                break;
                            default:
                                throw PyValueError.Create($"unsupported format character '{formatChar}' (0x{(int)formatChar:x}) at index {i + 1}");
                        }

                        // Replace %X with the value
                        result = result.Substring(0, i) + replacement + result.Substring(i + 2);
                        i += replacement.Length - 1; // Adjust index after replacement
                    }
                }

                if (valueIndex < values.Length)
                {
                    throw PyTypeError.Create("not all arguments converted during string formatting");
                }

                return new PyString(result);
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw PyTypeError.Create($"unsupported operand type(s) for %: 'str' and '{other.GetTypeName()}': {ex.Message}");
            }
        }

        /// <summary>
        /// 인덱스 접근 str[i]
        /// </summary>
        public PyString GetItem(int index)
        {
            // Python식 음수 인덱스 지원
            if (index < 0) index += Value.Length;
            
            if (index < 0 || index >= Value.Length)
                throw PyIndexError.Create("string index out of range");
            
            return new PyString(Value[index].ToString());
        }
        
        /// <summary>
        /// PyObject.GetItem 오버라이드 - 인덱싱 및 슬라이싱 지원
        /// </summary>
        public override PyObject GetItem(PyObject index)
        {
            if (index is PyInt pyInt)
            {
                return GetItem((int)pyInt.Value);
            }
            else if (index is PySlice slice)
            {
                // 슬라이싱 처리
                var (start, stop, step) = slice.Indices(Value.Length);
                
                var chars = new List<char>();
                if (step > 0)
                {
                    for (int i = start; i < stop; i += step)
                    {
                        if (i >= 0 && i < Value.Length)
                            chars.Add(Value[i]);
                    }
                }
                else if (step < 0)
                {
                    for (int i = start; i > stop; i += step)
                    {
                        if (i >= 0 && i < Value.Length)
                            chars.Add(Value[i]);
                    }
                }
                
                return new PyString(new string(chars.ToArray()));
            }
            else
            {
                throw PyTypeError.Create($"string indices must be integers or slices, not {index.GetTypeName()}");
            }
        }

        /// <summary>
        /// 슬라이싱 str[start:end]
        /// </summary>
        public PyString GetSlice(int? start = null, int? end = null, int step = 1)
        {
            if (step == 0)
                throw PyValueError.Create("slice step cannot be zero");
            
            var len = Value.Length;
            var actualStart = start ?? (step > 0 ? 0 : len - 1);
            var actualEnd = end ?? (step > 0 ? len : -1);
            
            // 음수 인덱스 정규화
            if (actualStart < 0) actualStart += len;
            if (actualEnd < 0) actualEnd += len;
            
            var result = new StringBuilder();
            if (step > 0)
            {
                for (int i = Math.Max(0, actualStart); i < Math.Min(len, actualEnd); i += step)
                {
                    result.Append(Value[i]);
                }
            }
            else
            {
                for (int i = Math.Min(len - 1, actualStart); i > Math.Max(-1, actualEnd); i += step)
                {
                    result.Append(Value[i]);
                }
            }
            
            return new PyString(result.ToString());
        }

        #endregion

        #region String Methods

        public PyString Upper() => new PyString(Value.ToUpper());
        public PyString Lower() => new PyString(Value.ToLower());
        public PyString Capitalize() => new PyString(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Value.ToLower()));
        public PyString Title() => Capitalize(); // 간단한 구현
        
        public PyString Strip(string chars = null)
        {
            if (chars == null)
                return new PyString(Value.Trim());
            return new PyString(Value.Trim(chars.ToCharArray()));
        }
        
        public PyString LStrip(string chars = null)
        {
            if (chars == null)
                return new PyString(Value.TrimStart());
            return new PyString(Value.TrimStart(chars.ToCharArray()));
        }
        
        public PyString RStrip(string chars = null)
        {
            if (chars == null)
                return new PyString(Value.TrimEnd());
            return new PyString(Value.TrimEnd(chars.ToCharArray()));
        }

        public PyString Replace(string old, string newStr, int count = -1)
        {
            if (count == -1)
                return new PyString(Value.Replace(old, newStr));
            
            // 제한된 횟수만 바꾸기
            var result = Value;
            for (int i = 0; i < count; i++)
            {
                var index = result.IndexOf(old);
                if (index == -1) break;
                result = result.Substring(0, index) + newStr + result.Substring(index + old.Length);
            }
            return new PyString(result);
        }

        public PyInt Find(string sub, int start = 0, int? end = null)
        {
            var actualEnd = end ?? Value.Length;
            if (start < 0) start = 0;
            if (actualEnd > Value.Length) actualEnd = Value.Length;
            
            var searchIn = start == 0 && actualEnd == Value.Length 
                ? Value 
                : Value.Substring(start, actualEnd - start);
                
            var index = searchIn.IndexOf(sub);
            return new PyInt(index == -1 ? -1 : index + start);
        }

        public PyInt Index(string sub, int start = 0, int? end = null)
        {
            var result = Find(sub, start, end);
            if (((PyInt)result).Value == -1)
                throw PyValueError.Create("substring not found");
            return (PyInt)result;
        }

        public PyInt Count(string sub)
        {
            if (string.IsNullOrEmpty(sub)) return new PyInt(Value.Length + 1);
            
            int count = 0;
            int index = 0;
            while ((index = Value.IndexOf(sub, index)) != -1)
            {
                count++;
                index += sub.Length;
            }
            return new PyInt(count);
        }

        public PyBool StartsWith(string prefix) => PyBool.FromBool(Value.StartsWith(prefix));
        public PyBool EndsWith(string suffix) => PyBool.FromBool(Value.EndsWith(suffix));

        public PyList Split(string sep = null, int maxsplit = -1)
        {
            string[] parts;
            
            if (sep == null)
            {
                // 공백문자로 분리
                var options = StringSplitOptions.RemoveEmptyEntries;
                parts = maxsplit == -1 
                    ? Value.Split(new char[0], options)
                    : Value.Split(new char[0], maxsplit + 1, options);
            }
            else
            {
                parts = maxsplit == -1 
                    ? Value.Split(new[] { sep }, StringSplitOptions.None)
                    : Value.Split(new[] { sep }, maxsplit + 1, StringSplitOptions.None);
            }
            
            return new PyList(parts.Select(p => new PyString(p)).Cast<PyObject>().ToArray());
        }

        public PyString Join(PyObject iterable)
        {
            return iterable switch
            {
                PyList list => new PyString(string.Join(Value, list.Items.Select(item => item switch
                {
                    PyString str => str.Value,
                    _ => throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found")
                }))),
                _ => throw PyTypeError.Create($"can only join an iterable")
            };
        }

        #endregion

        #region Type Testing Methods

        public PyBool IsDigit() => PyBool.FromBool(Value.All(char.IsDigit) && Value.Length > 0);
        public PyBool IsAlpha() => PyBool.FromBool(Value.All(char.IsLetter) && Value.Length > 0);
        public PyBool IsAlnum() => PyBool.FromBool(Value.All(char.IsLetterOrDigit) && Value.Length > 0);
        public PyBool IsSpace() => PyBool.FromBool(Value.All(char.IsWhiteSpace) && Value.Length > 0);
        public PyBool IsUpper() => PyBool.FromBool(Value.Any(char.IsLetter) && Value.All(c => !char.IsLetter(c) || char.IsUpper(c)));
        public PyBool IsLower() => PyBool.FromBool(Value.Any(char.IsLetter) && Value.All(c => !char.IsLetter(c) || char.IsLower(c)));
        public PyBool IsTitle() => PyBool.FromBool(Value == Capitalize().Value);

        #endregion

        #region Length and Contains

        public override int Length() => Value.Length;
        
        public PyBool Contains(string substring) => PyBool.FromBool(Value.Contains(substring));
        
        /// <summary>
        /// CPython __contains__ 메소드 구현 - PyObject 버전
        /// </summary>
        public override PyBool Contains(PyObject item)
        {
            if (item is PyString pyStr)
            {
                return PyBool.FromBool(Value.Contains(pyStr.Value));
            }
            
            // 다른 타입은 문자열로 변환하여 검사
            var itemStr = item.ToStr();
            return PyBool.FromBool(Value.Contains(itemStr.Value));
        }

        #endregion

        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyString → C# basic types) ===
        
        /// <summary>
        /// CPython PyObject_IsTrue 호환: PyString에서 C# bool 값 추출
        /// </summary>
        public override bool PyBoolValue() => Value.Length > 0;
        
        /// <summary>
        /// CPython PyUnicode_AsLong 호환: PyString에서 C# int 값 추출
        /// </summary>
        public override int ToInt()
        {
            if (int.TryParse(Value.Trim(), out int result))
                return result;
            throw PyValueError.Create($"invalid literal for int() with base 10: '{Value}'");
        }
        
        /// <summary>
        /// CPython PyUnicode_AsDouble 호환: PyString에서 C# double 값 추출
        /// </summary>
        public override double ToFloat()
        {
            if (double.TryParse(Value.Trim(), out double result))
                return result;
            throw PyValueError.Create($"could not convert string to float: '{Value}'");
        }
        
        // === As* Methods: Type Conversion (PyString → PyObject types) ===
        
        /// <summary>
        /// C# 네이티브 타입 변환: PyString → C# string
        /// </summary>
        public override string AsString() => Value;
        
        /// <summary>
        /// CPython 호환: PyString을 PyInt로 변환
        /// </summary>
        public override PyInt AsInt()
        {
            return new PyInt(ToInt());
        }
        
        /// <summary>
        /// CPython 호환: PyString을 PyFloat로 변환
        /// </summary>
        public override PyFloat AsFloat()
        {
            return new PyFloat(ToFloat());
        }
        
        /// <summary>
        /// CPython 호환: PyString을 PyBool로 변환
        /// </summary>
        public override PyBool AsBool()
        {
            return PyBool.FromBool(Value.Length > 0);
        }
        
        /// <summary>
        /// CPython 호환: PyString을 PyList로 변환 (각 문자를 PyString 요소로)
        /// </summary>
        public override PyList AsList()
        {
            var items = Value.Select(c => new PyString(c.ToString()) as PyObject).ToList();
            return new PyList(items);
        }

        #endregion

        #region Encoding (Simplified)

        /// <summary>
        /// str.encode() - UTF-8로 인코딩 (간단한 구현)
        /// </summary>
        public PyBytes Encode(string encoding = "utf-8")
        {
            try
            {
                byte[] bytes;
                var lowerEncoding = encoding.ToLower();
                if (lowerEncoding == "utf-8" || lowerEncoding == "utf8")
                    bytes = Encoding.UTF8.GetBytes(Value);
                else if (lowerEncoding == "ascii")
                    bytes = Encoding.ASCII.GetBytes(Value);
                else if (lowerEncoding == "unicode")
                    bytes = Encoding.Unicode.GetBytes(Value);
                else
                    throw PyLookupError.Create($"unknown encoding: {encoding}");
                return new PyBytes(bytes);
            }
            catch (Exception)
            {
                throw PyValueError.Create($"'{encoding}' codec can't encode characters");
            }
        }

        #endregion

        #region Static Factory Methods

        public static PyString FromBytes(byte[] bytes, string encoding = "utf-8")
        {
            try
            {
                string result;
                var lowerEncoding = encoding.ToLower();
                if (lowerEncoding == "utf-8" || lowerEncoding == "utf8")
                    result = Encoding.UTF8.GetString(bytes);
                else if (lowerEncoding == "ascii")
                    result = Encoding.ASCII.GetString(bytes);
                else if (lowerEncoding == "unicode")
                    result = Encoding.Unicode.GetString(bytes);
                else
                    throw PyLookupError.Create($"unknown encoding: {encoding}");
                return new PyString(result);
            }
            catch (Exception)
            {
                throw PyValueError.Create($"'{encoding}' codec can't decode bytes");
            }
        }

        #endregion

        #region Constants

        public static readonly PyString Empty = new PyString("");

        #endregion

        #region String Methods

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "upper" => new PyStringMethod(this, "upper", Upper),
                "lower" => new PyStringMethod(this, "lower", Lower),
                "title" => new PyStringMethod(this, "title", TitleMethod),
                "strip" => new PyStringMethod(this, "strip", Strip),
                "lstrip" => new PyStringMethod(this, "lstrip", LStrip),
                "rstrip" => new PyStringMethod(this, "rstrip", RStrip),
                "replace" => new PyStringMethod(this, "replace", Replace),
                "split" => new PyStringMethod(this, "split", Split),
                "join" => new PyStringMethod(this, "join", Join),
                "startswith" => new PyStringMethod(this, "startswith", StartsWith),
                "endswith" => new PyStringMethod(this, "endswith", EndsWith),
                "find" => new PyStringMethod(this, "find", Find),
                "count" => new PyStringMethod(this, "count", Count),
                "encode" => new PyStringMethod(this, "encode", EncodeMethod),
                _ => base.GetAttribute(name)
            };
        }

        private PyObject Upper(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"upper() takes no arguments ({args.Length} given)");
            return new PyString(Value.ToUpperInvariant());
        }

        private PyObject Lower(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"lower() takes no arguments ({args.Length} given)");
            return new PyString(Value.ToLowerInvariant());
        }

        private PyObject TitleMethod(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"title() takes no arguments ({args.Length} given)");
            return Title();
        }

        private PyObject Strip(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"strip() takes at most 1 argument ({args.Length} given)");
            
            if (args.Length == 0)
                return new PyString(Value.Trim());
            
            var chars = args[0] switch
            {
                PyString str => str.Value.ToCharArray(),
                _ => throw PyTypeError.Create("strip arg must be None or str")
            };
            
            return new PyString(Value.Trim(chars));
        }

        private PyObject LStrip(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"lstrip() takes at most 1 argument ({args.Length} given)");
            
            if (args.Length == 0)
                return new PyString(Value.TrimStart());
            
            var chars = args[0] switch
            {
                PyString str => str.Value.ToCharArray(),
                _ => throw PyTypeError.Create("lstrip arg must be None or str")
            };
            
            return new PyString(Value.TrimStart(chars));
        }

        private PyObject RStrip(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"rstrip() takes at most 1 argument ({args.Length} given)");
            
            if (args.Length == 0)
                return new PyString(Value.TrimEnd());
            
            var chars = args[0] switch
            {
                PyString str => str.Value.ToCharArray(),
                _ => throw PyTypeError.Create("rstrip arg must be None or str")
            };
            
            return new PyString(Value.TrimEnd(chars));
        }

        private PyObject Replace(PyObject[] args)
        {
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"replace() takes 2 or 3 arguments ({args.Length} given)");
            
            var old = args[0] switch
            {
                PyString str => str.Value,
                _ => throw PyTypeError.Create("replace() old must be str")
            };
            
            var newStr = args[1] switch
            {
                PyString str => str.Value,
                _ => throw PyTypeError.Create("replace() new must be str")
            };
            
            if (args.Length == 3)
            {
                var count = args[2] switch
                {
                    PyInt i => i.Value,
                    _ => throw PyTypeError.Create("replace() count must be int")
                };
                
                var result = Value;
                for (int i = 0; i < count && result.Contains(old); i++)
                {
                    var index = result.IndexOf(old);
                    if (index == -1) break;
                    result = result.Substring(0, index) + newStr + result.Substring(index + old.Length);
                }
                return new PyString(result);
            }
            
            return new PyString(Value.Replace(old, newStr));
        }

        private PyObject Split(PyObject[] args)
        {
            if (args.Length > 2)
                throw PyTypeError.Create($"split() takes at most 2 arguments ({args.Length} given)");
            
            string sep = null;
            int maxsplit = -1;
            
            if (args.Length >= 1 && args[0] is PyString sepStr)
                sep = sepStr.Value;
            
            if (args.Length >= 2 && args[1] is PyInt maxsplitInt)
                maxsplit = (int)maxsplitInt.Value;

            return Split(sep, maxsplit);
        }

        private PyObject Join(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"join() takes exactly one argument ({args.Length} given)");

            var iterable = args[0];
            var items = new System.Collections.Generic.List<string>();

            // CPython 3.12: Support all iterables (list, tuple, generator, etc.)
            if (iterable is PyList list)
            {
                foreach (var item in list.Items)
                {
                    if (item is PyString str)
                        items.Add(str.Value);
                    else
                        throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                }
            }
            else if (iterable is PyTuple tuple)
            {
                foreach (var item in tuple.Items)
                {
                    if (item is PyString str)
                        items.Add(str.Value);
                    else
                        throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                }
            }
            else if (iterable is PyGenerator generator)
            {
                // CPython 3.12: Consume generator expression
                while (true)
                {
                    try
                    {
                        var item = generator.Next();
                        if (item is PyString str)
                            items.Add(str.Value);
                        else
                            throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                    }
                    catch (PythonException ex) when (ex.PyException is PyStopIteration)
                    {
                        break;
                    }
                }
            }
            else
            {
                // CPython 3.12: Try to iterate using __iter__
                try
                {
                    var iterMethod = iterable.GetAttribute("__iter__");
                    if (iterMethod != null && iterMethod != PyNone.Instance)
                    {
                        var iterator = iterMethod.Call(new PyObject[0], null);
                        var nextMethod = iterator.GetAttribute("__next__");

                        while (true)
                        {
                            try
                            {
                                var item = nextMethod.Call(new PyObject[0], null);
                                if (item is PyString str)
                                    items.Add(str.Value);
                                else
                                    throw PyTypeError.Create($"sequence item: expected str instance, {item.GetTypeName()} found");
                            }
                            catch (PythonException ex) when (ex.PyException is PyStopIteration)
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        throw PyTypeError.Create("can only join an iterable");
                    }
                }
                catch (PythonException ex) when (ex.PyException is not PyStopIteration)
                {
                    throw PyTypeError.Create("can only join an iterable");
                }
                catch (Exception)
                {
                    throw PyTypeError.Create("can only join an iterable");
                }
            }

            return new PyString(string.Join(Value, items));
        }

        private PyObject StartsWith(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"startswith() takes exactly one argument ({args.Length} given)");
            
            var prefix = args[0] switch
            {
                PyString str => str.Value,
                _ => throw PyTypeError.Create("startswith first arg must be str")
            };
            
            return PyBool.FromBool(Value.StartsWith(prefix));
        }

        private PyObject EndsWith(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"endswith() takes exactly one argument ({args.Length} given)");
            
            var suffix = args[0] switch
            {
                PyString str => str.Value,
                _ => throw PyTypeError.Create("endswith first arg must be str")
            };
            
            return PyBool.FromBool(Value.EndsWith(suffix));
        }

        private PyObject Find(PyObject[] args)
        {
            if (args.Length < 1 || args.Length > 3)
                throw PyTypeError.Create($"find() takes 1 to 3 arguments ({args.Length} given)");

            var sub = args[0] switch
            {
                PyString str => str.Value,
                _ => throw PyTypeError.Create("find() sub must be str")
            };

            int start = 0;
            int end = Value.Length;

            // Handle negative indices (Python convention: -1 means last character)
            if (args.Length >= 2 && args[1] is PyInt startInt)
            {
                start = (int)startInt.Value;
                if (start < 0) start += Value.Length;
                start = Math.Max(0, Math.Min(Value.Length, start));
            }

            if (args.Length >= 3 && args[2] is PyInt endInt)
            {
                end = (int)endInt.Value;
                if (end < 0) end += Value.Length;
                end = Math.Max(0, Math.Min(Value.Length, end));
            }

            if (start >= end) return new PyInt(-1);
            
            var substring = start == 0 && end == Value.Length 
                ? Value 
                : Value.Substring(start, end - start);
                
            var index = substring.IndexOf(sub);
            return new PyInt(index == -1 ? -1 : index + start);
        }

        private PyObject Count(PyObject[] args)
        {
            if (args.Length < 1 || args.Length > 3)
                throw PyTypeError.Create($"count() takes 1 to 3 arguments ({args.Length} given)");

            var sub = args[0] switch
            {
                PyString str => str.Value,
                _ => throw PyTypeError.Create("count() sub must be str")
            };

            int start = 0;
            int end = Value.Length;

            // Handle negative indices (Python convention: -1 means last character)
            if (args.Length >= 2 && args[1] is PyInt startInt)
            {
                start = (int)startInt.Value;
                if (start < 0) start += Value.Length;
                start = Math.Max(0, Math.Min(Value.Length, start));
            }

            if (args.Length >= 3 && args[2] is PyInt endInt)
            {
                end = (int)endInt.Value;
                if (end < 0) end += Value.Length;
                end = Math.Max(0, Math.Min(Value.Length, end));
            }

            if (start >= end || sub.Length == 0) return new PyInt(0);
            
            var substring = start == 0 && end == Value.Length 
                ? Value 
                : Value.Substring(start, end - start);
                
            return CountOccurrences(substring, sub);
        }

        private PyObject CountOccurrences(string text, string sub)
        {
            if (string.IsNullOrEmpty(sub)) return new PyInt(0);
            
            int count = 0;
            int index = 0;
            
            while ((index = text.IndexOf(sub, index)) != -1)
            {
                count++;
                index += sub.Length;
            }
            
            return new PyInt(count);
        }

        private PyObject EncodeMethod(PyObject[] args)
        {
            if (args.Length > 2)
                throw PyTypeError.Create($"encode() takes at most 2 arguments ({args.Length} given)");
            
            string encoding = "utf-8";
            if (args.Length >= 1)
            {
                if (args[0] is PyString encodingStr)
                    encoding = encodingStr.Value;
                else
                    throw PyTypeError.Create("encode() encoding must be str");
            }
            
            // Second argument (errors) is ignored for now
            return Encode(encoding);
        }

        #endregion

        #region Iterator Support

        /// <summary>
        /// Iterator support for string iteration (for char in "string")
        /// </summary>
        public override PyObject GetIterator()
        {
            return new PyStringIterator(this);
        }

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // String literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion
    }
}