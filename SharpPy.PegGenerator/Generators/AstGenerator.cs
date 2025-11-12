using System.Text;
using SharpPy.PegGenerator.DataStructures;

namespace SharpPy.PegGenerator.Generators;

/// <summary>
/// Python.asdl → AstTypes.cs 생성기
/// CPython 3.12 compatible AST type generation
/// </summary>
public class AstGenerator
{
    private readonly StringBuilder _sb = new();
    private int _indentLevel = 0;

    public string Generate(AsdlModule module)
    {
        _sb.Clear();
        _indentLevel = 0;

        // File header
        WriteLine("// Generated AST Types from Python.asdl");
        WriteLine("// CPython 3.12 compatible - Auto-generated, DO NOT EDIT");
        WriteLine();
        WriteLine("using System;");
        WriteLine("using System.Collections.Generic;");
        WriteLine();
        WriteLine("namespace SharpPy.Generated");
        WriteLine("{");
        _indentLevel++;

        // Base types
        GenerateBaseTypes();

        // Each ASDL type → C# classes
        foreach (var type in module.Types)
        {
            GenerateTypeClasses(type);
        }

        // Sequence types
        GenerateSequenceTypes(module);

        // Helper types
        GenerateHelperTypes();

        // Parser intermediate types
        GenerateParserIntermediateTypes();

        // Placeholder type for incomplete AST generation
        GeneratePlaceholderType();

        _indentLevel--;
        WriteLine("}");

        return _sb.ToString();
    }

    private void GenerateBaseTypes()
    {
        WriteLine("// ============================================================");
        WriteLine("// Base Types");
        WriteLine("// ============================================================");
        WriteLine();
        WriteLine("// Note: GeneratedPtr and GeneratedSeq are defined in Parser/Tokenizer.cs");
        WriteLine("// They are fundamental types used by tokenizer, parser, and AST");
        WriteLine();

        // GeneratedAstNode (AST-specific base class)
        WriteLine("/// <summary>");
        WriteLine("/// Base class for all AST nodes");
        WriteLine("/// CPython 3.12: All AST nodes have lineno, col_offset attributes");
        WriteLine("/// </summary>");
        WriteLine("public abstract class GeneratedAstNode : GeneratedPtr");
        WriteLine("{");
        _indentLevel++;
        WriteLine("public int LineNo { get; set; }");
        WriteLine("public int ColOffset { get; set; }");
        WriteLine("public int EndLineNo { get; set; }");
        WriteLine("public int EndColOffset { get; set; }");
        _indentLevel--;
        WriteLine("}");
        WriteLine();
    }

    private void GenerateTypeClasses(AsdlType type)
    {
        WriteLine("// ============================================================");
        WriteLine($"// {type.Name} types");
        WriteLine("// ============================================================");
        WriteLine();

        // Product type vs Sum type
        bool isProductType = type.Constructors.Count == 1 &&
                             type.Constructors[0].Name == type.Name;

        if (!isProductType)
        {
            // Sum type: Abstract base class
            WriteLine($"/// <summary>");
            WriteLine($"/// Base class for all {type.Name} nodes");
            WriteLine($"/// CPython 3.12: {type.Name}_ty");
            WriteLine($"/// </summary>");
            WriteLine($"public abstract class Generated{ToPascalCase(type.Name)} : GeneratedAstNode {{ }}");
            WriteLine();
        }

        // Concrete classes for each constructor
        foreach (var constructor in type.Constructors)
        {
            GenerateConstructorClass(type.Name, constructor, isProductType);
        }
    }

    private void GenerateConstructorClass(string typeName, AsdlConstructor constructor, bool isProductType)
    {
        var constructorName = ToPascalCase(constructor.Name);

        // Name collision handling
        if (typeName == "stmt" && constructorName == "Expr")
            constructorName = "ExprStmt";
        else if (typeName == "type_ignore" && constructorName == "TypeIgnore")
            constructorName = "TypeIgnoreNode";
        else if (typeName == "operator" && constructorName == "Mod")
            constructorName = "Mod_";

        var className = $"Generated{constructorName}";

        // Singleton pattern for no-field constructors (Pass, Break, Continue)
        if (constructor.Fields.Count == 0)
        {
            WriteLine($"/// <summary>");
            WriteLine($"/// {constructor.Name} - Singleton pattern (no fields)");
            WriteLine($"/// </summary>");
            var baseClass = isProductType ? "GeneratedAstNode" : $"Generated{ToPascalCase(typeName)}";
            WriteLine($"public class {className} : {baseClass}");
            WriteLine("{");
            _indentLevel++;
            WriteLine($"public static readonly {className} Instance = new();");
            WriteLine($"private {className}() {{ }}");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            return;
        }

        // Regular constructor with fields
        var baseClassRegular = isProductType ? "GeneratedAstNode" : $"Generated{ToPascalCase(typeName)}";
        WriteLine($"public class {className} : {baseClassRegular}");
        WriteLine("{");
        _indentLevel++;

        // Properties for each field
        foreach (var field in constructor.Fields)
        {
            var csharpType = MapAsdlTypeToCSharp(field);
            var propertyName = ToPascalCase(field.Name);
            var defaultValue = GetDefaultValue(csharpType);
            WriteLine($"public {csharpType} {propertyName}{defaultValue}");
        }

        _indentLevel--;
        WriteLine("}");
        WriteLine();
    }

    private void GenerateSequenceTypes(AsdlModule module)
    {
        WriteLine("// ============================================================");
        WriteLine("// Sequence Types (GC optimized)");
        WriteLine("// ============================================================");
        WriteLine();

        var typeNames = new HashSet<string>();

        foreach (var type in module.Types)
        {
            typeNames.Add(type.Name);

            foreach (var constructor in type.Constructors)
            {
                foreach (var field in constructor.Fields)
                {
                    if (field.IsSequence)
                    {
                        typeNames.Add(field.Type);
                    }
                }
            }
        }

        foreach (var typeName in typeNames.OrderBy(x => x))
        {
            var pascalName = ToPascalCase(typeName);
            var seqClassName = $"Generated{pascalName}Seq";
            var itemType = IsBuiltin(typeName) ? MapBuiltinType(typeName) : $"Generated{pascalName}";

            WriteLine($"/// <summary>");
            WriteLine($"/// Sequence of {typeName} - CPython: asdl_{typeName}_seq");
            WriteLine($"/// </summary>");
            WriteLine($"public class {seqClassName} : GeneratedSeq");
            WriteLine("{");
            _indentLevel++;
            WriteLine($"public static readonly {seqClassName} Empty = new();");
            WriteLine();
            WriteLine($"public {seqClassName}() {{ }}");
            WriteLine($"public {seqClassName}(int capacity) : base(capacity) {{ }}");
            WriteLine($"public {seqClassName}(IEnumerable<{itemType}> collection)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("foreach (var item in collection) Add(item);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine($"public new void Add({itemType} item) => base.Add(item);");
            WriteLine($"public new {itemType} this[int index]");
            WriteLine("{");
            _indentLevel++;
            WriteLine($"get => ({itemType})base[index];");
            WriteLine("set => base[index] = value;");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        // Additional sequence types needed by PyParserHelpers
        WriteLine("// ============================================================");
        WriteLine("// Additional Parser Helper Sequence Types");
        WriteLine("// ============================================================");
        WriteLine();

        // GeneratedAstNodeSeq - used for CmpopExprPair sequences
        WriteLine("/// <summary>");
        WriteLine("/// Sequence of mixed AST nodes - CPython: asdl_seq*");
        WriteLine("/// Used for intermediate parser results (CmpopExprPair, etc.)");
        WriteLine("/// </summary>");
        WriteLine("public class GeneratedAstNodeSeq : GeneratedSeq");
        WriteLine("{");
        _indentLevel++;
        WriteLine("public static readonly GeneratedAstNodeSeq Empty = new();");
        WriteLine();
        WriteLine("public GeneratedAstNodeSeq() { }");
        WriteLine("public GeneratedAstNodeSeq(int capacity) : base(capacity) { }");
        WriteLine("public GeneratedAstNodeSeq(IEnumerable<GeneratedPtr> collection)");
        WriteLine("{");
        _indentLevel++;
        WriteLine("foreach (var item in collection) Add(item);");
        _indentLevel--;
        WriteLine("}");
        _indentLevel--;
        WriteLine("}");
        WriteLine();

        // GeneratedKeywordOrStarredSeq - used for call arguments
        WriteLine("/// <summary>");
        WriteLine("/// Sequence of KeywordOrStarred - CPython: asdl_seq*");
        WriteLine("/// Used for parsing function call arguments");
        WriteLine("/// </summary>");
        WriteLine("public class GeneratedKeywordOrStarredSeq : GeneratedSeq");
        WriteLine("{");
        _indentLevel++;
        WriteLine("public static readonly GeneratedKeywordOrStarredSeq Empty = new();");
        WriteLine();
        WriteLine("public GeneratedKeywordOrStarredSeq() { }");
        WriteLine("public GeneratedKeywordOrStarredSeq(int capacity) : base(capacity) { }");
        WriteLine("public GeneratedKeywordOrStarredSeq(IEnumerable<GeneratedKeywordOrStarred> collection)");
        WriteLine("{");
        _indentLevel++;
        WriteLine("foreach (var item in collection) Add(item);");
        _indentLevel--;
        WriteLine("}");
        WriteLine();
        WriteLine("public new void Add(GeneratedKeywordOrStarred item) => base.Add(item);");
        WriteLine("public new GeneratedKeywordOrStarred this[int index]");
        WriteLine("{");
        _indentLevel++;
        WriteLine("get => (GeneratedKeywordOrStarred)base[index];");
        WriteLine("set => base[index] = value;");
        _indentLevel--;
        WriteLine("}");
        _indentLevel--;
        WriteLine("}");
        WriteLine();
    }

    private void GenerateHelperTypes()
    {
        WriteLine("// ============================================================");
        WriteLine("// Helper Types");
        WriteLine("// ============================================================");
        WriteLine();

        // GeneratedIdentifier
        WriteLine("/// <summary>");
        WriteLine("/// Wrapper for Python identifier (ASDL builtin)");
        WriteLine("/// </summary>");
        WriteLine("public class GeneratedIdentifier : GeneratedPtr");
        WriteLine("{");
        _indentLevel++;
        WriteLine("public string Value { get; set; }");
        WriteLine();
        WriteLine("public GeneratedIdentifier(string value) { Value = value; }");
        WriteLine();
        WriteLine("public static implicit operator string(GeneratedIdentifier id) => id?.Value;");
        WriteLine("public static implicit operator GeneratedIdentifier(string value) => new GeneratedIdentifier(value);");
        WriteLine();
        WriteLine("public override string ToString() => Value;");
        _indentLevel--;
        WriteLine("}");
        WriteLine();
    }

    private void GenerateParserIntermediateTypes()
    {
        WriteLine("// ============================================================");
        WriteLine("// Parser Intermediate Types");
        WriteLine("// ============================================================");
        WriteLine();

        // SlashWithDefault
        WriteLine("public class GeneratedSlashWithDefault : GeneratedPtr");
        WriteLine("{");
        _indentLevel++;
        WriteLine("public GeneratedArgSeq Args { get; set; } = new();");
        WriteLine("public GeneratedExprSeq Defaults { get; set; } = new();");
        _indentLevel--;
        WriteLine("}");
        WriteLine();

        // KeyValuePair
        WriteLine("public class GeneratedKeyValuePair : GeneratedAstNode");
        WriteLine("{");
        _indentLevel++;
        WriteLine("public GeneratedExpr Key { get; set; } = null!;");
        WriteLine("public GeneratedExpr Value { get; set; } = null!;");
        _indentLevel--;
        WriteLine("}");
        WriteLine();

        // PyConstant types
        WriteLine("public abstract class GeneratedPyConstant");
        WriteLine("{");
        _indentLevel++;
        WriteLine("public static readonly GeneratedPyConstantNone None = new();");
        WriteLine("public static readonly GeneratedPyConstantBool True = new(true);");
        WriteLine("public static readonly GeneratedPyConstantBool False = new(false);");
        WriteLine("public static readonly GeneratedPyConstantEllipsis Ellipsis = new();");
        _indentLevel--;
        WriteLine("}");
        WriteLine();

        var constantTypes = new[] { "None", "Bool", "Int", "Float", "String", "Bytes", "Ellipsis" };
        foreach (var ct in constantTypes)
        {
            WriteLine($"public class GeneratedPyConstant{ct} : GeneratedPyConstant");
            WriteLine("{");
            _indentLevel++;

            if (ct == "None" || ct == "Ellipsis")
            {
                WriteLine($"internal GeneratedPyConstant{ct}() {{ }}");
            }
            else if (ct == "Bool")
            {
                WriteLine("public bool Value { get; }");
                WriteLine("internal GeneratedPyConstantBool(bool value) => Value = value;");
            }
            else if (ct == "Int")
            {
                WriteLine("public long Value { get; }");
                WriteLine("public GeneratedPyConstantInt(long value) => Value = value;");
            }
            else if (ct == "Float")
            {
                WriteLine("public double Value { get; }");
                WriteLine("public GeneratedPyConstantFloat(double value) => Value = value;");
            }
            else if (ct == "String")
            {
                WriteLine("public string Value { get; }");
                WriteLine("public GeneratedPyConstantString(string value) => Value = value;");
            }
            else if (ct == "Bytes")
            {
                WriteLine("public byte[] Value { get; }");
                WriteLine("public GeneratedPyConstantBytes(byte[] value) => Value = value;");
            }

            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        // StarEtc
        WriteLine("public class GeneratedStarEtc : GeneratedPtr");
        WriteLine("{");
        _indentLevel++;
        WriteLine("public GeneratedArg? Vararg { get; set; }");
        WriteLine("public GeneratedArgSeq Kwonlyargs { get; set; } = new();");
        WriteLine("public GeneratedExprSeq KwDefaults { get; set; } = new();");
        WriteLine("public GeneratedArg? Kwarg { get; set; }");
        _indentLevel--;
        WriteLine("}");
        WriteLine();

        // KeywordOrStarred
        WriteLine("public class GeneratedKeywordOrStarred : GeneratedPtr");
        WriteLine("{");
        _indentLevel++;
        WriteLine("public GeneratedKeyword? Keyword { get; set; }");
        WriteLine("public GeneratedExpr? Starred { get; set; }");
        _indentLevel--;
        WriteLine("}");
        WriteLine();
    }

    private void GeneratePlaceholderType()
    {
        WriteLine("// ============================================================");
        WriteLine("// Placeholder Type for incomplete AST generation");
        WriteLine("// ============================================================");
        WriteLine();
        WriteLine("/// <summary>");
        WriteLine("/// Placeholder for rules that don't yet generate proper AST nodes");
        WriteLine("/// TODO: Replace with actual AST node generation");
        WriteLine("/// </summary>");
        WriteLine("public class GeneratedPlaceholder : GeneratedPtr");
        WriteLine("{");
        _indentLevel++;
        WriteLine("public static readonly GeneratedPlaceholder Instance = new();");
        WriteLine("private GeneratedPlaceholder() { }");
        _indentLevel--;
        WriteLine("}");
        WriteLine();
    }

    private string MapAsdlTypeToCSharp(AsdlField field)
    {
        var baseType = IsBuiltin(field.Type) ? MapBuiltinType(field.Type) : $"Generated{ToPascalCase(field.Type)}";

        if (field.IsSequence)
            return $"Generated{ToPascalCase(field.Type)}Seq";
        else if (field.IsOptional)
            return $"{baseType}?";
        else
            return baseType;
    }

    private bool IsBuiltin(string type)
    {
        return type == "identifier" || type == "int" || type == "string" || type == "constant" || type == "singleton";
    }

    private string MapBuiltinType(string asdlType)
    {
        return asdlType switch
        {
            "identifier" => "GeneratedIdentifier",
            "int" => "int",
            "string" => "string",
            "constant" => "GeneratedPyConstant",
            "singleton" => "bool?",
            _ => "GeneratedPtr"
        };
    }

    private string GetDefaultValue(string csharpType)
    {
        if (csharpType.Contains("Seq"))
            return " { get; set; }";
        if (csharpType.EndsWith("?"))
            return " { get; set; }";
        if (csharpType == "int")
            return " { get; set; } = 0;";
        if (csharpType == "string")
            return " { get; set; } = \"\";";
        return " { get; set; } = null!;";
    }

    private string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var parts = name.Split('_');
        return string.Join("", parts.Select(p =>
            p.Length > 0 ? char.ToUpper(p[0]) + p.Substring(1) : ""
        ));
    }

    private void WriteLine(string line = "")
    {
        if (string.IsNullOrEmpty(line))
        {
            _sb.AppendLine();
        }
        else
        {
            _sb.AppendLine(new string(' ', _indentLevel * 4) + line);
        }
    }
}
