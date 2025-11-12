namespace SharpPy.PegGenerator.DataStructures;

/// <summary>
/// ASDL 모듈 (Python.asdl의 최상위 구조)
/// 예: module Python { ... }
/// </summary>
public class AsdlModule
{
    public string Name { get; set; } = "";
    public List<AsdlType> Types { get; set; } = new();
}

/// <summary>
/// ASDL 타입 (Sum Type)
/// 예: stmt = FunctionDef(...) | Return(...) | Pass
/// </summary>
public class AsdlType
{
    public string Name { get; set; } = "";
    public List<AsdlConstructor> Constructors { get; set; } = new();
    public List<AsdlField>? Attributes { get; set; }  // attributes (lineno, col_offset, ...)
}

/// <summary>
/// ASDL Constructor (Product Type)
/// 예: FunctionDef(identifier name, arguments args, stmt* body, expr? returns)
/// </summary>
public class AsdlConstructor
{
    public string Name { get; set; } = "";
    public List<AsdlField> Fields { get; set; } = new();
}

/// <summary>
/// ASDL Field
/// 예: identifier name, stmt* body, expr? returns
/// </summary>
public class AsdlField
{
    public string Type { get; set; } = "";        // identifier, stmt, expr
    public string Name { get; set; } = "";        // name, body, returns
    public bool IsOptional { get; set; }          // ? 붙음
    public bool IsSequence { get; set; }          // * 붙음

    public AsdlField() { }

    public AsdlField(string type, string name, bool isOptional = false, bool isSequence = false)
    {
        Type = type;
        Name = name;
        IsOptional = isOptional;
        IsSequence = isSequence;
    }
}
