using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SharpPy.PegGenerator.Grammar;

namespace SharpPy.PegGenerator.CodeGenerator
{
    /// <summary>
    /// Maps CPython's _PyAST_* function calls to C# AST construction code
    /// CPython 3.12: _PyAST_If(test, body, orelse, lineno, col_offset, arena)
    /// SharpPy: new GeneratedStmt { Type = "If", Test = test, Body = body, Orelse = orelse }
    /// </summary>
    public class ActionMapper
    {
        // Mapping of _PyAST_* functions to GeneratedStmt/GeneratedExpr types
        private static readonly Dictionary<string, string> StmtTypeMap = new()
        {
            ["_PyAST_If"] = "If",
            ["_PyAST_While"] = "While",
            ["_PyAST_For"] = "For",
            ["_PyAST_AsyncFor"] = "AsyncFor",
            ["_PyAST_With"] = "With",
            ["_PyAST_AsyncWith"] = "AsyncWith",
            ["_PyAST_Try"] = "Try",
            ["_PyAST_TryStar"] = "TryStar",
            ["_PyAST_Match"] = "Match",
            ["_PyAST_FunctionDef"] = "FunctionDef",
            ["_PyAST_AsyncFunctionDef"] = "AsyncFunctionDef",
            ["_PyAST_ClassDef"] = "ClassDef",
            ["_PyAST_Return"] = "Return",
            ["_PyAST_Delete"] = "Delete",
            ["_PyAST_Assign"] = "Assign",
            ["_PyAST_AugAssign"] = "AugAssign",
            ["_PyAST_AnnAssign"] = "AnnAssign",
            ["_PyAST_TypeAlias"] = "TypeAlias",
            ["_PyAST_Raise"] = "Raise",
            ["_PyAST_Assert"] = "Assert",
            ["_PyAST_Global"] = "Global",
            ["_PyAST_Nonlocal"] = "Nonlocal",
            ["_PyAST_Expr"] = "Expr",
            ["_PyAST_Pass"] = "Pass",
            ["_PyAST_Break"] = "Break",
            ["_PyAST_Continue"] = "Continue",
            ["_PyAST_Import"] = "Import",
            ["_PyAST_ImportFrom"] = "ImportFrom",
        };

        private static readonly Dictionary<string, string> ExprTypeMap = new()
        {
            ["_PyAST_BoolOp"] = "BoolOp",
            ["_PyAST_NamedExpr"] = "NamedExpr",
            ["_PyAST_BinOp"] = "BinOp",
            ["_PyAST_UnaryOp"] = "UnaryOp",
            ["_PyAST_Lambda"] = "Lambda",
            ["_PyAST_IfExp"] = "IfExp",
            ["_PyAST_Dict"] = "Dict",
            ["_PyAST_Set"] = "Set",
            ["_PyAST_ListComp"] = "ListComp",
            ["_PyAST_SetComp"] = "SetComp",
            ["_PyAST_DictComp"] = "DictComp",
            ["_PyAST_GeneratorExp"] = "GeneratorExp",
            ["_PyAST_Await"] = "Await",
            ["_PyAST_Yield"] = "Yield",
            ["_PyAST_YieldFrom"] = "YieldFrom",
            ["_PyAST_Compare"] = "Compare",
            ["_PyAST_Call"] = "Call",
            ["_PyAST_FormattedValue"] = "FormattedValue",
            ["_PyAST_JoinedStr"] = "JoinedStr",
            ["_PyAST_Constant"] = "Constant",
            ["_PyAST_Attribute"] = "Attribute",
            ["_PyAST_Subscript"] = "Subscript",
            ["_PyAST_Starred"] = "Starred",
            ["_PyAST_Name"] = "Name",
            ["_PyAST_List"] = "List",
            ["_PyAST_Tuple"] = "Tuple",
            ["_PyAST_Slice"] = "Slice",
        };

        /// <summary>
        /// Map a _PyAST_* function call to C# AST construction code
        /// </summary>
        /// <param name="funcName">_PyAST_If, _PyAST_While, etc.</param>
        /// <param name="args">List of argument names from the action</param>
        /// <param name="variables">Dictionary of variable names in scope</param>
        /// <param name="rule">The rule being generated</param>
        /// <returns>C# code string to construct the AST node</returns>
        public string? MapAction(string funcName, List<string> args, Dictionary<string, string> variables, Rule rule)
        {
            // CPython 3.12: Translate action arguments to C# expressions
            // Keep complex expressions intact (constants, function calls, etc.)
            var translatedArgs = new List<string>();
            foreach (var arg in args)
            {
                if (arg == "EXTRA")
                {
                    // Expand EXTRA macro to position parameters
                    translatedArgs.Add("_start_lineno");
                    translatedArgs.Add("_start_col_offset");
                    translatedArgs.Add("_end_lineno");
                    translatedArgs.Add("_end_col_offset");
                    // Note: arena is C-specific memory management, not needed in C#
                }
                else if (!arg.Contains("arena"))
                {
                    // Translate C expression to C# expression (keep complex expressions)
                    var translated = TranslateToCSharp(arg.Trim(), variables);
                    translatedArgs.Add(translated);
                }
            }

            // Determine if this is a statement or expression
            if (StmtTypeMap.TryGetValue(funcName, out var stmtType))
            {
                return GenerateStmtConstruction(stmtType, translatedArgs, variables);
            }
            else if (ExprTypeMap.TryGetValue(funcName, out var exprType))
            {
                return GenerateExprConstruction(exprType, translatedArgs, variables);
            }

            return null;
        }

        /// <summary>
        /// Translate CPython C expression to C# expression
        /// Examples:
        ///   Or → Or (constant, kept as-is)
        ///   a → a (variable, kept as-is)
        ///   n->v.Name.id → ASTHelpers.ExtractStringValue(n) (token string extraction)
        ///   _PyPegen_seq_insert_in_front(p, a, b) → _PyPegen_seq_insert_in_front(p, a, b) (function call, kept as-is)
        ///   (params) ? params : expr → params ?? expr (ternary to null coalescing)
        /// </summary>
        private string TranslateToCSharp(string expr, Dictionary<string, string> variables)
        {
            // Whitespace/empty
            if (string.IsNullOrWhiteSpace(expr))
                return "null";

            // NULL literal
            if (expr == "NULL")
                return "null";

            // Numeric literals
            if (int.TryParse(expr, out _) || expr.Contains(".") && double.TryParse(expr, out _))
                return expr;

            // String literals
            if (expr.StartsWith("\"") && expr.EndsWith("\""))
                return expr;

            // Position parameters from EXTRA expansion
            if (expr == "_start_lineno" || expr == "_start_col_offset" ||
                expr == "_end_lineno" || expr == "_end_col_offset")
                return expr;

            // CPython constants: Or, And, Eq, Lt, Gt, etc. - keep as-is
            var constants = new[] { "Or", "And", "Eq", "NotEq", "Lt", "LtE", "Gt", "GtE",
                                   "Is", "IsNot", "In", "NotIn", "Load", "Store", "Del" };
            if (constants.Contains(expr))
                return expr;

            // CPython ternary operator with complex expressions - CHECK BEFORE field access
            // This must come BEFORE the ->v. check because ternary may contain ->v.
            // Instead of trying to parse C expressions, use helper functions
            // Examples:
            //   (b) ? ((expr_ty) b)->v.Call.args : NULL → ExtractCallArgs(b)
            //   (b) ? ((expr_ty) b)->v.Call.keywords : NULL → ExtractCallKeywords(b)
            //   (params) ? params : expr → params_ ?? expr (simple case)
            if (expr.Contains("?") && expr.Contains(":"))
            {
                // Check for complex Cast/Field access patterns
                if (expr.Contains("->v.Call.args"))
                {
                    // Extract variable before the ternary
                    var condStart = expr.IndexOf('(');
                    var condEnd = expr.IndexOf(')');
                    if (condStart >= 0 && condEnd > condStart)
                    {
                        var variable = expr.Substring(condStart + 1, condEnd - condStart - 1).Trim();
                        return $"ExtractCallArgs({EscapeCSharpKeyword(variable)})";
                    }
                }
                else if (expr.Contains("->v.Call.keywords"))
                {
                    var condStart = expr.IndexOf('(');
                    var condEnd = expr.IndexOf(')');
                    if (condStart >= 0 && condEnd > condStart)
                    {
                        var variable = expr.Substring(condStart + 1, condEnd - condStart - 1).Trim();
                        return $"ExtractCallKeywords({EscapeCSharpKeyword(variable)})";
                    }
                }
                // Simple ternary: (x) ? x : y → x ?? y
                else
                {
                    var questionPos = expr.IndexOf('?');
                    var colonPos = expr.LastIndexOf(':');
                    var condition = expr.Substring(0, questionPos).Trim().Trim('(', ')');
                    var trueExpr = expr.Substring(questionPos + 1, colonPos - questionPos - 1).Trim();
                    var falseExpr = expr.Substring(colonPos + 1).Trim();

                    // Escape keywords
                    var escapedCondition = EscapeCSharpKeyword(condition);

                    // If condition is same as trueExpr, use null coalescing
                    if (condition == trueExpr)
                    {
                        return $"{escapedCondition} ?? {TranslateToCSharp(falseExpr, variables)}";
                    }
                    else
                    {
                        return $"{escapedCondition} != null ? {TranslateToCSharp(trueExpr, variables)} : {TranslateToCSharp(falseExpr, variables)}";
                    }
                }
            }

            // CPython token field access: n->v.Name.id → ASTHelpers.ExtractStringValue(n)
            // AFTER ternary check
            if (expr.Contains("->v.Name.id") || expr.Contains("->v.String.s") ||
                expr.Contains("->v.Number.") || expr.Contains("->v."))
            {
                // Extract variable name before ->
                var varName = expr.Substring(0, expr.IndexOf("->")).Trim();
                // Remove any parentheses
                varName = varName.Replace("(", "").Replace(")", "");
                return $"ASTHelpers.ExtractStringValue({varName})";
            }

            // Function calls: _PyPegen_*, _PyAST_* - keep as-is
            if (expr.Contains("_PyPegen_") || expr.Contains("_PyAST_"))
                return expr;

            // Simple variable reference - check if it's in variables dict
            if (variables.ContainsKey(expr))
            {
                // Check if it's a C# keyword and needs @ prefix
                return EscapeCSharpKeyword(expr);
            }

            // Check if expression is a simple identifier that needs escaping
            if (System.Text.RegularExpressions.Regex.IsMatch(expr, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
            {
                return EscapeCSharpKeyword(expr);
            }

            // Complex expression - keep as-is
            return expr;
        }

        /// <summary>
        /// Escape C# keywords with _ postfix
        /// CPython grammar uses some C# keywords as variable names (e.g., params, object, class)
        /// We add _ suffix to avoid conflicts: params → params_
        /// </summary>
        private string EscapeCSharpKeyword(string identifier)
        {
            var keywords = new HashSet<string> {
                "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
                "checked", "class", "const", "continue", "decimal", "default", "delegate",
                "do", "double", "else", "enum", "event", "explicit", "extern", "false",
                "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
                "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
                "new", "null", "object", "operator", "out", "override", "params", "private",
                "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
                "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
                "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
                "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
            };

            if (keywords.Contains(identifier))
            {
                return identifier + "_";
            }

            return identifier;
        }

        private string GenerateStmtConstruction(string stmtType, List<string> args, Dictionary<string, string> variables)
        {
            var sb = new StringBuilder();

            // CPython 3.12 pattern: Call _PyAST_* helper method directly
            // Arguments are already translated to C# expressions
            var pyastFuncName = $"_PyAST_{stmtType}";

            // Filter out only null arguments
            var validArgs = args.Where(a => a != "null" && !string.IsNullOrWhiteSpace(a)).ToList();

            // Generate the call
            if (validArgs.Count > 0)
            {
                sb.AppendLine($"_res = {pyastFuncName}({string.Join(", ", validArgs)});");
            }
            else
            {
                sb.AppendLine($"_res = {pyastFuncName}();");
            }

            return sb.ToString();
        }

        private string GenerateExprConstruction(string exprType, List<string> args, Dictionary<string, string> variables)
        {
            var sb = new StringBuilder();

            // CPython 3.12 pattern: Call _PyAST_* helper method directly
            // Arguments are already translated to C# expressions
            var pyastFuncName = $"_PyAST_{exprType}";

            // Filter out only null arguments
            var validArgs = args.Where(a => a != "null" && !string.IsNullOrWhiteSpace(a)).ToList();

            // Generate the call
            if (validArgs.Count > 0)
            {
                sb.AppendLine($"_res = {pyastFuncName}({string.Join(", ", validArgs)});");
            }
            else
            {
                sb.AppendLine($"_res = {pyastFuncName}();");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Simplify C expression to extract the core variable name
        /// Examples:
        ///   a->v.Name.id → a
        ///   (b) ? ((expr_ty) b)->v.Call.args : NULL → b
        ///   c → c
        ///   NULL → NULL
        /// </summary>
        private string SimplifyCExpression(string expr)
        {
            expr = expr.Trim();

            // Handle NULL
            if (expr == "NULL")
                return "NULL";

            // Handle ternary operator: (var) ? ... : ...
            // Extract the variable from the condition
            if (expr.StartsWith("(") && expr.Contains("?"))
            {
                var questionPos = expr.IndexOf('?');
                var condition = expr.Substring(1, questionPos - 1).Trim(); // Remove outer parens
                // Extract first identifier from condition
                var varName = ExtractFirstIdentifier(condition);
                if (!string.IsNullOrEmpty(varName))
                    return varName;
            }

            // Handle field access: a->v.Name.id
            // Extract the root variable (before ->)
            if (expr.Contains("->"))
            {
                var arrowPos = expr.IndexOf("->");
                var root = expr.Substring(0, arrowPos).Trim();
                // Remove any leading parentheses or casts
                root = ExtractFirstIdentifier(root);
                if (!string.IsNullOrEmpty(root))
                    return root;
            }

            // Handle type casts: ((expr_ty) b)
            if (expr.StartsWith("((") && expr.Contains(")"))
            {
                // Find the closing paren of the cast
                int depth = 0;
                int castEnd = -1;
                for (int i = 0; i < expr.Length; i++)
                {
                    if (expr[i] == '(') depth++;
                    else if (expr[i] == ')')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            castEnd = i;
                            break;
                        }
                    }
                }
                if (castEnd > 0 && castEnd < expr.Length - 1)
                {
                    // Extract the variable after the cast
                    var afterCast = expr.Substring(castEnd + 1).Trim();
                    return SimplifyCExpression(afterCast);
                }
            }

            // Default: extract first identifier
            return ExtractFirstIdentifier(expr);
        }

        /// <summary>
        /// Extract the first C identifier from a string
        /// </summary>
        private string ExtractFirstIdentifier(string str)
        {
            str = str.Trim();
            var sb = new StringBuilder();
            bool started = false;

            foreach (char c in str)
            {
                if (char.IsLetter(c) || c == '_')
                {
                    sb.Append(c);
                    started = true;
                }
                else if (started && char.IsDigit(c))
                {
                    sb.Append(c);
                }
                else if (started)
                {
                    // End of identifier
                    break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Get the C# variable name for an argument, or "null" if not found
        /// </summary>
        private string GetVarOrNull(string argName, Dictionary<string, string> variables)
        {
            // Handle NULL literal
            if (argName == "NULL" || argName == "null" || string.IsNullOrWhiteSpace(argName))
            {
                return "null";
            }

            // Handle numeric literals
            if (int.TryParse(argName, out _))
            {
                return argName;
            }

            // Handle string literals
            if (argName.StartsWith("\"") && argName.EndsWith("\""))
            {
                return argName;
            }

            // Handle position parameter names (from EXTRA expansion)
            // These are passed as-is since they're defined in the generated method
            if (argName == "_start_lineno" || argName == "_start_col_offset" ||
                argName == "_end_lineno" || argName == "_end_col_offset")
            {
                return argName;
            }

            // Look up variable
            if (variables.TryGetValue(argName, out var varName))
            {
                return varName;
            }

            // Default: Variable not found - return null
            // This happens when grammar parsing skips some items (e.g., after cut operator)
            return "null /* Missing variable: " + argName + " */";
        }
    }
}
