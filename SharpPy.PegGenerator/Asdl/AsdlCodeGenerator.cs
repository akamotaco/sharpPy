// ASDL → C# AST 타입 생성기
// CPython Python-ast.c 등가 (C# 버전)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpPy.PegGenerator.Asdl
{
    public class AsdlCodeGenerator
    {
        private readonly StringBuilder _sb = new();
        private int _indentLevel = 0;

        public string GenerateAstTypes(AsdlModule module)
        {
            _sb.Clear();
            _indentLevel = 0;

            // 파일 헤더
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

            // 각 ASDL 타입에 대한 C# 클래스 생성
            foreach (var type in module.Types)
            {
                GenerateTypeClasses(type);
            }

            // Sequence types (GC 최적화)
            GenerateSequenceTypes(module);

            // Helper types (arguments, keyword, etc.)
            GenerateHelperTypes(module);

            // Parser intermediate types (CPython: pegen.c에서 사용하는 임시 타입들)
            GenerateParserIntermediateTypes();

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

            // GeneratedPtr is defined in Generated/GeneratedPtr.cs (shared across all generators)
            WriteLine("// GeneratedPtr is defined in GeneratedPtr.cs");
            WriteLine();

            // GeneratedAstNode (AST 노드 베이스)
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

            // CPython 3.12: Product types (단일 constructor) vs Sum types (여러 constructors)
            // Product type: comprehension = (...) → concrete class만 생성
            // Sum type: stmt = A | B | C → abstract base + concrete classes 생성
            bool isProductType = type.Constructors.Count == 1 &&
                                 type.Constructors[0].Name == type.Name;

            if (!isProductType)
            {
                // Sum type: Abstract base class 생성
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

            // CPython 3.12: 이름 충돌 처리
            // stmt.Expr vs expr base type → ExprStmt
            // type_ignore.TypeIgnore vs type_ignore base → TypeIgnoreNode
            // operator.Mod vs mod base type → ModOp
            if (typeName == "stmt" && constructorName == "Expr")
            {
                constructorName = "ExprStmt";
            }
            else if (typeName == "type_ignore" && constructorName == "TypeIgnore")
            {
                constructorName = "TypeIgnoreNode";
            }
            else if (typeName == "operator" && constructorName == "Mod")
            {
                constructorName = "ModOp";
            }

            var className = $"Generated{constructorName}";

            // 단순 생성자 (Pass, Break, Continue)는 singleton
            if (constructor.Fields.Count == 0)
            {
                WriteLine($"/// <summary>");
                WriteLine($"/// {constructor.Name} - Singleton pattern (no fields)");
                WriteLine($"/// </summary>");
                WriteLine($"public class {className} : Generated{ToPascalCase(typeName)}");
                WriteLine("{");
                _indentLevel++;
                WriteLine($"public static readonly {className} Instance = new();");
                WriteLine($"private {className}() {{ }}");
                _indentLevel--;
                WriteLine("}");
                WriteLine();
                return;
            }

            // 필드가 있는 생성자
            // CPython 3.12: Product type은 base class 없이 직접 GeneratedAstNode 상속
            var baseClass = isProductType ? "GeneratedAstNode" : $"Generated{ToPascalCase(typeName)}";
            WriteLine($"public class {className} : {baseClass}");
            WriteLine("{");
            _indentLevel++;

            // 각 필드를 프로퍼티로 생성
            foreach (var field in constructor.Fields)
            {
                var csharpType = MapAsdlTypeToCSharp(field.Type, field.Cardinality);
                var propertyName = ToPascalCase(field.Name);
                var propDeclaration = GetDefaultValue(csharpType);

                // GetDefaultValue returns the complete property declaration including { get; set; } and optional initializer
                WriteLine($"public {csharpType} {propertyName}{propDeclaration}");
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

            // 모든 타입에 대한 시퀀스 생성
            var typeNames = new HashSet<string>();

            foreach (var type in module.Types)
            {
                typeNames.Add(type.Name);

                foreach (var constructor in type.Constructors)
                {
                    foreach (var field in constructor.Fields)
                    {
                        if (field.Cardinality == FieldCardinality.Sequence)
                        {
                            typeNames.Add(field.Type);
                        }
                    }
                }
            }

            // Base class for all sequence types - CPython void* compatibility
            WriteLine("/// <summary>");
            WriteLine("/// Base class for all sequence types");
            WriteLine("/// CPython 3.12: All asdl_seq types inherit from this for void* compatibility");
            WriteLine("/// </summary>");
            WriteLine("public abstract class GeneratedSeq : GeneratedPtr { }");
            WriteLine();

            foreach (var typeName in typeNames.OrderBy(x => x))
            {
                var pascalName = ToPascalCase(typeName);
                var seqClassName = $"Generated{pascalName}Seq";
                var itemType = AsdlBuiltins.IsBuiltin(typeName)
                    ? MapBuiltinType(typeName)
                    : $"Generated{pascalName}";

                WriteLine($"/// <summary>");
                WriteLine($"/// Sequence of {typeName} - CPython: asdl_{typeName}_seq");
                WriteLine($"/// C# GC optimized: List<T> with GeneratedSeq inheritance");
                WriteLine($"/// </summary>");
                WriteLine($"public class {seqClassName} : GeneratedSeq, System.Collections.Generic.IList<{itemType}>");
                WriteLine("{");
                _indentLevel++;
                WriteLine($"private readonly List<{itemType}> _items = new();");
                WriteLine($"public static readonly {seqClassName} Empty = new();");
                WriteLine();
                WriteLine($"public {seqClassName}() {{ }}");
                WriteLine($"public {seqClassName}(int capacity) {{ _items = new List<{itemType}>(capacity); }}");
                WriteLine($"public {seqClassName}(IEnumerable<{itemType}> collection) {{ _items = new List<{itemType}>(collection); }}");
                WriteLine();
                WriteLine($"// IList<T> implementation");
                WriteLine($"public {itemType} this[int index] {{ get => _items[index]; set => _items[index] = value; }}");
                WriteLine($"public int Count => _items.Count;");
                WriteLine($"public bool IsReadOnly => false;");
                WriteLine($"public void Add({itemType} item) => _items.Add(item);");
                WriteLine($"public void Clear() => _items.Clear();");
                WriteLine($"public bool Contains({itemType} item) => _items.Contains(item);");
                WriteLine($"public void CopyTo({itemType}[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);");
                WriteLine($"public System.Collections.Generic.IEnumerator<{itemType}> GetEnumerator() => _items.GetEnumerator();");
                WriteLine($"System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();");
                WriteLine($"public int IndexOf({itemType} item) => _items.IndexOf(item);");
                WriteLine($"public void Insert(int index, {itemType} item) => _items.Insert(index, item);");
                WriteLine($"public bool Remove({itemType} item) => _items.Remove(item);");
                WriteLine($"public void RemoveAt(int index) => _items.RemoveAt(index);");
                WriteLine();
                WriteLine($"// Additional List<T> methods for compatibility");
                WriteLine($"public void AddRange(System.Collections.Generic.IEnumerable<{itemType}> collection) => _items.AddRange(collection);");
                _indentLevel--;
                WriteLine("}");
                WriteLine();
            }
        }

        private void GenerateHelperTypes(AsdlModule module)
        {
            WriteLine("// ============================================================");
            WriteLine("// Helper Types");
            WriteLine("// ============================================================");
            WriteLine();

            // arguments, keyword, comprehension, alias, withitem 등은
            // ASDL에서 별도로 정의되어 있음
            // 여기서는 추가 헬퍼 타입만 정의

            // GeneratedTokenInfo는 이미 PyTokenizer.cs에 있음
            WriteLine("// GeneratedTokenInfo is defined in PyTokenizer.cs");
            WriteLine();
        }

        private void GenerateParserIntermediateTypes()
        {
            WriteLine("// ============================================================");
            WriteLine("// Parser Intermediate Types");
            WriteLine("// CPython 3.12: pegen.c에서 사용하는 임시 타입들");
            WriteLine("// ============================================================");
            WriteLine();

            // SlashWithDefault - arguments 파싱용
            WriteLine("/// <summary>");
            WriteLine("/// Intermediate type for slash_with_default rule in python.gram");
            WriteLine("/// </summary>");
            WriteLine("public class GeneratedSlashWithDefault");
            WriteLine("{");
            _indentLevel++;
            WriteLine("public GeneratedArgSeq Args { get; set; } = new();");
            WriteLine("public GeneratedExprSeq Defaults { get; set; } = new();");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // StarEtc - arguments 파싱용
            WriteLine("/// <summary>");
            WriteLine("/// Intermediate type for star_etc rule in python.gram");
            WriteLine("/// </summary>");
            WriteLine("public class GeneratedStarEtc");
            WriteLine("{");
            _indentLevel++;
            WriteLine("public GeneratedArg? Vararg { get; set; }");
            WriteLine("public GeneratedArgSeq Kwonlyargs { get; set; } = new();");
            WriteLine("public GeneratedExprSeq KwDefaults { get; set; } = new();");
            WriteLine("public GeneratedArg? Kwarg { get; set; }");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // KeywordOrStarred - call arguments 파싱용
            WriteLine("/// <summary>");
            WriteLine("/// Intermediate type for keyword_or_starred rule in python.gram");
            WriteLine("/// </summary>");
            WriteLine("public class GeneratedKeywordOrStarred");
            WriteLine("{");
            _indentLevel++;
            WriteLine("public GeneratedKeyword? Keyword { get; set; }");
            WriteLine("public GeneratedExpr? Starred { get; set; }");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Generic Seq wrapper - List<T> wrapper
            WriteLine("/// <summary>");
            WriteLine("/// Generic sequence wrapper for parser intermediate results");
            WriteLine("/// </summary>");
            WriteLine("public class GeneratedSeq<T> : List<T>");
            WriteLine("{");
            _indentLevel++;
            WriteLine("public GeneratedSeq() { }");
            WriteLine("public GeneratedSeq(int capacity) : base(capacity) { }");
            WriteLine("public GeneratedSeq(IEnumerable<T> collection) : base(collection) { }");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // GeneratedMixedSeq (non-generic) - GeneratedPtr list
            WriteLine("/// <summary>");
            WriteLine("/// Non-generic sequence for mixed types - parser intermediate");
            WriteLine("/// CPython 3.12: asdl_seq* equivalent - no boxing/unboxing");
            WriteLine("/// </summary>");
            WriteLine("public class GeneratedMixedSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedPtr>");
            WriteLine("{");
            _indentLevel++;
            WriteLine("private readonly List<GeneratedPtr> _items = new();");
            WriteLine();
            WriteLine("public GeneratedMixedSeq() { }");
            WriteLine("public GeneratedMixedSeq(int capacity) { _items = new List<GeneratedPtr>(capacity); }");
            WriteLine("public GeneratedMixedSeq(IEnumerable<GeneratedPtr> collection) { _items = new List<GeneratedPtr>(collection); }");
            WriteLine();
            WriteLine("// IList<GeneratedPtr> implementation");
            WriteLine("public GeneratedPtr this[int index] { get => _items[index]; set => _items[index] = value; }");
            WriteLine("public int Count => _items.Count;");
            WriteLine("public bool IsReadOnly => false;");
            WriteLine("public void Add(GeneratedPtr item) => _items.Add(item);");
            WriteLine("public void Clear() => _items.Clear();");
            WriteLine("public bool Contains(GeneratedPtr item) => _items.Contains(item);");
            WriteLine("public void CopyTo(GeneratedPtr[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);");
            WriteLine("public System.Collections.Generic.IEnumerator<GeneratedPtr> GetEnumerator() => _items.GetEnumerator();");
            WriteLine("System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();");
            WriteLine("public int IndexOf(GeneratedPtr item) => _items.IndexOf(item);");
            WriteLine("public void Insert(int index, GeneratedPtr item) => _items.Insert(index, item);");
            WriteLine("public bool Remove(GeneratedPtr item) => _items.Remove(item);");
            WriteLine("public void RemoveAt(int index) => _items.RemoveAt(index);");
            WriteLine();
            WriteLine("// Additional List<T> methods for compatibility");
            WriteLine("public void AddRange(System.Collections.Generic.IEnumerable<GeneratedPtr> collection) => _items.AddRange(collection);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // GeneratedAstNodeSeq - AST node list
            WriteLine("/// <summary>");
            WriteLine("/// Sequence of GeneratedAstNode for mixed AST types");
            WriteLine("/// CPython 3.12: Used for sequences that can contain any AST node type");
            WriteLine("/// </summary>");
            WriteLine("public class GeneratedAstNodeSeq : GeneratedSeq, System.Collections.Generic.IList<GeneratedAstNode>");
            WriteLine("{");
            _indentLevel++;
            WriteLine("private readonly List<GeneratedAstNode> _items = new();");
            WriteLine("public static readonly GeneratedAstNodeSeq Empty = new();");
            WriteLine();
            WriteLine("public GeneratedAstNodeSeq() { }");
            WriteLine("public GeneratedAstNodeSeq(int capacity) { _items = new List<GeneratedAstNode>(capacity); }");
            WriteLine("public GeneratedAstNodeSeq(IEnumerable<GeneratedAstNode> collection) { _items = new List<GeneratedAstNode>(collection); }");
            WriteLine();
            WriteLine("// IList<GeneratedAstNode> implementation");
            WriteLine("public GeneratedAstNode this[int index] { get => _items[index]; set => _items[index] = value; }");
            WriteLine("public int Count => _items.Count;");
            WriteLine("public bool IsReadOnly => false;");
            WriteLine("public void Add(GeneratedAstNode item) => _items.Add(item);");
            WriteLine("public void Clear() => _items.Clear();");
            WriteLine("public bool Contains(GeneratedAstNode item) => _items.Contains(item);");
            WriteLine("public void CopyTo(GeneratedAstNode[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);");
            WriteLine("public System.Collections.Generic.IEnumerator<GeneratedAstNode> GetEnumerator() => _items.GetEnumerator();");
            WriteLine("System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();");
            WriteLine("public int IndexOf(GeneratedAstNode item) => _items.IndexOf(item);");
            WriteLine("public void Insert(int index, GeneratedAstNode item) => _items.Insert(index, item);");
            WriteLine("public bool Remove(GeneratedAstNode item) => _items.Remove(item);");
            WriteLine("public void RemoveAt(int index) => _items.RemoveAt(index);");
            WriteLine();
            WriteLine("// Additional List<T> methods for compatibility");
            WriteLine("public void AddRange(System.Collections.Generic.IEnumerable<GeneratedAstNode> collection) => _items.AddRange(collection);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        private string MapAsdlTypeToCSharp(string asdlType, FieldCardinality cardinality)
        {
            var baseType = AsdlBuiltins.IsBuiltin(asdlType)
                ? MapBuiltinType(asdlType)
                : $"Generated{ToPascalCase(asdlType)}";

            return cardinality switch
            {
                FieldCardinality.Optional => $"{baseType}?",
                FieldCardinality.Sequence => $"Generated{ToPascalCase(asdlType)}Seq",
                _ => baseType
            };
        }

        private string MapBuiltinType(string asdlType)
        {
            return asdlType switch
            {
                "identifier" => "string",
                "int" => "int",
                "string" => "string",
                "constant" => "object",
                "singleton" => "bool?",
                _ => "object"
            };
        }

        private string GetDefaultValue(string csharpType)
        {
            // Seq 타입은 초기화 불필요 (List<T>는 자동으로 null) - nullable reference
            if (csharpType.Contains("Seq"))
            {
                return " { get; set; }";
            }
            // Optional 타입 (nullable) - 초기화 불필요
            if (csharpType.EndsWith("?"))
            {
                return " { get; set; }";
            }
            // int 타입 - non-nullable이므로 초기화 필요
            if (csharpType == "int")
            {
                return " { get; set; } = 0;";
            }
            // string 타입 - non-nullable이므로 초기화 필요
            if (csharpType == "string")
            {
                return " { get; set; } = \"\";";
            }
            // AST 노드 타입 (non-nullable reference) - 초기화 필요
            return " { get; set; } = null!;";
        }

        private string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            // CPython 3.12: snake_case → PascalCase
            // 예: function_def → FunctionDef, comprehension → Comprehension
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
}
