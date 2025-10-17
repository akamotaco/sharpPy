namespace SharpPy.PegGenerator.DataStructures;

/// <summary>
/// PEG 규칙 (python_py.gram의 한 규칙)
/// 예: if_stmt: | 'if' named_expression ':' block elif_stmt
/// 타입 어노테이션: file[GeneratedMod]: ...
/// </summary>
public class PegRule
{
    public string Name { get; set; } = "";
    public string? ReturnType { get; set; }  // 규칙의 반환 타입 (예: GeneratedMod, GeneratedExpr)
    public List<Alternative> Alternatives { get; set; } = new();
}

/// <summary>
/// PEG Alternative (| 로 구분된 대안)
/// </summary>
public class Alternative
{
    public List<Item> Items { get; set; } = new();

    /// <summary>
    /// C# 액션 코드 (python_cs.gram의 { ... } 블록)
    /// 예: { PyParserHelpers._PyAST_Pass(...) }
    /// </summary>
    public string? ActionCode { get; set; }
}

/// <summary>
/// PEG Item (순차 매칭 항목)
/// 예: a=expr, a[GeneratedStmt]=expr, expr, 'if'
/// </summary>
public class Item
{
    public string? Name { get; set; }  // a=expr의 'a' (선택적)
    public string? Type { get; set; }  // a[GeneratedStmt]=expr의 'GeneratedStmt' (선택적)
    public Atom Atom { get; set; } = null!;
}

/// <summary>
/// PEG Atom (원자 표현식 base class)
/// </summary>
public abstract class Atom { }

/// <summary>
/// 다른 규칙 참조 (예: expression, statement)
/// </summary>
public class RuleRef : Atom
{
    public string Name { get; set; } = "";
}

/// <summary>
/// 키워드 (예: 'if', "match")
/// CRITICAL: Quote type으로 HARD/SOFT 구분
/// </summary>
public class Keyword : Atom
{
    public string Value { get; set; } = "";

    /// <summary>
    /// true: SOFT KEYWORD (Double quote "match")
    /// false: HARD KEYWORD (Single quote 'if')
    /// </summary>
    public bool IsSoft { get; set; }
}

/// <summary>
/// 토큰 타입 (예: NAME, NUMBER, STRING)
/// </summary>
public class Token : Atom
{
    public string TokenType { get; set; } = "";
}

/// <summary>
/// 그룹 (a | b | c)
/// </summary>
public class Group : Atom
{
    public List<Alternative> Alternatives { get; set; } = new();
}

/// <summary>
/// Optional [expr]
/// </summary>
public class Optional : Atom
{
    public Atom Inner { get; set; } = null!;
}

/// <summary>
/// Zero or more expr*
/// </summary>
public class ZeroOrMore : Atom
{
    public Atom Inner { get; set; } = null!;
}

/// <summary>
/// One or more expr+
/// </summary>
public class OneOrMore : Atom
{
    public Atom Inner { get; set; } = null!;
}

/// <summary>
/// Positive lookahead &expr
/// </summary>
public class PositiveLookahead : Atom
{
    public Atom Inner { get; set; } = null!;
}

/// <summary>
/// Negative lookahead !expr
/// </summary>
public class NegativeLookahead : Atom
{
    public Atom Inner { get; set; } = null!;
}

/// <summary>
/// Gather pattern (sep.item+ or sep.item*)
/// 예: ','.expression+
/// </summary>
public class Gather : Atom
{
    public Atom Separator { get; set; } = null!;  // ','
    public Atom Item { get; set; } = null!;       // expression
    public bool IsPlus { get; set; }              // true: +, false: *
}

/// <summary>
/// @trailer code block
/// CPython: 파서 생성 후 추가되는 코드 (entry points 등)
/// </summary>
public class TrailerCode
{
    public string Code { get; set; } = "";
}
