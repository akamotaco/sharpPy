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
        private readonly List<string> _allVarNames = new();  // Track all variables in order
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

            // CPython 3.12: Check error_indicator at start of each alternative
            _parent.WriteLine("// CPython 3.12: Check error indicator before trying alternative");
            _parent.WriteLine("if (_pendingSyntaxError != null)");
            _parent.WriteLine("{");
            _parent.Indent();
            _parent.WriteLine("_res = null;");
            _parent.WriteLine("break;");
            _parent.Dedent();
            _parent.WriteLine("}");
            _parent.WriteLine();

            // Generate code for each item in the sequence
            bool allItemsSucceeded = GenerateItemSequence();

            if (allItemsSucceeded)
            {
                // Generate AST construction code
                GenerateActionCode();

                // CPython 3.12: Don't clear error here - let the rule-level done block handle it
                // RAISE_* actions set pending error and break, so they never reach here
                bool isRaiseAction = !string.IsNullOrEmpty(_alternative.Action) &&
                    (_alternative.Action.Contains("RAISE_SYNTAX_ERROR") ||
                     _alternative.Action.Contains("RAISE_INDENTATION_ERROR") ||
                     _alternative.Action.Contains("RAISE_"));

                if (!isRaiseAction)
                {
                    _parent.WriteLine("if (_res != null) goto done;");
                }
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
                // CPython 3.12: Lookahead items don't produce values, so skip tracking them
                bool isLookahead = item.Atom is PositiveLookahead || item.Atom is NegativeLookahead;

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
                        if (!isLookahead) _allVarNames.Add(varName);  // Track all variables except lookahead
                    }
                    else
                    {
                        varName = baseName;
                        usedVarNames.Add(baseName);
                        _variables[item.Name] = varName;
                        if (!isLookahead) _allVarNames.Add(varName);  // Track all variables except lookahead
                    }
                }
                else
                {
                    varName = $"_tmp{_tempVarCounter++}";
                    if (!isLookahead) _allVarNames.Add(varName);  // Track unnamed variables except lookahead
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
                // CPython 3.12: No action specified - return first variable (named or unnamed)
                // Special case: Single token alternatives need implicit conversion to AST nodes
                _parent.WriteLine("// No action specified - using default result");

                // Check if this alternative contains invalid_* rules (error recovery)
                bool hasInvalidRule = _alternative.Items.Any(item =>
                {
                    if (item.Atom is SharpPy.PegGenerator.Grammar.RuleRef ruleRef)
                    {
                        return ruleRef.Name.StartsWith("invalid_");
                    }
                    return false;
                });

                // If this is an error recovery alternative with invalid_* rules, return null
                if (hasInvalidRule)
                {
                    _parent.WriteLine("// Error recovery alternative - return null");
                    _parent.WriteLine("_res = null;");
                    return;
                }

                var firstVar = _allVarNames.FirstOrDefault();
                Console.WriteLine($"[CODEGEN] Alternative with no action: firstVar={firstVar}, allVarCount={_allVarNames.Count}, rule={_rule.Name}");
                if (firstVar != null)
                {
                    // Check if this is a single token alternative that needs implicit AST conversion
                    // CPython 3.12: NAME → _PyPegen_name_token(), NUMBER → _PyPegen_number_token(), STRING → strings
                    if (_alternative.Items.Count == 1)
                    {
                        var item = _alternative.Items[0];

                        // Check if it's a token reference (RuleRef to NAME/NUMBER/STRING)
                        if (item.Atom is SharpPy.PegGenerator.Grammar.RuleRef ruleRef)
                        {
                            var ruleName = ruleRef.Name;

                            // CPython 3.12: Implicit token to AST node conversion for token rules
                            // IMPORTANT: ItemCodeGenerator already auto-converts NAME/NUMBER/STRING to AST nodes
                            // (unless insideRepeater=true, which doesn't apply to single-item alternatives)
                            // So firstVar is already GeneratedName/GeneratedConstant, not a token
                            // Just assign directly - no double conversion needed
                            if (ruleName == "NAME" && _rule.ReturnType == "expr_ty")
                            {
                                _parent.WriteLine($"// CPython 3.12: NAME token → Name expression (already converted by ItemCodeGenerator)");
                                _parent.WriteLine($"_res = {firstVar};");
                                return;
                            }
                            else if (ruleName == "NUMBER" && _rule.ReturnType == "expr_ty")
                            {
                                _parent.WriteLine($"// CPython 3.12: NUMBER token → Constant expression (already converted by ItemCodeGenerator)");
                                _parent.WriteLine($"_res = {firstVar};");
                                return;
                            }
                            else if (ruleName == "STRING" && _rule.ReturnType == "expr_ty")
                            {
                                _parent.WriteLine($"// CPython 3.12: STRING token → handled by strings rule (already converted by ItemCodeGenerator)");
                                _parent.WriteLine($"_res = {firstVar};");
                                return;
                            }
                        }
                    }

                    // Default: assign to result
                    // CPython 3.12: C allows implicit void* conversions, C# needs explicit casts
                    // Only cast if necessary - avoid (object?) boxing for performance
                    var targetType = _parent.GetRuleReturnType(_rule);

                    // Special case: Converting specific Seq types to GeneratedMixedSeq
                    // PegenHelpers.ToMixedSeq has overloads for all sequence types
                    // Let C# method overload resolution choose the right one
                    if (targetType == "GeneratedMixedSeq")
                    {
                        _parent.WriteLine($"_res = PegenHelpers.ToMixedSeq({firstVar});");
                        return;
                    }

                    // If variable type matches target type, direct assignment (no cast needed)
                    // Otherwise, explicit cast (C#'s strong typing requirement)
                    _parent.WriteLine($"_res = ({targetType}){firstVar};");
                }
                else
                {
                    // No variables at all - this alternative matches empty sequence
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

            // Check if this is a RAISE_SYNTAX_ERROR, RAISE_INDENTATION_ERROR, or RAISE_*_KNOWN_* call
            if (_alternative.Action.Contains("RAISE_SYNTAX_ERROR") ||
                _alternative.Action.Contains("RAISE_INDENTATION_ERROR") ||
                _alternative.Action.Contains("RAISE_"))
            {
                GenerateRaiseErrorAction();
            }
            // Check if this is a _PyAST_* or _PyPegen_* function call
            else if (_alternative.Action.Contains("_PyAST_") || _alternative.Action.Contains("_PyPegen_"))
            {
                GeneratePyASTAction();
            }
            else
            {
                // Direct variable reference or expression
                var cleanAction = _alternative.Action.Trim();
                if (_variables.ContainsKey(cleanAction))
                {
                    _parent.WriteLine($"_res = ({_parent.GetRuleReturnType(_rule)})((GeneratedPtr?){_variables[cleanAction]});");
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

            if (needsSequenceWrap && action.Contains("Pass"))
            {
                Console.WriteLine($"[DEBUG Pass] Original action: [{action}]");
                Console.WriteLine($"[DEBUG Pass] needsSequenceWrap: {needsSequenceWrap}");
            }

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

            // CPython 3.12: Extract type cast if present
            // Example: (asdl_stmt_seq*)_PyPegen_singleton_seq(...) → extract "asdl_stmt_seq*"
            string typeCast = null;
            if (action.TrimStart().StartsWith("("))
            {
                var castEnd = action.IndexOf(')');
                if (castEnd > 0)
                {
                    typeCast = action.Substring(1, castEnd - 1).Trim();
                    action = action.Substring(castEnd + 1).Trim();
                }
            }

            // Extract function name and arguments
            // Example: _PyAST_If(a, b, c, EXTRA) or _PyPegen_set_expr_context(p, a, Store)
            var funcStart = action.IndexOf("_PyAST_");
            if (funcStart < 0)
            {
                // Try _PyPegen_* functions
                funcStart = action.IndexOf("_PyPegen_");
            }

            if (funcStart < 0)
            {
                _parent.WriteLine($"// No _PyAST_ or _PyPegen_ function in action: {action}");
                _parent.WriteLine($"_res = default({_parent.GetRuleReturnType(_rule)});");
                return;
            }

            var funcEnd = action.IndexOf('(', funcStart);
            var argsEnd = FindMatchingParen(action, funcEnd);

            if (funcEnd < 0 || argsEnd < 0 || funcEnd >= argsEnd)
            {
                _parent.WriteLine($"// Failed to parse action: {action}");
                _parent.WriteLine($"_res = default({_parent.GetRuleReturnType(_rule)});");
                return;
            }

            var funcName = action.Substring(funcStart, funcEnd - funcStart);
            var argsStr = action.Substring(funcEnd + 1, argsEnd - funcEnd - 1);

            if (funcName.Contains("Pass"))
            {
                Console.WriteLine($"[DEBUG Pass argsStr] Before clean: [{argsStr}]");
                Console.WriteLine($"[DEBUG Pass argsStr] funcEnd={funcEnd}, argsEnd={argsEnd}");
            }

            // Debug: Check if action is multiline
            if (funcName.Contains("BoolOp") || funcName.Contains("Compare"))
            {
                Console.WriteLine($"[DEBUG] Full action string for {funcName}:");
                Console.WriteLine($"  Length: {action.Length}");
                Console.WriteLine($"  funcStart: {funcStart}, funcEnd: {funcEnd}, argsEnd: {argsEnd}");
                Console.WriteLine($"  Substring calc: funcEnd+1={funcEnd+1}, length={argsEnd - funcEnd - 1}");
                Console.WriteLine($"  Action text: [{action}]");
                Console.WriteLine($"  Extracted argsStr length: {argsStr.Length}");
                Console.WriteLine($"  Extracted argsStr (escaped): [{argsStr.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")}]");
            }

            // Clean CPython macros from arguments
            argsStr = CleanCPythonMacros(argsStr);

            if (funcName.Contains("BoolOp") || funcName.Contains("Compare"))
            {
                Console.WriteLine($"[DEBUG] {funcName} after CleanCPythonMacros: [{argsStr}]");
            }

            // Split arguments respecting parentheses depth
            var args = SplitArgumentsRespectingParens(argsStr);

            if (funcName.Contains("BoolOp") || funcName.Contains("Compare"))
            {
                Console.WriteLine($"[DEBUG] {funcName} split args count: {args.Count}, values: [{string.Join("], [", args)}]");
            }

            // Use ActionMapper to convert to C# AST construction
            var mapper = new ActionMapper();
            var astCode = mapper.MapAction(funcName, args, _variables, _rule, typeCast);

            if (needsSequenceWrap && action.Contains("Pass"))
            {
                Console.WriteLine($"[DEBUG Pass] After MapAction, astCode: [{astCode}]");
            }

            if (astCode != null)
            {
                // CPython 3.12: If _PyPegen sequence functions are used, they already handle the wrapping/conversion
                bool isPyPegenSeqFunc = funcName.Contains("_PyPegen_singleton_seq") ||
                                       funcName.Contains("_PyPegen_seq_insert_in_front") ||
                                       funcName.Contains("_PyPegen_seq_append_to_end") ||
                                       funcName.Contains("_PyPegen_seq_flatten");

                // If the action needs a sequence wrap (singleton_seq), modify the generated code
                // BUT only for _PyAST_ functions, not _PyPegen_ functions that handle sequences themselves
                if (needsSequenceWrap && _parent.GetRuleReturnType(_rule).Contains("Seq") && !isPyPegenSeqFunc)
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
            // Debug logging
            bool debugLog = argsStr.Contains("BoolOp") || argsStr.Contains("_PyPegen_seq_insert") || argsStr.Contains("_PyPegen_get_cmpops");

            if (debugLog)
            {
                Console.WriteLine($"[CleanCPythonMacros] Input: [{argsStr.Replace("\n", "\\n")}]");
            }

            // Remove C type casts: (asdl_expr_seq*), (expr_ty), etc.
            // Pattern: (type*) or (type_ty)
            argsStr = System.Text.RegularExpressions.Regex.Replace(argsStr, @"\([a-zA-Z_][a-zA-Z0-9_]*\s*\*+\s*\)", "");
            argsStr = System.Text.RegularExpressions.Regex.Replace(argsStr, @"\([a-zA-Z_][a-zA-Z0-9_]*_ty\)", "");

            // Remove C pointer operators for specific patterns
            // b->kind → ASTHelpers.ExtractOpKind(b)
            // b->v.Name.id → ASTHelpers.ExtractStringValue(b)
            argsStr = System.Text.RegularExpressions.Regex.Replace(argsStr, @"([a-zA-Z_][a-zA-Z0-9_]*)\s*->\s*kind", "ASTHelpers.ExtractOpKind($1)");
            argsStr = System.Text.RegularExpressions.Regex.Replace(argsStr, @"([a-zA-Z_][a-zA-Z0-9_]*)\s*->\s*v\.Name\.id", "ASTHelpers.ExtractStringValue($1)");
            argsStr = System.Text.RegularExpressions.Regex.Replace(argsStr, @"([a-zA-Z_][a-zA-Z0-9_]*)\s*->\s*v\.[a-zA-Z_]+\.[a-zA-Z_]+", "ASTHelpers.ExtractStringValue($1)");
            // Generic fallback: b->something → b
            argsStr = System.Text.RegularExpressions.Regex.Replace(argsStr, @"([a-zA-Z_][a-zA-Z0-9_]*)\s*->\s*[a-zA-Z_][a-zA-Z0-9_]*", "$1");

            // Remove 'p' parameter from _PyPegen_* function calls
            // Pattern: _PyPegen_*(p, ...) → _PyPegen_*(...)
            argsStr = System.Text.RegularExpressions.Regex.Replace(argsStr, @"_PyPegen_([a-zA-Z0-9_]+)\(p,\s*", "_PyPegen_$1(");
            argsStr = System.Text.RegularExpressions.Regex.Replace(argsStr, @"_PyPegen_([a-zA-Z0-9_]+)\(p\)", "_PyPegen_$1()");

            // Remove CHECK(...) macro
            // Pattern: CHECK(type, expression) → expression
            while (argsStr.Contains("CHECK("))
            {
                var checkStart = argsStr.IndexOf("CHECK(");
                var checkEnd = FindMatchingParen(argsStr, checkStart + 5);  // +5 for "CHECK"

                if (debugLog)
                {
                    Console.WriteLine($"[CleanCPythonMacros] CHECK found at {checkStart}, matching paren at {checkEnd}");
                }

                if (checkEnd > checkStart)
                {
                    var checkContent = argsStr.Substring(checkStart + 6, checkEnd - checkStart - 6);

                    if (debugLog)
                    {
                        Console.WriteLine($"[CleanCPythonMacros] CHECK content: [{checkContent.Replace("\n", "\\n")}]");
                    }

                    // Extract the expression after the first comma
                    var commaPos = checkContent.IndexOf(',');
                    if (commaPos >= 0)
                    {
                        var expression = checkContent.Substring(commaPos + 1).Trim();
                        if (debugLog)
                        {
                            Console.WriteLine($"[CleanCPythonMacros] Extracted expression: [{expression.Replace("\n", "\\n")}]");
                        }
                        argsStr = argsStr.Substring(0, checkStart) + expression + argsStr.Substring(checkEnd + 1);
                    }
                    else
                    {
                        // No comma, remove the entire CHECK
                        argsStr = argsStr.Substring(0, checkStart) + argsStr.Substring(checkEnd + 1);
                    }

                    if (debugLog)
                    {
                        Console.WriteLine($"[CleanCPythonMacros] After CHECK removal: [{argsStr.Replace("\n", "\\n")}]");
                    }
                }
                else
                {
                    if (debugLog)
                    {
                        Console.WriteLine($"[CleanCPythonMacros] Could not find matching paren, breaking");
                    }
                    break; // Can't find matching paren, give up
                }
            }

            // Remove CHECK_NULL_ALLOWED(type, expr) → expr
            // Pattern: CHECK_NULL_ALLOWED(asdl_expr_seq*, _PyPegen_seq_extract_starred_exprs(p, a)) → _PyPegen_seq_extract_starred_exprs(p, a)
            while (argsStr.Contains("CHECK_NULL_ALLOWED("))
            {
                var checkStart = argsStr.IndexOf("CHECK_NULL_ALLOWED(");
                var checkEnd = FindMatchingParen(argsStr, checkStart + 18);  // +18 for "CHECK_NULL_ALLOWED"

                if (checkEnd > checkStart)
                {
                    var checkContent = argsStr.Substring(checkStart + 19, checkEnd - checkStart - 19);

                    // Extract the expression after the first comma (skip type)
                    var commaPos = checkContent.IndexOf(',');
                    if (commaPos >= 0)
                    {
                        var expression = checkContent.Substring(commaPos + 1).Trim();
                        argsStr = argsStr.Substring(0, checkStart) + expression + argsStr.Substring(checkEnd + 1);
                    }
                    else
                    {
                        // No comma, remove the entire CHECK_NULL_ALLOWED
                        argsStr = argsStr.Substring(0, checkStart) + argsStr.Substring(checkEnd + 1);
                    }
                }
                else
                {
                    break;
                }
            }

            // CPython: _PyPegen_singleton_seq(p, x) is a real function, not a macro!
            // It creates a sequence with a single element: asdl_seq* with one item
            // We have overloaded C# implementations in PyParserBase.cs
            // DO NOT remove this - let ActionMapper handle the translation

            // CPython: NEW_TYPE_COMMENT(p, x) → x?.Value (extract token string value)
            // TYPE_COMMENT is a token, needs .Value to get the string
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
                        // Add ?.Value to extract string from token
                        argsStr = argsStr.Substring(0, tcStart) + expression + "?.Value" + argsStr.Substring(tcEnd + 1);
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

            // Note: _PyPegen_* functions are helper functions, not macros to remove
            // They should be kept in the argument list and handled at runtime
            // Example: _PyPegen_seq_insert_in_front(p, a, b) should remain as-is

            if (debugLog)
            {
                Console.WriteLine($"[CleanCPythonMacros] Final output: [{argsStr.Replace("\n", "\\n")}]");
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

        /// <summary>
        /// Generate code for RAISE_SYNTAX_ERROR, RAISE_INDENTATION_ERROR, and RAISE_*_KNOWN_* calls
        /// CPython 3.12: These actions throw exceptions to report invalid syntax
        /// </summary>
        private void GenerateRaiseErrorAction()
        {
            var action = _alternative.Action.Trim();

            // CPython 3.12: All RAISE_* macros throw syntax errors
            // For now, just throw a generic PySyntaxError since we're focused on getting parsing to work
            // The exact error message isn't critical for our testing purposes

            // CPython 3.12: For now, use System.Exception for all syntax errors
            // TODO: Create custom PySyntaxError and PyIndentationError exception classes
            string exceptionClass = "System.Exception";

            // Try to extract the first quoted string as the error message
            // Need to handle escaped quotes like \"==\" properly
            string message = "\"invalid syntax\"";  // Default message
            var firstQuote = action.IndexOf('"');
            if (firstQuote >= 0)
            {
                // Find the closing quote, skipping over escaped quotes
                int pos = firstQuote + 1;
                while (pos < action.Length)
                {
                    if (action[pos] == '"')
                    {
                        // Found potential closing quote
                        message = action.Substring(firstQuote, pos - firstQuote + 1);
                        break;
                    }
                    else if (action[pos] == '\\' && pos + 1 < action.Length)
                    {
                        // Skip escaped character
                        pos += 2;
                    }
                    else
                    {
                        pos++;
                    }
                }
            }

            // Generate C# code to set pending error (CPython 3.12: RAISE_SYNTAX_ERROR sets error and returns NULL immediately)
            // CPython 3.12: After RAISE_SYNTAX_ERROR, no other alternatives are tried
            _parent.WriteLine($"// CPython 3.12: Invalid syntax detected - set error and return immediately");
            _parent.WriteLine($"_pendingSyntaxError = {message};");
            _parent.WriteLine($"_pendingErrorPosition = _position;");
            _parent.WriteLine($"_res = null;");
            _parent.WriteLine($"goto done;  // CPython: Skip remaining alternatives after RAISE_SYNTAX_ERROR");
        }
    }
}
