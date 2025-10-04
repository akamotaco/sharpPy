// ASDL (Abstract Syntax Description Language) 파서
// CPython 3.12 Parser/Python.asdl 형식 파싱

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SharpPy.PegGenerator.Asdl
{
    public class AsdlParser
    {
        private string[] _lines = Array.Empty<string>();
        private int _lineIndex = 0;

        public AsdlModule Parse(string asdlContent)
        {
            // 주석 제거 및 라인 분리
            _lines = asdlContent
                .Split('\n')
                .Select(line => RemoveComment(line).Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            _lineIndex = 0;

            // module Python { ... } 파싱
            var module = new AsdlModule();

            // "module Python" 찾기
            while (_lineIndex < _lines.Length)
            {
                var line = _lines[_lineIndex];
                if (line.StartsWith("module "))
                {
                    var match = Regex.Match(line, @"module\s+(\w+)");
                    if (match.Success)
                    {
                        module.Name = match.Groups[1].Value;
                        _lineIndex++;
                        break;
                    }
                }
                _lineIndex++;
            }

            // '{' 찾기
            while (_lineIndex < _lines.Length && !_lines[_lineIndex].Contains("{"))
            {
                _lineIndex++;
            }
            _lineIndex++;

            // 타입 정의 파싱
            while (_lineIndex < _lines.Length)
            {
                var line = _lines[_lineIndex];

                // '}' 만나면 종료
                if (line.Contains("}"))
                {
                    break;
                }

                // 타입 정의 파싱 (stmt = ... | ...)
                if (line.Contains("="))
                {
                    var type = ParseType();
                    if (type != null)
                    {
                        module.Types.Add(type);
                    }
                }
                else
                {
                    _lineIndex++;
                }
            }

            return module;
        }

        private AsdlType? ParseType()
        {
            var type = new AsdlType();

            // 첫 번째 라인: "stmt = Constructor(...)"
            var line = _lines[_lineIndex];
            var parts = line.Split('=', 2);
            if (parts.Length != 2)
            {
                _lineIndex++;
                return null;
            }

            type.Name = parts[0].Trim();
            var firstConstructor = parts[1].Trim();

            // 첫 번째 생성자 파싱 (다중 라인 가능)
            if (!string.IsNullOrEmpty(firstConstructor) && !firstConstructor.StartsWith("|"))
            {
                var constructorStr = CollectConstructorLines(firstConstructor);

                // Product type 처리: (field1, field2, ...)
                if (constructorStr.TrimStart().StartsWith("("))
                {
                    // Product type: type 이름을 constructor 이름으로 사용
                    var fieldsMatch = Regex.Match(constructorStr, @"^\s*\((.*)\)");
                    if (fieldsMatch.Success)
                    {
                        var constructor = new AsdlConstructor
                        {
                            Name = type.Name,  // CPython: Product type은 type 이름 사용
                            Fields = ParseFields(fieldsMatch.Groups[1].Value)
                        };
                        type.Constructors.Add(constructor);
                    }
                }
                else
                {
                    // Sum type: 일반 constructor
                    var constructor = ParseConstructor(constructorStr);
                    if (constructor != null)
                    {
                        type.Constructors.Add(constructor);
                    }
                }
            }

            _lineIndex++;

            // 나머지 생성자 파싱 (| Constructor(...))
            while (_lineIndex < _lines.Length)
            {
                line = _lines[_lineIndex];

                // attributes(...) 만나면 처리
                if (line.TrimStart().StartsWith("attributes"))
                {
                    type.Attributes = ParseAttributes(line);
                    _lineIndex++;
                    break;
                }

                // | Constructor 아니면 종료
                if (!line.TrimStart().StartsWith("|"))
                {
                    break;
                }

                // | 제거하고 생성자 파싱 (다중 라인 가능)
                var constructorStart = line.TrimStart().Substring(1).Trim();
                var constructorStr = CollectConstructorLines(constructorStart);
                var constructor = ParseConstructor(constructorStr);
                if (constructor != null)
                {
                    type.Constructors.Add(constructor);
                }

                _lineIndex++;
            }

            return type;
        }

        private string CollectConstructorLines(string firstLine)
        {
            // 괄호가 닫혀있으면 한 줄짜리
            if (!firstLine.Contains("("))
            {
                return firstLine; // Pass, Break, Continue 같은 경우
            }

            // 열린 괄호와 닫힌 괄호 개수 세기
            int openCount = firstLine.Count(c => c == '(');
            int closeCount = firstLine.Count(c => c == ')');

            // 이미 균형이 맞으면 한 줄짜리
            if (openCount == closeCount)
            {
                return firstLine;
            }

            // 다중 라인: 다음 라인들을 읽어서 괄호 균형 맞추기
            var fullStr = firstLine;
            _lineIndex++;

            while (_lineIndex < _lines.Length)
            {
                var line = _lines[_lineIndex];

                // | 또는 attributes로 시작하면 다른 생성자/속성 시작
                if (line.TrimStart().StartsWith("|") || line.TrimStart().StartsWith("attributes"))
                {
                    _lineIndex--; // 다시 되돌림
                    break;
                }

                fullStr += " " + line;
                openCount += line.Count(c => c == '(');
                closeCount += line.Count(c => c == ')');

                // 괄호 균형 맞음
                if (openCount == closeCount)
                {
                    break;
                }

                _lineIndex++;
            }

            return fullStr;
        }

        private AsdlConstructor? ParseConstructor(string constructorStr)
        {
            // CPython 3.12 ASDL 형식:
            // Sum type: FunctionDef(identifier name, arguments args, ...)
            // Product type: (identifier name, arguments args, ...) - constructor 이름 없음
            // Singleton: Pass | Break | Continue (괄호 없음)

            // Product type 체크: 괄호로 시작
            if (constructorStr.TrimStart().StartsWith("("))
            {
                // Product type은 constructor 이름이 없으므로 null 반환
                // 호출자가 type 이름을 사용하도록 함
                return null;
            }

            var match = Regex.Match(constructorStr, @"^(\w+)(?:\((.*)\))?");
            if (!match.Success)
            {
                return null;
            }

            var constructor = new AsdlConstructor
            {
                Name = match.Groups[1].Value
            };

            // 괄호 안의 필드들
            if (match.Groups[2].Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
            {
                var fieldsStr = match.Groups[2].Value;
                constructor.Fields = ParseFields(fieldsStr);
            }

            return constructor;
        }

        private List<AsdlField> ParseFields(string fieldsStr)
        {
            var fields = new List<AsdlField>();

            // 필드는 쉼표로 구분: "identifier name, stmt* body, expr? value"
            // 하지만 중첩 괄호 고려: "keyword* keywords" 같은 경우

            var parts = SplitFields(fieldsStr);

            foreach (var part in parts)
            {
                var field = ParseField(part.Trim());
                if (field != null)
                {
                    fields.Add(field);
                }
            }

            return fields;
        }

        private AsdlField? ParseField(string fieldStr)
        {
            // "type_name field_name" 또는 "type_name? field_name" 또는 "type_name* field_name"
            var match = Regex.Match(fieldStr, @"^(\w+)([*?]?)\s+(\w+)$");
            if (!match.Success)
            {
                return null;
            }

            var field = new AsdlField
            {
                Type = match.Groups[1].Value,
                Name = match.Groups[3].Value
            };

            var cardinality = match.Groups[2].Value;
            field.Cardinality = cardinality switch
            {
                "?" => FieldCardinality.Optional,
                "*" => FieldCardinality.Sequence,
                _ => FieldCardinality.Single
            };

            return field;
        }

        private AsdlAttributes? ParseAttributes(string line)
        {
            // attributes (int lineno, int col_offset, ...)
            var match = Regex.Match(line, @"attributes\s*\((.*)\)");
            if (!match.Success)
            {
                return null;
            }

            var fieldsStr = match.Groups[1].Value;
            var fields = ParseFields(fieldsStr);

            return new AsdlAttributes { Fields = fields };
        }

        private List<string> SplitFields(string fieldsStr)
        {
            // 쉼표로 필드 분리 (중첩 괄호 고려)
            var fields = new List<string>();
            var current = "";
            var depth = 0;

            foreach (var c in fieldsStr)
            {
                if (c == '(')
                {
                    depth++;
                    current += c;
                }
                else if (c == ')')
                {
                    depth--;
                    current += c;
                }
                else if (c == ',' && depth == 0)
                {
                    if (!string.IsNullOrWhiteSpace(current))
                    {
                        fields.Add(current.Trim());
                    }
                    current = "";
                }
                else
                {
                    current += c;
                }
            }

            if (!string.IsNullOrWhiteSpace(current))
            {
                fields.Add(current.Trim());
            }

            return fields;
        }

        private string RemoveComment(string line)
        {
            // -- 주석 제거
            var commentIndex = line.IndexOf("--");
            if (commentIndex >= 0)
            {
                return line.Substring(0, commentIndex);
            }
            return line;
        }
    }
}
