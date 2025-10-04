// ASDL (Abstract Syntax Description Language) AST 구조
// CPython 3.12 Parser/Python.asdl 파싱용

using System.Collections.Generic;

namespace SharpPy.PegGenerator.Asdl
{
    /// <summary>
    /// ASDL 모듈 (module Python { ... })
    /// </summary>
    public class AsdlModule
    {
        public string Name { get; set; } = "";
        public List<AsdlType> Types { get; set; } = new();

        public override string ToString() => $"module {Name} {{ {Types.Count} types }}";
    }

    /// <summary>
    /// ASDL 타입 정의 (stmt = ... | ... | ...)
    /// </summary>
    public class AsdlType
    {
        public string Name { get; set; } = ""; // "stmt", "expr", "mod"
        public List<AsdlConstructor> Constructors { get; set; } = new();
        public AsdlAttributes? Attributes { get; set; }

        public override string ToString() => $"{Name} = {string.Join(" | ", Constructors)}";
    }

    /// <summary>
    /// ASDL 생성자 (FunctionDef(...), Return(...))
    /// </summary>
    public class AsdlConstructor
    {
        public string Name { get; set; } = ""; // "FunctionDef", "Return"
        public List<AsdlField> Fields { get; set; } = new();

        public override string ToString() => $"{Name}({Fields.Count} fields)";
    }

    /// <summary>
    /// ASDL 필드 (identifier name, stmt* body, expr? value)
    /// </summary>
    public class AsdlField
    {
        public string Type { get; set; } = ""; // "identifier", "stmt", "expr"
        public string Name { get; set; } = ""; // "name", "body", "value"
        public FieldCardinality Cardinality { get; set; } = FieldCardinality.Single;

        public override string ToString()
        {
            var card = Cardinality switch
            {
                FieldCardinality.Optional => "?",
                FieldCardinality.Sequence => "*",
                _ => ""
            };
            return $"{Type}{card} {Name}";
        }
    }

    /// <summary>
    /// 필드 카디널리티 (단일, 선택적, 시퀀스)
    /// </summary>
    public enum FieldCardinality
    {
        Single,    // field_type field_name
        Optional,  // field_type? field_name
        Sequence   // field_type* field_name
    }

    /// <summary>
    /// ASDL attributes (공통 속성 정의)
    /// </summary>
    public class AsdlAttributes
    {
        public List<AsdlField> Fields { get; set; } = new();

        public override string ToString() => $"attributes({string.Join(", ", Fields)})";
    }

    /// <summary>
    /// ASDL 빌트인 타입
    /// </summary>
    public static class AsdlBuiltins
    {
        public static readonly HashSet<string> BuiltinTypes = new()
        {
            "identifier",  // string
            "int",         // int
            "string",      // string
            "constant",    // object
            "singleton"    // bool? (True/False/None)
        };

        public static bool IsBuiltin(string type) => BuiltinTypes.Contains(type);
    }
}
