using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SharpPy.Core;
using SharpPy.Generated;

namespace SharpPy.Tools
{
    /// <summary>
    /// AST를 CPython 3.12 호환 포맷으로 출력하는 클래스
    /// Python의 ast.dump(indent=2)와 동일한 포맷
    /// </summary>
    public class ASTDumper
    {
        private int _indentSize = 2;

        public void DumpAST(string pythonFile)
        {
            try
            {
#if DEBUG
                Console.WriteLine("[DEBUG] ASTDumper.DumpAST START");
#endif
                if (string.IsNullOrEmpty(pythonFile) || !File.Exists(pythonFile))
                {
                    Console.WriteLine("Error: Python file not found.");
                    return;
                }

#if DEBUG
                Console.WriteLine($"[DEBUG] Reading file: {pythonFile}");
#endif
                var source = File.ReadAllText(pythonFile);
#if DEBUG
                Console.WriteLine($"[DEBUG] File content length: {source.Length}");
#endif

                // QuietMode가 아닐 때만 헤더 출력
                if (!SharpPyConfig.QuietMode)
                {
                    Console.WriteLine($"🔧 SharpPy AST Dump: {pythonFile}");
                    Console.WriteLine("========================================");
                }

#if DEBUG
                Console.WriteLine("[DEBUG] Calling PyParserRuntime.ParseSource...");
#endif
                var tokens = PyParserRuntime.LexerSource(source);
                var statements = PyParserRuntime.ParseSource(tokens, source, pythonFile);
#if DEBUG
                Console.WriteLine($"[DEBUG] ParseSource returned {statements?.Count ?? 0} statements");
#endif

                // CPython 3.12 스타일: Module(body=[...], type_ignores=[])
                Console.WriteLine("Module(");
                Console.WriteLine("  body=[");

                for (int i = 0; i < statements.Count; i++)
                {
                    var stmt = statements[i];
                    var formattedStmt = FormatNode(stmt, 2);
                    Console.Write(formattedStmt);

                    if (i < statements.Count - 1)
                        Console.WriteLine(",");
                    else
                        Console.WriteLine();
                }

                Console.WriteLine("  ],");
                Console.WriteLine("  type_ignores=[])");

                // QuietMode가 아닐 때만 성공 메시지 출력
                if (!SharpPyConfig.QuietMode)
                {
                    Console.WriteLine("========================================");
                    Console.WriteLine($"✅ Successfully dumped AST with {statements.Count} statements");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during AST dump: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// AST 노드를 재귀적으로 포맷팅
        /// </summary>
        private string FormatNode(object node, int indentLevel)
        {
            if (node == null)
                return "None";

            var indent = new string(' ', indentLevel);
            var sb = new StringBuilder();

            // Statement/Expression 노드들 처리
            if (node is ASTNode astNode)
            {
                sb.Append(indent);
                sb.Append(astNode.NodeType);
                sb.Append("(");

                var fields = GetNodeFields(astNode);
                if (fields.Count > 0)
                {
                    sb.AppendLine();

                    for (int i = 0; i < fields.Count; i++)
                    {
                        var (name, value) = fields[i];
                        sb.Append(indent);
                        sb.Append("  ");
                        sb.Append(name);
                        sb.Append("=");

                        var formattedValue = FormatValue(value, indentLevel + _indentSize);

                        // 리스트나 복잡한 객체는 개행
                        if (value is IList || value is ASTNode)
                        {
                            sb.AppendLine();
                            sb.Append(formattedValue);
                        }
                        else
                        {
                            sb.Append(formattedValue);
                        }

                        if (i < fields.Count - 1)
                            sb.Append(",");

                        sb.AppendLine();
                    }

                    sb.Append(indent);
                }
                sb.Append(")");
            }
            else
            {
                sb.Append(indent);
                sb.Append(FormatValue(node, indentLevel));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 값을 포맷팅 (리스트, 상수 등)
        /// </summary>
        private string FormatValue(object value, int indentLevel)
        {
            if (value == null)
                return "None";

            var indent = new string(' ', indentLevel);

            // List 처리
            if (value is IList list)
            {
                if (list.Count == 0)
                    return "[]";

                var sb = new StringBuilder();
                sb.Append(indent);
                sb.AppendLine("[");

                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    var formatted = FormatNode(item, indentLevel + _indentSize);
                    sb.Append(formatted);

                    if (i < list.Count - 1)
                        sb.Append(",");

                    sb.AppendLine();
                }

                sb.Append(indent);
                sb.Append("]");
                return sb.ToString();
            }

            // ASTNode 처리
            if (value is ASTNode node)
            {
                return FormatNode(node, indentLevel);
            }

            // ImportAlias 처리 (CPython 3.12 호환)
            if (value is ImportAlias alias)
            {
                var sb = new StringBuilder();
                sb.Append(indent);
                sb.Append("alias(");
                sb.Append($"name='{alias.Name}'");
                if (!string.IsNullOrEmpty(alias.AsName))
                {
                    sb.Append($", asname='{alias.AsName}'");
                }
                sb.Append(")");
                return sb.ToString();
            }

            // GeneratedCmpop 처리 (CPython 3.12 호환)
            if (value is Generated.GeneratedCmpop cmpop)
            {
                var typeName = value.GetType().Name.Replace("Generated", "");
                return $"{indent}{typeName}()";
            }

            // 기본 타입 처리
            if (value is string str)
                return $"'{str}'";

            if (value is bool b)
                return b ? "True" : "False";

            if (value is int || value is long || value is double || value is float)
                return value.ToString();

            // PyObject 처리 (CPython repr() 스타일)
            if (value is PyObject pyObj)
            {
                if (pyObj is PyString pyStr)
                    return $"'{pyStr.Value}'";
                return pyObj.ToString();
            }

            // Enum 처리
            if (value is Enum)
                return value.ToString();

            // 기타 객체는 ToString()
            return value.ToString() ?? "None";
        }

        /// <summary>
        /// AST 노드에서 중요한 필드들을 추출
        /// CPython과 유사한 필드명으로 매핑
        /// </summary>
        private List<(string name, object value)> GetNodeFields(ASTNode node)
        {
            var fields = new List<(string, object)>();

            switch (node)
            {
                case ChainedAssignStatement chainedAssign:
                    fields.Add(("targets", chainedAssign.Targets));
                    fields.Add(("value", chainedAssign.Value));
                    break;

                case AssignStatement assign:
                    fields.Add(("targets", assign.Targets));
                    fields.Add(("value", assign.Value));
                    break;

                case ExpressionStatement exprStmt:
                    fields.Add(("value", exprStmt.Expression));
                    break;

                case FunctionDefStatement funcDef:
                    fields.Add(("name", funcDef.Name));
                    fields.Add(("args", funcDef.Parameters));
                    fields.Add(("body", funcDef.Body));
                    if (funcDef.Decorators?.Count > 0)
                        fields.Add(("decorator_list", funcDef.Decorators));
                    break;

                case ClassDefStatement classDef:
                    fields.Add(("name", classDef.Name));
                    fields.Add(("bases", classDef.Bases));
                    fields.Add(("body", classDef.Body));
                    break;

                case ReturnStatement returnStmt:
                    if (returnStmt.Value != null)
                        fields.Add(("value", returnStmt.Value));
                    break;

                case WhileStatement whileStmt:
                    fields.Add(("test", whileStmt.Test));
                    fields.Add(("body", whileStmt.Body));
                    break;

                case ForStatement forStmt:
                    fields.Add(("target", forStmt.Target));
                    fields.Add(("iter", forStmt.Iter));
                    fields.Add(("body", forStmt.Body));
                    break;

                case CallExpression call:
                    fields.Add(("func", call.Function));
                    fields.Add(("args", call.Arguments ?? new List<Expression>()));
                    if (call.Keywords?.Count > 0)
                        fields.Add(("keywords", call.Keywords));
                    break;

                case NameExpression name:
                    fields.Add(("id", name.Name));
                    if (name.Ctx != null)
                        fields.Add(("ctx", name.Ctx));
                    break;

                case ConstantExpression constant:
                    fields.Add(("value", constant.Value));
                    break;

                case BinOpExpression binOp:
                    fields.Add(("left", binOp.Left));
                    fields.Add(("op", binOp.OpNode));
                    fields.Add(("right", binOp.Right));
                    break;

                case CompareExpression compare:
                    fields.Add(("left", compare.Left));
                    fields.Add(("ops", compare.Ops));
                    fields.Add(("comparators", compare.Comparators));
                    break;

                case UnaryOpExpression unaryOp:
                    fields.Add(("op", unaryOp.OpNode));
                    fields.Add(("operand", unaryOp.Operand));
                    break;

                case ListExpression list:
                    fields.Add(("elts", list.Elements));
                    break;

                case DictExpression dict:
                    // CPython은 keys/values로 분리하지만 SharpPy는 Items로 관리
                    var keys = dict.Items.Select(item => item.Key).ToList();
                    var values = dict.Items.Select(item => item.Value).ToList();
                    fields.Add(("keys", keys));
                    fields.Add(("values", values));
                    break;

                case SubscriptExpression subscript:
                    fields.Add(("value", subscript.Value));
                    fields.Add(("slice", subscript.Slice));
                    break;

                case AttributeExpression attr:
                    fields.Add(("value", attr.Value));
                    fields.Add(("attr", attr.Attr));
                    break;

                case LambdaExpression lambda:
                    fields.Add(("args", lambda.Args));
                    fields.Add(("body", lambda.Body));
                    break;

                case TryStatement tryStmt:
                    fields.Add(("body", tryStmt.Body));
                    if (tryStmt.Handlers?.Count > 0)
                        fields.Add(("handlers", tryStmt.Handlers));
                    if (tryStmt.OrElse?.Count > 0)
                        fields.Add(("orelse", tryStmt.OrElse));
                    if (tryStmt.FinalBody?.Count > 0)
                        fields.Add(("finalbody", tryStmt.FinalBody));
                    break;

                case ImportStatement import:
                    fields.Add(("names", import.Names));
                    break;

                case ImportFromStatement importFrom:
                    fields.Add(("module", importFrom.Module));
                    fields.Add(("names", importFrom.Names));
                    fields.Add(("level", importFrom.Level));
                    break;

                default:
                    // Reflection으로 public 프로퍼티 자동 추출
                    var properties = node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var prop in properties)
                    {
                        // 메타데이터 필드 제외
                        if (prop.Name == "NodeType" || prop.Name == "LineNo" ||
                            prop.Name == "ColOffset" || prop.Name == "Parent")
                            continue;

                        try
                        {
                            var value = prop.GetValue(node);
                            if (value != null)
                            {
                                // 빈 리스트는 제외
                                if (value is IList list && list.Count == 0)
                                    continue;

                                fields.Add((ToCamelCase(prop.Name), value));
                            }
                        }
                        catch
                        {
                            // 접근 불가능한 프로퍼티는 건너뛰기
                        }
                    }
                    break;
            }

            return fields;
        }

        /// <summary>
        /// PascalCase를 snake_case로 변환
        /// </summary>
        private string ToCamelCase(string pascalCase)
        {
            if (string.IsNullOrEmpty(pascalCase))
                return pascalCase;

            // 간단한 매핑
            var result = new StringBuilder();
            for (int i = 0; i < pascalCase.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascalCase[i]))
                    result.Append('_');
                result.Append(char.ToLower(pascalCase[i]));
            }
            return result.ToString();
        }
    }
}