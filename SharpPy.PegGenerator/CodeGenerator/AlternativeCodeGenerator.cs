using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SharpPy.PegGenerator.Grammar;

namespace SharpPy.PegGenerator.CodeGenerator
{
    /// <summary>
    /// Generates C# code for a single Alternative in a PEG rule
    /// CPython 3.12 style: converts python.gram alternatives to direct C# parsing logic
    /// </summary>
    public class AlternativeCodeGenerator
    {
        private readonly CSharpCodeGenerator _parent;
        private readonly Rule _rule;
        private readonly Alternative _alternative;
        private readonly int _alternativeIndex;
        private readonly Dictionary<string, string> _variables = new();
        private int _tempVarCounter = 0;
        private readonly string _labelPrefix;

        public AlternativeCodeGenerator(
            CSharpCodeGenerator parent,
            Rule rule,
            Alternative alternative,
            int alternativeIndex)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _rule = rule ?? throw new ArgumentNullException(nameof(rule));
            _alternative = alternative ?? throw new ArgumentNullException(nameof(alternative));
            _alternativeIndex = alternativeIndex;
            _labelPrefix = $"alt{alternativeIndex}";
        }

        /// <summary>
        /// Generate complete C# code for this alternative
        /// </summary>
        public void Generate()
        {
            // CPython 3.12 style: do-while(false) block + break for failure
            _parent.WriteLine($"// Alternative {_alternativeIndex + 1}");
            _parent.WriteLine("do");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine("_position = _mark;");
            _parent.WriteLine();

            // Generate code for each item in the sequence
            bool allItemsSucceeded = GenerateItemSequence();

            if (allItemsSucceeded)
            {
                // Generate AST construction code
                GenerateActionCode();
                _parent.WriteLine("if (_res != null) goto done;");
            }

            _parent.Dedent();
            _parent.WriteLine("} while (false);");
            _parent.WriteLine();
        }

        /// <summary>
        /// Generate code for the sequence of items in this alternative
        /// Returns true if we should proceed to action code
        /// </summary>
        private bool GenerateItemSequence()
        {
            var usedVarNames = new HashSet<string>();

            foreach (var item in _alternative.Items)
            {
                // Determine variable name
                string varName;
                if (!string.IsNullOrEmpty(item.Name))
                {
                    // CPython 3.12: Check for C# reserved keywords
                    var baseName = EscapeCSharpKeyword(item.Name);

                    // Handle duplicate names in same alternative (e.g., 'a' used twice)
                    if (usedVarNames.Contains(baseName))
                    {
                        // Add suffix to make unique
                        int counter = 2;
                        string uniqueName = $"{baseName}_{counter}";
                        while (usedVarNames.Contains(uniqueName))
                        {
                            counter++;
                            uniqueName = $"{baseName}_{counter}";
                        }
                        varName = uniqueName;
                        usedVarNames.Add(uniqueName);
                        _variables[item.Name] = varName;
                    }
                    else
                    {
                        varName = baseName;
                        usedVarNames.Add(baseName);
                        _variables[item.Name] = varName;
                    }
                }
                else
                {
                    varName = $"_tmp{_tempVarCounter++}";
                }

                // Generate code for this item
                var itemGen = new ItemCodeGenerator(_parent, item, varName, _labelPrefix);
                itemGen.Generate();
            }

            return true;
        }

        /// <summary>
        /// Escape C# reserved keywords by adding suffix underscore
        /// CPython 3.12: Use suffix instead of @ prefix for compatibility with generated var names
        /// </summary>
        private string EscapeCSharpKeyword(string name)
        {
            // CPython 3.12: Common variable names that conflict with C# keywords
            var keywords = new HashSet<string>
            {
                "params", "object", "string", "int", "bool", "class", "struct",
                "interface", "enum", "namespace", "using", "void", "byte", "sbyte",
                "short", "ushort", "uint", "long", "ulong", "float", "double",
                "decimal", "char", "true", "false", "null", "if", "else", "while",
                "for", "foreach", "do", "switch", "case", "default", "break",
                "continue", "return", "goto", "try", "catch", "finally", "throw",
                "public", "private", "protected", "internal", "static", "readonly",
                "const", "virtual", "override", "abstract", "sealed", "new",
                "is", "as", "typeof", "sizeof", "checked", "unchecked", "lock",
                "out", "ref", "in", "event", "delegate", "operator", "explicit",
                "implicit", "base", "this"
            };

            if (keywords.Contains(name))
            {
                return name + "_"; // e.g., params_ instead of @params
            }
            return name;
        }

        /// <summary>
        /// Generate AST construction code from the action
        /// </summary>
        private void GenerateActionCode()
        {
            if (string.IsNullOrEmpty(_alternative.Action))
            {
                // No action specified - return first non-null variable or create default AST
                _parent.WriteLine("// No action specified - using default result");

                var firstVar = _variables.Values.FirstOrDefault();
                if (firstVar != null)
                {
                    _parent.WriteLine($"_res = ({_parent.GetRuleReturnType(_rule)})((object?){firstVar});");
                }
                else
                {
                    _parent.WriteLine($"_res = default({_parent.GetRuleReturnType(_rule)});");
                }
                return;
            }

            // Parse and convert action code
            // Handle multi-line actions properly in comments
            var actionLines = _alternative.Action.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (actionLines.Length == 1)
            {
                _parent.WriteLine($"// Action: {_alternative.Action}");
            }
            else
            {
                _parent.WriteLine($"// Action (multiline):");
                foreach (var line in actionLines)
                {
                    _parent.WriteLine($"//   {line.Trim()}");
                }
            }

            // Check if this is a _PyAST_* function call
            if (_alternative.Action.Contains("_PyAST_"))
            {
                GeneratePyASTAction();
            }
            else
            {
                // Direct variable reference or expression
                var cleanAction = _alternative.Action.Trim();
                if (_variables.ContainsKey(cleanAction))
                {
                    _parent.WriteLine($"_res = ({_parent.GetRuleReturnType(_rule)})((object?){_variables[cleanAction]});");
                }
                else
                {
                    // Complex expression - try to evaluate
                    // Handle multi-line expressions properly in comments
                    var exprLines = cleanAction.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (exprLines.Length == 1)
                    {
                        _parent.WriteLine($"// TODO: Complex action expression: {cleanAction}");
                    }
                    else
                    {
                        _parent.WriteLine($"// TODO: Complex action expression (multiline):");
                        foreach (var line in exprLines)
                        {
                            _parent.WriteLine($"//   {line.Trim()}");
                        }
                    }
                    _parent.WriteLine($"_res = default({_parent.GetRuleReturnType(_rule)});");
                }
            }
        }

        /// <summary>
        /// Generate code for _PyAST_* function calls
        /// </summary>
        private void GeneratePyASTAction()
        {
            var action = _alternative.Action;

            // Check if the action indicates a sequence result (asdl_stmt_seq* or asdl_expr_seq*)
            bool needsSequenceWrap = action.Contains("asdl_stmt_seq*") || action.Contains("asdl_expr_seq*") || action.Contains("_PyPegen_singleton_seq");

            // Remove CHECK_VERSION wrapper first
            // CHECK_VERSION(type, version, "message", actual_call)
            if (action.Contains("CHECK_VERSION("))
            {
                var cvStart = action.IndexOf("CHECK_VERSION(");
                var cvEnd = FindMatchingParen(action, cvStart + 13);  // +13 for "CHECK_VERSION"
                if (cvEnd > cvStart)
                {
                    var cvContent = action.Substring(cvStart + 14, cvEnd - cvStart - 14);
                    // Find the last comma to get the actual AST call
                    var lastComma = cvContent.LastIndexOf(',');
                    if (lastComma >= 0)
                    {
                        action = cvContent.Substring(lastComma + 1).Trim();
                    }
                }
            }

            // Extract function name and arguments
            // Example: _PyAST_If(a, b, c, EXTRA)
            var funcStart = action.IndexOf("_PyAST_");
            if (funcStart < 0)
            {
                _parent.WriteLine($"// No _PyAST_ function in action: {action}");
                _parent.WriteLine($"_res = default({_parent.GetRuleReturnType(_rule)});");
                return;
            }

            var funcEnd = action.IndexOf('(', funcStart);
            var argsEnd = action.LastIndexOf(')');

            if (funcEnd < 0 || argsEnd < 0 || funcEnd >= argsEnd)
            {
                _parent.WriteLine($"// Failed to parse action: {action}");
                _parent.WriteLine($"_res = default({_parent.GetRuleReturnType(_rule)});");
                return;
            }

            var funcName = action.Substring(funcStart, funcEnd - funcStart);
            var argsStr = action.Substring(funcEnd + 1, argsEnd - funcEnd - 1);

            // Clean CPython macros from arguments
            argsStr = CleanCPythonMacros(argsStr);

            // Split arguments respecting parentheses depth
            var args = SplitArgumentsRespectingParens(argsStr);

            // Use ActionMapper to convert to C# AST construction
            var mapper = new ActionMapper();
            var astCode = mapper.MapAction(funcName, args, _variables, _rule);

            if (astCode != null)
            {
                // If the action needs a sequence wrap (singleton_seq), modify the generated code
                if (needsSequenceWrap && _parent.GetRuleReturnType(_rule).Contains("Seq"))
                {
                    // Replace "_res = _PyAST_..." with wrapping in singleton_seq
                    astCode = astCode.Replace("_res = _PyAST_", "var _stmt_tmp = _PyAST_");
                    _parent.WriteLine(astCode);
                    _parent.WriteLine("_res = (_stmt_tmp != null) ? new GeneratedStmtSeq { _stmt_tmp } : null;");
                }
                else
                {
                    _parent.WriteLine(astCode);
                }
            }
            else
            {
                _parent.WriteLine($"// Unknown AST function: {funcName}");
                _parent.WriteLine($"_res = default({_parent.GetRuleReturnType(_rule)});");
            }
        }

        /// <summary>
        /// Clean CPython-specific macros and function calls from action code
        /// CHECK(type, expr) → expr
        /// _PyPegen_singleton_seq(p, x) → x wrapped in list
        /// </summary>
        private string CleanCPythonMacros(string argsStr)
        {
            // Remove CHECK(...) macro
            // Pattern: CHECK(type, expression) → expression
            while (argsStr.Contains("CHECK("))
            {
                var checkStart = argsStr.IndexOf("CHECK(");
                var checkEnd = FindMatchingParen(argsStr, checkStart + 5);  // +5 for "CHECK"
                if (checkEnd > checkStart)
                {
                    var checkContent = argsStr.Substring(checkStart + 6, checkEnd - checkStart - 6);
                    // Extract the expression after the first comma
                    var commaPos = checkContent.IndexOf(',');
                    if (commaPos >= 0)
                    {
                        var expression = checkContent.Substring(commaPos + 1).Trim();
                        argsStr = argsStr.Substring(0, checkStart) + expression + argsStr.Substring(checkEnd + 1);
                    }
                    else
                    {
                        // No comma, remove the entire CHECK
                        argsStr = argsStr.Substring(0, checkStart) + argsStr.Substring(checkEnd + 1);
                    }
                }
                else
                {
                    break; // Can't find matching paren, give up
                }
            }

            // Remove _PyPegen_singleton_seq(p, x) → x (for now)
            while (argsStr.Contains("_PyPegen_singleton_seq("))
            {
                var seqStart = argsStr.IndexOf("_PyPegen_singleton_seq(");
                var seqEnd = FindMatchingParen(argsStr, seqStart + 22);  // +22 for "_PyPegen_singleton_seq"
                if (seqEnd > seqStart)
                {
                    var seqContent = argsStr.Substring(seqStart + 23, seqEnd - seqStart - 23);
                    // Extract the expression after the first comma (skip 'p')
                    var commaPos = seqContent.IndexOf(',');
                    if (commaPos >= 0)
                    {
                        var expression = seqContent.Substring(commaPos + 1).Trim();
                        argsStr = argsStr.Substring(0, seqStart) + expression + argsStr.Substring(seqEnd + 1);
                    }
                    else
                    {
                        argsStr = argsStr.Substring(0, seqStart) + argsStr.Substring(seqEnd + 1);
                    }
                }
                else
                {
                    break;
                }
            }

            // Remove NEW_TYPE_COMMENT(p, x) → x
            while (argsStr.Contains("NEW_TYPE_COMMENT("))
            {
                var tcStart = argsStr.IndexOf("NEW_TYPE_COMMENT(");
                var tcEnd = FindMatchingParen(argsStr, tcStart + 16);  // +16 for "NEW_TYPE_COMMENT"
                if (tcEnd > tcStart)
                {
                    var tcContent = argsStr.Substring(tcStart + 17, tcEnd - tcStart - 17);
                    // Extract the expression after the first comma (skip 'p')
                    var commaPos = tcContent.IndexOf(',');
                    if (commaPos >= 0)
                    {
                        var expression = tcContent.Substring(commaPos + 1).Trim();
                        argsStr = argsStr.Substring(0, tcStart) + expression + argsStr.Substring(tcEnd + 1);
                    }
                    else
                    {
                        argsStr = argsStr.Substring(0, tcStart) + "null" + argsStr.Substring(tcEnd + 1);
                    }
                }
                else
                {
                    break;
                }
            }

            // Remove any remaining incomplete macro calls (ending without closing paren)
            // Pattern: MACRO_NAME(... without closing )
            var macroPatterns = new[] { "CHECK(", "NEW_TYPE_COMMENT(", "_PyPegen_" };
            foreach (var pattern in macroPatterns)
            {
                var pos = argsStr.IndexOf(pattern);
                if (pos >= 0)
                {
                    // Find if there's a closing paren
                    var parenPos = FindMatchingParen(argsStr, pos + pattern.Length - 1);
                    if (parenPos < 0)
                    {
                        // No matching paren found, remove from this pattern to end
                        argsStr = argsStr.Substring(0, pos).TrimEnd();
                    }
                }
            }

            return argsStr;
        }

        /// <summary>
        /// Find the matching closing parenthesis
        /// </summary>
        private int FindMatchingParen(string str, int openPos)
        {
            if (openPos >= str.Length || str[openPos] != '(')
                return -1;

            int depth = 1;
            for (int i = openPos + 1; i < str.Length; i++)
            {
                if (str[i] == '(')
                    depth++;
                else if (str[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Split arguments by comma, but respect parentheses depth
        /// Example: "a, (b ? x : y), c" → ["a", "(b ? x : y)", "c"]
        /// </summary>
        private List<string> SplitArgumentsRespectingParens(string argsStr)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            int depth = 0;

            for (int i = 0; i < argsStr.Length; i++)
            {
                char c = argsStr[i];

                if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                    current.Append(c);
                }
                else if (c == ')' || c == ']' || c == '}')
                {
                    depth--;
                    current.Append(c);
                }
                else if (c == ',' && depth == 0)
                {
                    // Top-level comma - split here
                    var arg = current.ToString().Trim();
                    if (!string.IsNullOrEmpty(arg))
                    {
                        result.Add(arg);
                    }
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            // Add the last argument
            var lastArg = current.ToString().Trim();
            if (!string.IsNullOrEmpty(lastArg))
            {
                result.Add(lastArg);
            }

            return result;
        }
    }
}
