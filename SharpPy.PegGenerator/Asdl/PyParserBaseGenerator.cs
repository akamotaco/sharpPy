// PyParserBase.cs 코드 생성기
// CPython 3.12의 pegen.c + Python-ast.c 등가 (C# 버전)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpPy.PegGenerator.Asdl
{
    public class PyParserBaseGenerator
    {
        private readonly StringBuilder _sb = new();
        private int _indentLevel = 0;

        public string GenerateParserBase(AsdlModule module)
        {
            _sb.Clear();
            _indentLevel = 0;

            // 파일 헤더
            WriteLine("// Generated PyParserBase from Python.asdl");
            WriteLine("// CPython 3.12 compatible - Auto-generated, DO NOT EDIT");
            WriteLine();
            WriteLine("using System;");
            WriteLine("using System.Collections.Generic;");
            WriteLine("using System.Linq;");
            WriteLine("using SharpPy.Tokenizer;");
            WriteLine();
            WriteLine("namespace SharpPy.Generated");
            WriteLine("{");
            _indentLevel++;

            // PyParserBase abstract class
            GenerateParserBaseClass();

            // _PyAST_* helper functions for each constructor
            GenerateAstHelpers(module);

            // _PyPegen_* helper functions
            GeneratePegenHelpers(module);

            // ASTHelpers static class
            GenerateAstHelpersClass();

            _indentLevel--;
            WriteLine("}");

            return _sb.ToString();
        }

        private void GenerateParserBaseClass()
        {
            WriteLine("// ============================================================");
            WriteLine("// PyParserBase - Base parser class");
            WriteLine("// ============================================================");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// Base class for generated parser - provides common parsing logic");
            WriteLine("/// CPython 3.12: Parser/pegen.c equivalent");
            WriteLine("/// </summary>");
            WriteLine("public abstract class PyParserBase<TResult>");
            WriteLine("{");
            _indentLevel++;

            // Fields
            WriteLine("protected List<GeneratedTokenInfo> _tokens;");
            WriteLine("protected int _position = 0;");
            WriteLine("protected string _filename;");
            WriteLine("protected Exception? _pendingSyntaxError = null;");
            WriteLine();

            // Constructor
            WriteLine("protected PyParserBase(List<GeneratedTokenInfo> tokens, string filename)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_tokens = tokens;");
            WriteLine("_filename = filename;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Current token property
            WriteLine("protected GeneratedTokenInfo? CurrentToken");
            WriteLine("{");
            _indentLevel++;
            WriteLine("get => _position < _tokens.Count ? _tokens[_position] : null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Abstract Parse method
            WriteLine("public abstract TResult Parse();");
            WriteLine();

            // ParseFile method (default implementation)
            WriteLine("protected virtual GeneratedModule ParseFile()");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Override in generated parser");
            WriteLine("throw new NotImplementedException(\"ParseFile must be overridden\");");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // ExpectToken method
            WriteLine("protected GeneratedTokenInfo? ExpectToken(GeneratedTokenType type)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var token = CurrentToken;");
            WriteLine("if (token != null && token.Type == type)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position++;");
            WriteLine("return token;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Expect method (with value)
            WriteLine("protected GeneratedTokenInfo? Expect(GeneratedTokenType type, string value)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var token = CurrentToken;");
            WriteLine("if (token != null && token.Type == type && token.Value == value)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position++;");
            WriteLine("return token;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return null;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        private void GenerateAstHelpers(AsdlModule module)
        {
            WriteLine("// ============================================================");
            WriteLine("// _PyAST_* Helper Functions");
            WriteLine("// ============================================================");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// AST node factory functions - CPython 3.12: Python-ast.c");
            WriteLine("/// </summary>");
            WriteLine("public static partial class AstFactory");
            WriteLine("{");
            _indentLevel++;

            foreach (var type in module.Types)
            {
                foreach (var constructor in type.Constructors)
                {
                    GenerateAstFactoryMethod(type.Name, constructor, type.Attributes);
                }
            }

            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        private void GenerateAstFactoryMethod(string typeName, AsdlConstructor constructor, AsdlAttributes? attributes)
        {
            var constructorName = ToPascalCase(constructor.Name);

            // CPython 3.12: 이름 충돌 처리 (AsdlCodeGenerator와 동일)
            if (typeName == "stmt" && constructorName == "Expr")
            {
                constructorName = "ExprStmt";
            }
            else if (typeName == "type_ignore" && constructorName == "TypeIgnore")
            {
                constructorName = "TypeIgnoreNode";
            }

            var className = $"Generated{constructorName}";
            var returnType = $"Generated{ToPascalCase(typeName)}";

            // Singleton 타입 (Pass, Break, Continue)
            if (constructor.Fields.Count == 0)
            {
                WriteLine($"public static {returnType} _PyAST_{constructor.Name}(int lineno, int col_offset, int? end_lineno, int? end_col_offset)");
                WriteLine("{");
                _indentLevel++;
                WriteLine($"var node = {className}.Instance;");
                WriteLine("node.LineNo = lineno;");
                WriteLine("node.ColOffset = col_offset;");
                WriteLine("node.EndLineNo = end_lineno ?? 0;");
                WriteLine("node.EndColOffset = end_col_offset ?? 0;");
                WriteLine("return node;");
                _indentLevel--;
                WriteLine("}");
                WriteLine();
                return;
            }

            // 필드가 있는 타입
            var parameters = new List<string>();
            foreach (var field in constructor.Fields)
            {
                var csharpType = MapAsdlTypeToCSharp(field.Type, field.Cardinality);
                var paramName = field.Name;
                parameters.Add($"{csharpType} {paramName}");
            }

            // EXTRA parameters (lineno, col_offset, etc.) - only if attributes exist
            // CPython 3.12: type_ignore has no attributes, so don't add position params
            if (attributes != null && attributes.Fields.Count > 0)
            {
                parameters.Add("int lineno");
                parameters.Add("int col_offset");
                parameters.Add("int? end_lineno");
                parameters.Add("int? end_col_offset");
            }

            WriteLine($"public static {returnType} _PyAST_{constructor.Name}({string.Join(", ", parameters)})");
            WriteLine("{");
            _indentLevel++;
            WriteLine($"var node = new {className}();");

            // Set fields
            foreach (var field in constructor.Fields)
            {
                var propName = ToPascalCase(field.Name);
                WriteLine($"node.{propName} = {field.Name};");
            }

            // Set location attributes (only if attributes exist)
            if (attributes != null && attributes.Fields.Count > 0)
            {
                WriteLine("node.LineNo = lineno;");
                WriteLine("node.ColOffset = col_offset;");
                WriteLine("node.EndLineNo = end_lineno ?? 0;");
                WriteLine("node.EndColOffset = end_col_offset ?? 0;");
            }
            WriteLine("return node;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Overload without optional parameters (for convenience)
            if (constructor.Fields.Any(f => f.Cardinality == FieldCardinality.Optional))
            {
                var requiredParams = new List<string>();
                foreach (var field in constructor.Fields.Where(f => f.Cardinality != FieldCardinality.Optional))
                {
                    var csharpType = MapAsdlTypeToCSharp(field.Type, field.Cardinality);
                    requiredParams.Add($"{csharpType} {field.Name}");
                }

                requiredParams.Add("int lineno");
                requiredParams.Add("int col_offset");
                requiredParams.Add("int? end_lineno");
                requiredParams.Add("int? end_col_offset");

                WriteLine($"public static {returnType} _PyAST_{constructor.Name}({string.Join(", ", requiredParams)})");
                WriteLine("{");
                _indentLevel++;

                var allParams = new List<string>();
                foreach (var field in constructor.Fields)
                {
                    if (field.Cardinality == FieldCardinality.Optional)
                    {
                        allParams.Add("null");
                    }
                    else
                    {
                        allParams.Add(field.Name);
                    }
                }
                allParams.Add("lineno");
                allParams.Add("col_offset");
                allParams.Add("end_lineno");
                allParams.Add("end_col_offset");

                WriteLine($"return _PyAST_{constructor.Name}({string.Join(", ", allParams)});");
                _indentLevel--;
                WriteLine("}");
                WriteLine();
            }
        }

        private void GeneratePegenHelpers(AsdlModule module)
        {
            WriteLine("// ============================================================");
            WriteLine("// _PyPegen_* Helper Functions");
            WriteLine("// ============================================================");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// PEG parser helper functions - CPython 3.12: Parser/pegen.c");
            WriteLine("/// </summary>");
            WriteLine("public static partial class PegenHelpers");
            WriteLine("{");
            _indentLevel++;

            // seq_flatten
            WriteLine("// CPython: _PyPegen_seq_flatten");
            WriteLine("public static GeneratedStmtSeq _PyPegen_seq_flatten(GeneratedStmtSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Already flat in C# - just return the sequence");
            WriteLine("return seq ?? GeneratedStmtSeq.Empty;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // singleton_seq
            WriteLine("// CPython: _PyPegen_singleton_seq");
            WriteLine("public static GeneratedStmtSeq _PyPegen_singleton_seq(GeneratedStmt item)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var seq = new GeneratedStmtSeq(1);");
            WriteLine("seq.Add(item);");
            WriteLine("return seq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedExprSeq _PyPegen_singleton_seq(GeneratedExpr item)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var seq = new GeneratedExprSeq(1);");
            WriteLine("seq.Add(item);");
            WriteLine("return seq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedAliasSeq _PyPegen_singleton_seq(GeneratedAlias item)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var seq = new GeneratedAliasSeq(1);");
            WriteLine("seq.Add(item);");
            WriteLine("return seq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // seq_insert_in_front
            WriteLine("// CPython: _PyPegen_seq_insert_in_front");
            WriteLine("public static GeneratedExprSeq _PyPegen_seq_insert_in_front(GeneratedExpr item, GeneratedExprSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var newSeq = new GeneratedExprSeq(seq.Count + 1);");
            WriteLine("newSeq.Add(item);");
            WriteLine("newSeq.AddRange(seq);");
            WriteLine("return newSeq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedPatternSeq _PyPegen_seq_insert_in_front(GeneratedPattern item, GeneratedPatternSeq seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var newSeq = new GeneratedPatternSeq(seq.Count + 1);");
            WriteLine("newSeq.Add(item);");
            WriteLine("newSeq.AddRange(seq);");
            WriteLine("return newSeq;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // seq_count_dots
            WriteLine("// CPython: _PyPegen_seq_count_dots");
            WriteLine("public static int _PyPegen_seq_count_dots(GeneratedIdentifierSeq? seq)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return seq?.Count ?? 0;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // map_names_to_ids
            WriteLine("// CPython: _PyPegen_map_names_to_ids");
            WriteLine("public static GeneratedIdentifierSeq _PyPegen_map_names_to_ids(GeneratedExprSeq names)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var ids = new GeneratedIdentifierSeq(names.Count);");
            WriteLine("foreach (var name in names)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (name is GeneratedName nameExpr)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("ids.Add(nameExpr.Id);");
            _indentLevel--;
            WriteLine("}");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return ids;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // alias_for_star
            WriteLine("// CPython: _PyPegen_alias_for_star");
            WriteLine("public static GeneratedAlias _PyPegen_alias_for_star(int lineno, int col_offset, int? end_lineno, int? end_col_offset)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var alias = new GeneratedAlias();");
            WriteLine("alias.Name = \"*\";");
            WriteLine("alias.Asname = null;");
            WriteLine("alias.LineNo = lineno;");
            WriteLine("alias.ColOffset = col_offset;");
            WriteLine("alias.EndLineNo = end_lineno ?? 0;");
            WriteLine("alias.EndColOffset = end_col_offset ?? 0;");
            WriteLine("return alias;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // empty_arguments
            WriteLine("// CPython: _PyPegen_empty_arguments");
            WriteLine("public static GeneratedArguments _PyPegen_empty_arguments()");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var args = new GeneratedArguments();");
            WriteLine("args.Posonlyargs = GeneratedArgSeq.Empty;");
            WriteLine("args.Args = GeneratedArgSeq.Empty;");
            WriteLine("args.Kwonlyargs = GeneratedArgSeq.Empty;");
            WriteLine("args.KwDefaults = GeneratedExprSeq.Empty;");
            WriteLine("args.Defaults = GeneratedExprSeq.Empty;");
            WriteLine("return args;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // set_expr_context
            WriteLine("// CPython: _PyPegen_set_expr_context");
            WriteLine("public static GeneratedExpr _PyPegen_set_expr_context(GeneratedExpr expr, GeneratedExprContext ctx)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Update expr context based on type");
            WriteLine("switch (expr)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("case GeneratedName name:");
            _indentLevel++;
            WriteLine("name.Ctx = ctx;");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("case GeneratedAttribute attr:");
            _indentLevel++;
            WriteLine("attr.Ctx = ctx;");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("case GeneratedSubscript subscript:");
            _indentLevel++;
            WriteLine("subscript.Ctx = ctx;");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("case GeneratedList list:");
            _indentLevel++;
            WriteLine("list.Ctx = ctx;");
            WriteLine("foreach (var elt in list.Elts)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_PyPegen_set_expr_context(elt, ctx);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("case GeneratedTuple tuple:");
            _indentLevel++;
            WriteLine("tuple.Ctx = ctx;");
            WriteLine("foreach (var elt in tuple.Elts)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_PyPegen_set_expr_context(elt, ctx);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("case GeneratedStarred starred:");
            _indentLevel++;
            WriteLine("starred.Ctx = ctx;");
            WriteLine("_PyPegen_set_expr_context(starred.Value, ctx);");
            WriteLine("break;");
            _indentLevel--;
            _indentLevel--;
            WriteLine("}");
            WriteLine("return expr;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        private void GenerateAstHelpersClass()
        {
            WriteLine("// ============================================================");
            WriteLine("// ASTHelpers - Utility functions");
            WriteLine("// ============================================================");
            WriteLine();

            WriteLine("/// <summary>");
            WriteLine("/// Helper functions for AST manipulation");
            WriteLine("/// </summary>");
            WriteLine("public static class ASTHelpers");
            WriteLine("{");
            _indentLevel++;

            WriteLine("public static string ExtractStringValue(GeneratedExpr expr)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (expr is GeneratedName name)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return name.Id;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("throw new InvalidOperationException(\"Expected Name expression\");");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedOperator ExtractOpKind(object op)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// TODO: Implement operator extraction");
            WriteLine("return GeneratedAdd.Instance;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedExprSeq ExtractCallArgs(GeneratedExpr call)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (call is GeneratedCall callExpr)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return callExpr.Args;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return GeneratedExprSeq.Empty;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("public static GeneratedKeywordSeq ExtractCallKeywords(GeneratedExpr call)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (call is GeneratedCall callExpr)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return callExpr.Keywords;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return GeneratedKeywordSeq.Empty;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

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

        private string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            // CPython 3.12: snake_case → PascalCase
            // 각 단어의 첫 글자만 대문자로, 나머지는 그대로 유지
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
