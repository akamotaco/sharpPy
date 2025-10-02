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
            // CPython 3.12: Expand EXTRA into actual position parameters
            // EXTRA = _start_lineno, _start_col_offset, _end_lineno, _end_col_offset, p->arena
            var expandedArgs = new List<string>();
            foreach (var arg in args)
            {
                if (arg == "EXTRA")
                {
                    // Expand EXTRA macro to position parameters
                    expandedArgs.Add("_start_lineno");
                    expandedArgs.Add("_start_col_offset");
                    expandedArgs.Add("_end_lineno");
                    expandedArgs.Add("_end_col_offset");
                    // Note: arena is C-specific memory management, not needed in C#
                }
                else if (!arg.Contains("arena"))
                {
                    // Skip arena parameter (C-specific)
                    expandedArgs.Add(arg);
                }
            }

            // Determine if this is a statement or expression
            if (StmtTypeMap.TryGetValue(funcName, out var stmtType))
            {
                return GenerateStmtConstruction(stmtType, expandedArgs, variables);
            }
            else if (ExprTypeMap.TryGetValue(funcName, out var exprType))
            {
                return GenerateExprConstruction(exprType, expandedArgs, variables);
            }

            return null;
        }

        private string GenerateStmtConstruction(string stmtType, List<string> args, Dictionary<string, string> variables)
        {
            var sb = new StringBuilder();

            // CPython 3.12 pattern: Call _PyAST_* helper method directly
            // These methods are already implemented in PyParserBase
            var pyastFuncName = $"_PyAST_{stmtType}";

            // Build argument list, filtering out only NULL
            // Position parameters (_start_lineno, etc.) are now included
            var actualArgs = args.Where(a =>
                a != "NULL" &&
                !string.IsNullOrWhiteSpace(a)
            ).Select(a => {
                var result = GetVarOrNull(a, variables);
                Console.WriteLine($"[ActionMapper] Stmt arg '{a}' -> '{result}'");
                return result;
            }).ToList();

            // Generate the call
            if (actualArgs.Count > 0)
            {
                sb.AppendLine($"_res = {pyastFuncName}({string.Join(", ", actualArgs)});");
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
            var pyastFuncName = $"_PyAST_{exprType}";

            // Build argument list, filtering out only NULL
            // Position parameters (_start_lineno, etc.) are now included
            var actualArgs = args.Where(a =>
                a != "NULL" &&
                !string.IsNullOrWhiteSpace(a)
            ).Select(a => GetVarOrNull(a, variables)).ToList();

            // Generate the call
            if (actualArgs.Count > 0)
            {
                sb.AppendLine($"_res = {pyastFuncName}({string.Join(", ", actualArgs)});");
            }
            else
            {
                sb.AppendLine($"_res = {pyastFuncName}();");
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
