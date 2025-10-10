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

            // Add debug logging for Expression, Primary, Arguments, SimpleStmt and Assignment alternatives
            if (_rule.Name.Equals("expression", StringComparison.OrdinalIgnoreCase))
            {
                _parent.WriteLine("#if DEBUG_PARSE_LOG");
                _parent.WriteLine($"Console.WriteLine($\"[EXPRESSION-ALT{_alternativeIndex + 1}] START at pos={{_position}}\");");
                _parent.WriteLine("#endif");
            }
            if (_rule.Name.Equals("simple_stmt", StringComparison.OrdinalIgnoreCase))
            {
                _parent.WriteLine("#if DEBUG_PARSE_LOG");
                _parent.WriteLine($"Console.WriteLine($\"[SIMPLE_STMT-ALT{_alternativeIndex + 1}] START at pos={{_position}}\");");
                _parent.WriteLine("#endif");
            }
            if (_rule.Name.Equals("assignment", StringComparison.OrdinalIgnoreCase))
            {
                _parent.WriteLine("#if DEBUG_PARSE_LOG");
                _parent.WriteLine($"Console.WriteLine($\"[ASSIGNMENT-ALT{_alternativeIndex + 1}] START at pos={{_position}}\");");
                _parent.WriteLine("#endif");
            }
            if (_rule.Name.Equals("primary", StringComparison.OrdinalIgnoreCase))
            {
                _parent.WriteLine("#if DEBUG_PARSE_LOG");
                _parent.WriteLine($"Console.WriteLine($\"[PRIMARY-ALT{_alternativeIndex + 1}] START at pos={{_position}}, token={{CurrentToken?.Type}}:'{{CurrentToken?.Value}}'\");");
                _parent.WriteLine("#endif");
            }
            if (_rule.Name.Equals("arguments", StringComparison.OrdinalIgnoreCase))
            {
                _parent.WriteLine("#if DEBUG_PARSE_LOG");
                _parent.WriteLine($"Console.WriteLine($\"[ARGUMENTS-ALT{_alternativeIndex + 1}] START at pos={{_position}}\");");
                _parent.WriteLine("#endif");
            }
            // F-string parsing debug logs
            if (_rule.Name.Equals("fstring_middle", StringComparison.OrdinalIgnoreCase) ||
                _rule.Name.Equals("fstring_replacement_field", StringComparison.OrdinalIgnoreCase) ||
                _rule.Name.Equals("fstring_conversion", StringComparison.OrdinalIgnoreCase) ||
                _rule.Name.Equals("fstring_full_format_spec", StringComparison.OrdinalIgnoreCase) ||
                _rule.Name.Equals("fstring", StringComparison.OrdinalIgnoreCase))
            {
                _parent.WriteLine("#if DEBUG_FSTRING_LOG");
                _parent.WriteLine($"Console.WriteLine($\"[{_rule.Name.ToUpper()}-ALT{_alternativeIndex + 1}] START at pos={{_position}}, token={{CurrentToken?.Type}}:'{{CurrentToken?.Value}}'\");");
                _parent.WriteLine("#endif");
            }

            _parent.WriteLine();

            // CPython 3.12: Check error_indicator at start of each alternative
            _parent.WriteLine("// CPython 3.12: Check error indicator before trying alternative");

            // Add debug logging for Expression error check
            if (_rule.Name.Equals("expression", StringComparison.OrdinalIgnoreCase))
            {
                _parent.WriteLine("#if DEBUG_PARSE_LOG");
                _parent.WriteLine($"Console.WriteLine($\"[EXPRESSION-ALT{_alternativeIndex + 1}] pendingSyntaxError={{(_pendingSyntaxError == null ? \"null\" : \"SET\")}}\");");
                _parent.WriteLine("#endif");
            }

            _parent.WriteLine("if (_pendingSyntaxError != null)");
            _parent.WriteLine("{");
            _parent.Indent();

            if (_rule.Name.Equals("expression", StringComparison.OrdinalIgnoreCase))
            {
                _parent.WriteLine("#if DEBUG_PARSE_LOG");
                _parent.WriteLine($"Console.WriteLine($\"[EXPRESSION-ALT{_alternativeIndex + 1}] SKIP due to pendingSyntaxError\");");
                _parent.WriteLine("#endif");
            }

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
                    // Add debug logging for Primary and Arguments alternatives success
                    if (_rule.Name.Equals("primary", StringComparison.OrdinalIgnoreCase))
                    {
                        _parent.WriteLine($"#if DEBUG_PARSE_LOG");
                        _parent.WriteLine($"Console.WriteLine($\"[PRIMARY-ALT{_alternativeIndex + 1}] SUCCESS at pos={{_position}}\");");
                        _parent.WriteLine($"#endif");
                    }
                    if (_rule.Name.Equals("arguments", StringComparison.OrdinalIgnoreCase))
                    {
                        _parent.WriteLine($"#if DEBUG_PARSE_LOG");
                        _parent.WriteLine($"Console.WriteLine($\"[ARGUMENTS-ALT{_alternativeIndex + 1}] SUCCESS at pos={{_position}}\");");
                        _parent.WriteLine($"#endif");
                    }
                    // F-string success logs
                    if (_rule.Name.Equals("fstring_middle", StringComparison.OrdinalIgnoreCase) ||
                        _rule.Name.Equals("fstring_replacement_field", StringComparison.OrdinalIgnoreCase) ||
                        _rule.Name.Equals("fstring_conversion", StringComparison.OrdinalIgnoreCase) ||
                        _rule.Name.Equals("fstring_full_format_spec", StringComparison.OrdinalIgnoreCase) ||
                        _rule.Name.Equals("fstring", StringComparison.OrdinalIgnoreCase))
                    {
                        _parent.WriteLine("#if DEBUG_FSTRING_LOG");
                        _parent.WriteLine($"Console.WriteLine($\"[{_rule.Name.ToUpper()}-ALT{_alternativeIndex + 1}] SUCCESS at pos={{_position}}, _res={{_res?.GetType().Name ?? \"null\"}}\");");
                        _parent.WriteLine("#endif");
                    }
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

                // If this is an error recovery alternative with invalid_* rules
                // CPython 3.12: invalid_* rules set error indicator and return NULL
                // We must check if error was set and exit immediately if so
                if (hasInvalidRule)
                {
                    _parent.WriteLine("// CPython 3.12: invalid_* rule matched - check if error was set");
                    _parent.WriteLine("if (_pendingSyntaxError != null)");
                    _parent.WriteLine("{");
                    _parent.Indent();
                    _parent.WriteLine("// Error was set by invalid_* rule - exit rule immediately");
                    _parent.WriteLine("_res = null;");
                    _parent.WriteLine("goto done;");
                    _parent.Dedent();
                    _parent.WriteLine("}");
                    _parent.WriteLine("// No error set - this invalid_* rule didn't match, try next alternative");
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

                    // Special case: Converting specific Seq types to GeneratedSeq
                    // PegenHelpers.ToMixedSeq has overloads for all sequence types
                    // Let C# method overload resolution choose the right one
                    if (targetType == "GeneratedSeq")
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
            if (action.Contains("CHECK_VERSION"))
            {
                var cvStart = action.IndexOf("CHECK_VERSION");
                // Skip past "CHECK_VERSION" to find opening paren
                var cvParenStart = action.IndexOf('(', cvStart);
                if (cvParenStart >= 0)
                {
                    var cvEnd = FindMatchingParen(action, cvParenStart);
                    if (cvEnd > cvParenStart)
                    {
                        var cvContent = action.Substring(cvParenStart + 1, cvEnd - cvParenStart - 1);

                        Console.WriteLine($"[DEBUG CHECK_VERSION] Full action: {action}");
                        Console.WriteLine($"[DEBUG CHECK_VERSION] Content: {cvContent}");

                        // Split by comma respecting nested parens/quotes
                        var cvArgs = SplitArgumentsRespectingParens(cvContent);

                        Console.WriteLine($"[DEBUG CHECK_VERSION] Args count: {cvArgs.Count}");
                        for (int i = 0; i < cvArgs.Count; i++)
                        {
                            Console.WriteLine($"[DEBUG CHECK_VERSION] Arg[{i}]: {cvArgs[i]}");
                        }

                        // Last argument is the actual expression
                        if (cvArgs.Count >= 4)
                        {
                            action = cvArgs[cvArgs.Count - 1].Trim();
                            Console.WriteLine($"[DEBUG CHECK_VERSION] Extracted action: {action}");
                        }
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
            // Find the OUTERMOST (leftmost) function call
            // CPython: action may have nested calls like _PyPegen_func(CHECK<type>(_PyAST_func(...)))
            // We need to find the outermost function, not the first occurrence in string
            var funcStart = -1;
            var pyastIdx = action.IndexOf("_PyAST_");
            var pyPegenIdx = action.IndexOf("_PyPegen_");

            // Choose the leftmost (outermost) function
            if (pyastIdx >= 0 && pyPegenIdx >= 0)
            {
                funcStart = Math.Min(pyastIdx, pyPegenIdx);
            }
            else if (pyastIdx >= 0)
            {
                funcStart = pyastIdx;
            }
            else if (pyPegenIdx >= 0)
            {
                funcStart = pyPegenIdx;
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
            // CPython 3.12: If no explicit cast, use rule's return type
            if (string.IsNullOrEmpty(typeCast) && !string.IsNullOrEmpty(_rule.ReturnType))
            {
                // Check if already C# type (artificial rules use __CSHARP__: prefix)
                if (_rule.ReturnType.StartsWith("__CSHARP__:"))
                {
                    typeCast = _rule.ReturnType.Substring("__CSHARP__:".Length);
                }
                else
                {
                    typeCast = _parent.TranslatePegTypeToCS(_rule.ReturnType);
                }
            }
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

            // CPython 3.12: All C→C# conversions are done in python_cs.gram
            // No runtime transformations needed - grammar already contains C# code

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

            // CPython 3.12: python_cs.gram contains C# action code directly
            // No transformation needed - just use the action as-is

            // Check if this is a conditional error (contains ternary operator)
            if (action.Contains("?") && action.Contains(":"))
            {
                // Pattern: CheckLegacyStmt(a) ? RaiseSyntaxErrorKnownRange(...) : null
                if (action.Contains("CheckLegacyStmt"))
                {
                    GenerateConditionalRaiseAction(action);
                    return;
                }
            }

            // Unconditional RAISE_SYNTAX_ERROR
            // Try to extract the first quoted string as the error message
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

        /// <summary>
        /// Generate conditional RAISE_SYNTAX_ERROR based on CheckLegacyStmt
        /// CPython 3.12: Two patterns:
        ///   Pattern 1: check ? RAISE_ERROR : null (invalid_legacy_expression)
        ///   Pattern 2: check ? null : RAISE_ERROR (invalid_expression)
        /// </summary>
        /// <param name="action">C# action string from python_cs.gram</param>
        private void GenerateConditionalRaiseAction(string action)
        {
            // python_cs.gram action: CheckLegacyStmt(a) ? RaiseSyntaxErrorKnownRange(...) : null
            // OR: CheckLegacyStmt(a) ? null : other_conditions ? null : RaiseSyntaxErrorKnownRange(...)

            // CPython 3.12 approach: Just execute the ternary expression directly
            // If RaiseSyntaxErrorKnownRange is called, it will throw
            // If null is returned, alternative fails gracefully

            _parent.WriteLine($"// CPython: Conditional error - execute C# ternary expression");
            _parent.WriteLine($"try");
            _parent.WriteLine($"{{");
            _parent.Indent();
            _parent.WriteLine($"_res = {action};");
            _parent.WriteLine($"// If we reach here, ternary returned null (no error)");
            _parent.WriteLine($"goto done;");
            _parent.Dedent();
            _parent.WriteLine($"}}");
            _parent.WriteLine($"catch (PySyntaxErrorException ex)");
            _parent.WriteLine($"{{");
            _parent.Indent();
            _parent.WriteLine($"// CPython: Error was raised - set pending error and fail alternative");
            _parent.WriteLine($"_pendingSyntaxError = ex.Message;");
            _parent.WriteLine($"_pendingErrorPosition = _position;");
            _parent.WriteLine($"_res = null;");
            _parent.WriteLine($"goto done;");
            _parent.Dedent();
            _parent.WriteLine($"}}");
        }
    }
}
