using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SharpPy.Generated;
// using SharpPy.Tokenizer.Generated; // Now using SharpPy.Generated
using SharpPy.Utils;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 compatible generated parser bridge
    /// Uses auto-generated tokenizer and parser from Grammar/python.gram
    /// </summary>
    public static class GeneratedParserBridge
    {
        /// <summary>
        /// Main parsing entry point - uses auto-generated CPython 3.12 compatible tokenizer and parser
        /// </summary>
        public static List<Statement> ParseSource(string source, string filename = "<string>")
        {
#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] GeneratedParserBridge: Using auto-generated CPython 3.12 tokenizer + parser for {filename}");
#endif

            // Use generated tokenizer
            var tokenizer = new GeneratedPyTokenizer(source, filename);
            var generatedTokens = tokenizer.Tokenize();

            // Use generated parser
            var parser = new GeneratedPyParser(generatedTokens, filename);
            var parseResult = parser.ParseFile();

#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] Parse result type: {parseResult?.GetType()?.Name ?? "null"}");
            if (parseResult != null)
            {
                Console.WriteLine($"[DEBUG] Parse result toString: {parseResult}");
            }
#endif

            // Convert generated AST to SharpPy AST
            return ConvertToSharpPyAST(parseResult, filename);
        }


        /// <summary>
        /// Convert generated parser result to SharpPy AST
        /// This is where CPython 3.12 AST compatibility is implemented
        /// </summary>
        private static List<Statement> ConvertToSharpPyAST(object? parseResult, string filename)
        {
            if (parseResult == null)
            {
                return new List<Statement>();
            }

            // Phase 2: Start implementing AST conversion
            return ConvertGeneratedAST(parseResult, filename);
        }

        /// <summary>
        /// Core AST conversion logic - converts Generated AST nodes to SharpPy AST
        /// </summary>
        private static List<Statement> ConvertGeneratedAST(object parseResult, string filename)
        {
            var statements = new List<Statement>();

            switch (parseResult)
            {
                case GeneratedModule module:
                    // Convert module to statement list
                    statements.AddRange(ConvertModuleBody(module));
                    break;

                case GeneratedStmtSeq stmtSeq:
                    // Convert statement sequence
                    foreach (var stmt in stmtSeq)
                    {
                        var converted = ConvertStatement(stmt, false, false);
                        if (converted != null)
                            statements.Add(converted);
                    }
                    break;

                case GeneratedStmt stmt:
                    // Single statement
                    var convertedStmt = ConvertStatement(stmt, false, false);
                    if (convertedStmt != null)
                        statements.Add(convertedStmt);
                    break;

                default:
                    // Fallback for unhandled types
                    Console.WriteLine($"[DEBUG] ConvertGeneratedAST: Unhandled type {parseResult.GetType()}");
                    break;
            }

            return statements;
        }

        /// <summary>
        /// Convert Generated module body to statement list
        /// </summary>
        private static List<Statement> ConvertModuleBody(GeneratedModule module)
        {
            var statements = new List<Statement>();

            if (module.Body != null)
            {
                // Detect and merge chain assignments
                var moduleStmts = module.Body.ToList();
                for (int i = 0; i < moduleStmts.Count; i++)
                {
                    var stmt = moduleStmts[i];

                    if (stmt.StatementType == "assignment")
                    {
                        // Look for chain assignment pattern
                        var chainGroup = DetectChainAssignment(moduleStmts, i);

#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] Chain assignment detection: Index {i}, ChainGroup.Count = {chainGroup.Count}");
                        for (int j = 0; j < chainGroup.Count; j++)
                        {
                            var chainStmt = chainGroup[j];
                            var data = chainStmt.Value as dynamic;
                            Console.WriteLine($"[DEBUG]   Chain[{j}]: Target='{data?.Target}', Value='{data?.Value}'");
                        }
#endif

                        if (chainGroup.Count > 1)
                        {
                            // Convert chain assignment
                            var chainStmt = ConvertChainAssignment(chainGroup);
                            if (chainStmt != null)
                            {
#if DEBUG_LOG
                                Console.WriteLine($"[DEBUG] Created ChainedAssignStatement with {((ChainedAssignStatement)chainStmt).Targets.Count} targets");
#endif
                                statements.Add(chainStmt);
                            }

                            // Skip the statements we just processed
                            i += chainGroup.Count - 1;
                            continue;
                        }
                    }

                    // Regular statement conversion
                    var converted = ConvertStatement(stmt, false, false);
                    if (converted != null)
                        statements.Add(converted);
                }
            }

            return statements;
        }

        /// <summary>
        /// Detect chain assignment pattern (a = b = c = value)
        /// </summary>
        private static List<GeneratedStmt> DetectChainAssignment(List<GeneratedStmt> statements, int startIndex)
        {
            var chainGroup = new List<GeneratedStmt>();
            var currentIndex = startIndex;

            // Find the end of the chain by looking for assignments where value is not a name
            while (currentIndex < statements.Count && statements[currentIndex].StatementType == "assignment")
            {
                var stmt = statements[currentIndex];
                var assignmentData = stmt.Value as dynamic;
                var valueExpr = assignmentData?.Value;

                chainGroup.Add(stmt);

                // If value is not a name reference, this is the end of the chain
                if (valueExpr != null)
                {
                    dynamic valueDynamic = valueExpr;
                    if (valueDynamic.type?.ToString() != "name")
                    {
                        break; // Found the actual value, end of chain
                    }
                }

                currentIndex++;
            }

            return chainGroup;
        }

        /// <summary>
        /// Convert chain assignment to a single statement
        /// </summary>
        private static Statement? ConvertChainAssignment(List<GeneratedStmt> chainGroup)
        {
            if (chainGroup.Count == 0) return null;

            // Find the statement with the actual value (not a name reference)
            GeneratedStmt? valueStmt = null;
            var targetNames = new List<string>();

            // First pass: collect all target names
            for (int i = 0; i < chainGroup.Count; i++)
            {
                var stmt = chainGroup[i];
                var assignmentData = stmt.Value as dynamic;
                var target = assignmentData?.Target;

                if (target != null)
                {
                    dynamic targetDynamic = target;
                    if (targetDynamic.type?.ToString() == "name")
                    {
                        var targetName = targetDynamic.value?.ToString();
                        if (!string.IsNullOrEmpty(targetName))
                        {
                            targetNames.Add(targetName);
#if DEBUG_LOG
                            Console.WriteLine($"[DEBUG] Added target name: '{targetName}', Total targets: {targetNames.Count}");
#endif
                        }
                    }
                }
            }

            // Second pass: find the actual value (in the last statement)
            var lastStmt = chainGroup[chainGroup.Count - 1];
            var lastAssignmentData = lastStmt.Value as dynamic;
            var lastValue = lastAssignmentData?.Value;

#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] Last statement value: '{lastValue}', ValueType='{(lastValue != null ? (lastValue as dynamic).type?.ToString() : "null")}'");
#endif

            if (lastValue != null)
            {
                dynamic lastValueDynamic = lastValue;
                if (lastValueDynamic.type?.ToString() != "name")
                {
                    valueStmt = lastStmt;
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] Found value statement at last position");
#endif
                }
            }

#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] ConvertChainAssignment: ValueStmt={valueStmt != null}, TargetNames.Count={targetNames.Count}");
            Console.WriteLine($"[DEBUG] Target names: [{string.Join(", ", targetNames)}]");
#endif

            if (valueStmt != null && targetNames.Count > 1)
            {
                // Create a chain assignment statement
                var assignmentData = valueStmt.Value as dynamic;
                var valueExpr = assignmentData?.Value;

                if (valueExpr != null)
                {
                    Expression convertedValueExpr = ConvertAnyExpression(valueExpr);

                    // Convert target names to NameExpression objects
                    var targetExpressions = targetNames.Select(name => (Expression)new NameExpression(name)).ToList();

                    return new ChainedAssignStatement(targetExpressions, convertedValueExpr);
                }
            }

            return null;
        }

        /// <summary>
        /// Convert Generated statement to SharpPy statement
        /// </summary>
        private static Statement? ConvertStatement(GeneratedStmt stmt, bool insideLoop = false, bool insideFunction = false)
        {
#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] ConvertStatement: Converting statement type '{stmt.StatementType}' (insideLoop: {insideLoop})");
#endif

            switch (stmt.StatementType)
            {
                case "pass":
                    // Pass statement - represented as expression statement with None
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "break":
                    // Break statement - only valid inside loops
                    if (!insideLoop)
                    {
                        throw new PythonException(new PySyntaxError("'break' outside loop"));
                    }
                    return new BreakStatement();

                case "continue":
                    // Continue statement - only valid inside loops
                    if (!insideLoop)
                    {
                        throw new PythonException(new PySyntaxError("'continue' not properly in loop"));
                    }
                    return new ContinueStatement();

                case "assignment":
                    // Assignment statement (name = value)
                    if (stmt.Value != null)
                    {
                        var assignmentData = stmt.Value as dynamic;
                        var target = assignmentData?.Target;
                        var valueExpr = assignmentData?.Value;

#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Target='{target}', Value='{valueExpr}', ValueType={valueExpr?.GetType()}");
#endif

                        if (target != null && valueExpr != null)
                        {
                            // Extract variable name from target object
                            string? targetName = null;

                            // Handle target object with { type = "name", value = "variable_name" } structure
                            if (target is object targetObj)
                            {
                                var targetType = targetObj.GetType();
                                var valueProperty = targetType.GetProperty("value");
                                if (valueProperty != null)
                                {
                                    targetName = valueProperty.GetValue(targetObj)?.ToString();
                                }
                            }

                            // Fallback to toString() if property extraction fails
                            if (string.IsNullOrEmpty(targetName))
                            {
                                targetName = target.ToString();
                            }

#if DEBUG_LOG
                            Console.WriteLine($"[DEBUG] ConvertStatement Assignment Final: Target='{targetName}', Value='{valueExpr}', ValueType={valueExpr?.GetType()}");
#endif

                            if (!string.IsNullOrEmpty(targetName))
                            {
                                // Convert the value expression using ConvertAnyExpression
                                Expression convertedValueExpr = ConvertAnyExpression(valueExpr);
                                return new AssignStatement(targetName, convertedValueExpr);
                            }
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "expression":
                    // Expression statement (standalone expression)
                    if (stmt.Value != null)
                    {
                        // Convert any expression using ConvertAnyExpression
                        var expression = ConvertAnyExpression(stmt.Value);
                        return new ExpressionStatement(expression);

                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "return":
                    // Return statement (return [expression]) - only valid inside functions
                    if (!insideFunction)
                    {
                        throw new PythonException(new PySyntaxError("'return' outside function"));
                    }

                    Expression? returnValue = null;

                    if (stmt.Value != null)
                    {
                        var returnData = stmt.Value as dynamic;
                        if (returnData?.value != null)
                        {
                            // Convert the return value expression using ConvertAnyExpression
                            returnValue = ConvertAnyExpression(returnData.value);
                        }
                    }

                    // If no value provided, return None
                    returnValue ??= new ConstantExpression(PyNone.Instance);

                    return new ReturnStatement(returnValue);

                case "if":
                    // If statement (if condition: body)
                    if (stmt.Value != null)
                    {
                        var ifData = stmt.Value as dynamic;

                        // Convert condition expression
                        Expression conditionExpr = ConvertAnyExpression(ifData.condition);

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        if (ifData.body != null)
                        {
                            foreach (var bodyStmt in ifData.body)
                            {
                                var convertedStmt = ConvertStatement(bodyStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    bodyStmts.Add(convertedStmt);
                            }
                        }

                        // For now, create empty else clause (TODO: handle elif/else)
                        var elseStmts = new List<Statement>();

                        return new IfStatement(conditionExpr, bodyStmts, elseStmts);
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "while":
                    // While statement (while condition: body [else: elseBody])
                    if (stmt.Value != null)
                    {
                        var whileData = stmt.Value as dynamic;

                        // Convert condition expression
                        Expression conditionExpr = ConvertAnyExpression(whileData.condition);

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        if (whileData.body != null)
                        {
                            foreach (var bodyStmt in whileData.body)
                            {
                                var convertedStmt = ConvertStatement(bodyStmt, true, insideFunction); // insideLoop = true
                                if (convertedStmt != null)
                                    bodyStmts.Add(convertedStmt);
                            }
                        }

                        // Convert optional else statements (Python while-else construct)
                        var elseStmts = new List<Statement>();
                        if (whileData.elseBody != null)
                        {
                            foreach (var elseStmt in whileData.elseBody)
                            {
                                var convertedStmt = ConvertStatement(elseStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    elseStmts.Add(convertedStmt);
                            }
                        }

                        return new WhileStatement(conditionExpr, bodyStmts, elseStmts);
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "for":
                    // For statement (for target in iterable: body [else: elseBody])
                    if (stmt.Value != null)
                    {
                        var forData = stmt.Value as dynamic;

                        // Extract target variable name
                        string targetVar = forData.target?.ToString() ?? "i";

                        // Convert iterable expression
                        Expression iterableExpr = ConvertAnyExpression(forData.iterable);

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        if (forData.body != null)
                        {
                            foreach (var bodyStmt in forData.body)
                            {
                                var convertedStmt = ConvertStatement(bodyStmt, true, insideFunction); // insideLoop = true
                                if (convertedStmt != null)
                                    bodyStmts.Add(convertedStmt);
                            }
                        }

                        // Convert optional else statements (Python for-else construct)
                        var elseStmts = new List<Statement>();
                        if (forData.elseBody != null)
                        {
                            foreach (var elseStmt in forData.elseBody)
                            {
                                var convertedStmt = ConvertStatement(elseStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    elseStmts.Add(convertedStmt);
                            }
                        }

                        return new ForStatement(targetVar, iterableExpr, bodyStmts, elseStmts);
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "async_for":
                    // Async for statement (async for target in iterable: body [else: elseBody])
                    if (stmt.Value != null)
                    {
                        var asyncForData = stmt.Value as dynamic;

                        // Extract target variable name
                        string targetVar = asyncForData.target?.ToString() ?? "i";

                        // Convert iterable expression
                        Expression iterableExpr = ConvertAnyExpression(asyncForData.iterable);

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        if (asyncForData.body != null)
                        {
                            foreach (var bodyStmt in asyncForData.body)
                            {
                                var convertedStmt = ConvertStatement(bodyStmt, true, insideFunction); // insideLoop = true
                                if (convertedStmt != null)
                                    bodyStmts.Add(convertedStmt);
                            }
                        }

                        // Convert optional else statements
                        var elseStmts = new List<Statement>();
                        if (asyncForData.elseBody != null)
                        {
                            foreach (var elseStmt in asyncForData.elseBody)
                            {
                                var convertedStmt = ConvertStatement(elseStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    elseStmts.Add(convertedStmt);
                            }
                        }

                        return new AsyncForStatement(targetVar, iterableExpr, bodyStmts, elseStmts);
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "try":
                    // Try statement (try: body except: handler)
                    if (stmt.Value != null)
                    {
                        var tryData = stmt.Value as dynamic;

                        // Convert try body statements
                        var tryBodyStmts = new List<Statement>();
                        if (tryData.body != null)
                        {
                            foreach (var bodyStmt in tryData.body)
                            {
                                var convertedStmt = ConvertStatement(bodyStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    tryBodyStmts.Add(convertedStmt);
                            }
                        }

                        // Convert except blocks
                        var exceptHandlers = new List<ExceptHandler>();
                        if (tryData.exceptBlocks != null)
                        {
                            foreach (var exceptBlock in tryData.exceptBlocks)
                            {
                                var exceptData = exceptBlock as dynamic;

                                // Convert except body statements
                                var exceptBodyStmts = new List<Statement>();
                                if (exceptData.body != null)
                                {
                                    foreach (var exceptStmt in exceptData.body)
                                    {
                                        var convertedStmt = ConvertStatement(exceptStmt, insideLoop, insideFunction);
                                        if (convertedStmt != null)
                                            exceptBodyStmts.Add(convertedStmt);
                                    }
                                }

                                // For now, create a catch-all exception handler (no specific type)
                                // TODO: Implement proper exception type parsing
                                Expression? exceptionTypeExpr = null;
                                string? variableName = null;
                                exceptHandlers.Add(new ExceptHandler(exceptionTypeExpr, variableName, exceptBodyStmts));
                            }
                        }

                        // For now, no else or finally blocks (TODO: implement later)
                        var elseStmts = new List<Statement>();
                        var finallyStmts = new List<Statement>();

                        return new TryStatement(tryBodyStmts, exceptHandlers, elseStmts, finallyStmts);
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "function_def":
                    // Function definition (def name(): body)
                    if (stmt.Value is GeneratedFunctionDef funcData)
                    {
                        var name = funcData.Name;

                        if (!string.IsNullOrEmpty(name))
                        {
                            // Create parameter list (empty for now, TODO: parse arguments)
                            var parameters = new List<string>();

                            // Create body statements (for now, simple pass statement)
                            var bodyStmts = new List<Statement>
                            {
                                new ExpressionStatement(new ConstantExpression(PyNone.Instance))
                            };

                            return new FunctionDefStatement(name, parameters, bodyStmts);
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "async_function_def":
                    // Async function definition (async def name(): body)
                    if (stmt.Value is GeneratedFunctionDef asyncFuncData)
                    {
                        var name = asyncFuncData.Name;

                        if (!string.IsNullOrEmpty(name))
                        {
                            // Create parameter list (empty for now, TODO: parse arguments)
                            var parameters = new List<string>();

                            // Create body statements (for now, simple pass statement)
                            var bodyStmts = new List<Statement>
                            {
                                new ExpressionStatement(new ConstantExpression(PyNone.Instance))
                            };

                            return new AsyncFunctionDefStatement(name, parameters, bodyStmts);
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "class_def":
                    // Class definition (class name: body)
                    if (stmt.Value != null)
                    {
                        var classData = stmt.Value as dynamic;
                        var name = classData?.Name as string;
                        var body = classData?.Body as string;

                        if (!string.IsNullOrEmpty(name) && body == "pass")
                        {
                            // Create base classes list (empty for now)
                            var bases = new List<Expression>();

                            // Create body statements
                            var bodyStmts = new List<Statement>
                            {
                                new ExpressionStatement(new ConstantExpression(PyNone.Instance))
                            };

                            return new ClassDefStatement(name, bases, bodyStmts);
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));


                case "global":
                    // Global statement (global var1, var2, ...)
                    if (stmt.Value != null)
                    {
                        var names = stmt.Value as string[];
                        if (names != null && names.Length > 0)
                        {
                            return new GlobalStatement(names.ToList());
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "nonlocal":
                    // Nonlocal statement (nonlocal var1, var2, ...)
                    if (stmt.Value != null)
                    {
                        var names = stmt.Value as string[];
                        if (names != null && names.Length > 0)
                        {
                            return new NonlocalStatement(names.ToList());
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "del":
                    // Delete statement (del var)
                    if (stmt.Value != null)
                    {
                        var targetName = stmt.Value as string;
                        if (!string.IsNullOrEmpty(targetName))
                        {
                            var targets = new List<Expression>
                            {
                                new NameExpression(targetName)
                            };
                            return new DeleteStatement(targets);
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "import":
                    // Import statement (import module)
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] Import case triggered");
                    Console.WriteLine($"  stmt.ImportModules: {stmt.ImportModules}");
                    Console.WriteLine($"  stmt.ImportModules type: {stmt.ImportModules?.GetType()}");
                    if (stmt.ImportModules != null)
                    {
                        Console.WriteLine($"  ImportModules count: {((List<object>)stmt.ImportModules).Count}");
                        foreach (var module in (List<object>)stmt.ImportModules)
                        {
                            Console.WriteLine($"  Module: {module}");
                        }
                    }
#endif
                    if (stmt.ImportModules != null && stmt.ImportModules is List<object> modules && modules.Count > 0)
                    {
                        var names = new List<string>();
                        foreach (var module in modules)
                        {
                            string moduleName = "";

                            // Try to extract module name from dynamic object
                            try
                            {
                                var moduleObj = module as dynamic;
                                if (moduleObj?.name != null)
                                {
                                    moduleName = moduleObj.name.ToString();
                                }
                                else
                                {
                                    moduleName = module.ToString();
                                }
                            }
                            catch
                            {
                                moduleName = module.ToString();
                            }

                            if (!string.IsNullOrEmpty(moduleName) && moduleName != "{ name = , asname =  }")
                            {
                                names.Add(moduleName);
                            }
                        }

                        if (names.Count > 0)
                        {
#if DEBUG_LOG
                            Console.WriteLine($"[DEBUG] Import: Creating ImportStatement with {names.Count} modules: {string.Join(", ", names)}");
#endif
                            return new ImportStatement(names);
                        }
                    }
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] Import: No valid modules found - returning fallback");
#endif
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "from_import":
                    // From import statement (from module import name)
                    if (stmt.Value != null)
                    {
                        var fromImportData = stmt.Value as dynamic;
                        var module = fromImportData?.Module as string;
                        var name = fromImportData?.Name as string;

                        if (!string.IsNullOrEmpty(module) && !string.IsNullOrEmpty(name))
                        {
                            var names = new List<string> { name };
                            return new ImportFromStatement(module, names);
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "with":
                    // With statement (with context_expr [as target]: body)
                    if (stmt.Value != null)
                    {
                        var withData = stmt.Value as dynamic;

                        // Create with items
                        var items = new List<WithItem>();
                        if (withData.items != null)
                        {
                            foreach (var item in withData.items)
                            {
                                var itemData = item as dynamic;
                                var contextExpr = ConvertAnyExpression(itemData.context_expr);
                                Expression? optionalVars = itemData.optional_vars != null ?
                                    ConvertAnyExpression(itemData.optional_vars) : null;

                                items.Add(new WithItem(contextExpr, optionalVars));
                            }
                        }
                        else
                        {
                            // Single context expression
                            var contextExpr = ConvertAnyExpression(withData.context_expr ?? withData);
                            items.Add(new WithItem(contextExpr, null));
                        }

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        if (withData.body != null)
                        {
                            foreach (var bodyStmt in withData.body)
                            {
                                var convertedStmt = ConvertStatement(bodyStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    bodyStmts.Add(convertedStmt);
                            }
                        }

                        return new WithStatement(items, bodyStmts);
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "async_with":
                    // Async with statement (async with context_expr [as target]: body)
                    if (stmt.Value != null)
                    {
                        var asyncWithData = stmt.Value as dynamic;

                        // Create with items
                        var items = new List<WithItem>();
                        if (asyncWithData.items != null)
                        {
                            foreach (var item in asyncWithData.items)
                            {
                                var itemData = item as dynamic;
                                var contextExpr = ConvertAnyExpression(itemData.context_expr);
                                Expression? optionalVars = itemData.optional_vars != null ?
                                    ConvertAnyExpression(itemData.optional_vars) : null;

                                items.Add(new WithItem(contextExpr, optionalVars));
                            }
                        }
                        else
                        {
                            // Single context expression
                            var contextExpr = ConvertAnyExpression(asyncWithData.context_expr ?? asyncWithData);
                            items.Add(new WithItem(contextExpr, null));
                        }

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        if (asyncWithData.body != null)
                        {
                            foreach (var bodyStmt in asyncWithData.body)
                            {
                                var convertedStmt = ConvertStatement(bodyStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    bodyStmts.Add(convertedStmt);
                            }
                        }

                        return new AsyncWithStatement(items, bodyStmts);
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "match_stmt":
                    // Match statement (match subject: case pattern: body)
                    if (stmt.Value != null)
                    {
                        var matchData = stmt.Value as dynamic;

                        // Convert subject expression
                        var subject = ConvertAnyExpression(matchData.subject);

                        // Convert match cases
                        var cases = new List<MatchCase>();
                        if (matchData.cases != null)
                        {
                            foreach (var caseItem in matchData.cases)
                            {
                                var caseData = caseItem as dynamic;

                                // Convert pattern (simplified for now)
                                var pattern = ConvertAnyExpression(caseData.pattern);

                                // Convert guard (optional)
                                Expression? guard = caseData.guard != null ?
                                    ConvertAnyExpression(caseData.guard) : null;

                                // Convert body statements
                                var caseBodyStmts = new List<Statement>();
                                if (caseData.body != null)
                                {
                                    foreach (var bodyStmt in caseData.body)
                                    {
#if DEBUG_LOG
                                        Console.WriteLine($"[DEBUG] Match case body statement type: {bodyStmt?.GetType()?.Name}");
                                        Console.WriteLine($"[DEBUG] Match case body statement value: {bodyStmt}");
#endif
                                        // Handle different body types
                                        if (bodyStmt is GeneratedStmt generatedStmt)
                                        {
                                            var convertedStmt = ConvertStatement(generatedStmt, insideLoop, insideFunction);
                                            if (convertedStmt != null)
                                                caseBodyStmts.Add(convertedStmt);
                                        }
                                        else if (bodyStmt is IList<object> bodyList)
                                        {
#if DEBUG_LOG
                                            Console.WriteLine($"[DEBUG] Converting List body with {bodyList.Count} statements");
#endif
                                            // Convert each statement in the list
                                            foreach (var listItem in bodyList)
                                            {
                                                if (listItem is GeneratedStmt genStmt)
                                                {
                                                    var convertedStmt = ConvertStatement(genStmt, insideLoop, insideFunction);
                                                    if (convertedStmt != null)
                                                        caseBodyStmts.Add(convertedStmt);
                                                }
                                                else
                                                {
#if DEBUG_LOG
                                                    Console.WriteLine($"[DEBUG] List item not GeneratedStmt: {listItem?.GetType()?.Name}");
#endif
                                                }
                                            }
                                        }
                                        else
                                        {
#if DEBUG_LOG
                                            Console.WriteLine($"[DEBUG] Skipping unsupported body statement type: {bodyStmt?.GetType()?.Name}");
#endif
                                            // For now, create a simple pass statement as fallback
                                            caseBodyStmts.Add(new ExpressionStatement(new ConstantExpression(PyNone.Instance)));
                                        }
                                    }
                                }

                                cases.Add(new MatchCase(pattern, caseBodyStmts, guard));
                            }
                        }

                        return new MatchStatement(subject, cases);
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                default:
                    // Fallback for unhandled statement types
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] ConvertStatement: Unhandled statement type '{stmt.StatementType}'");
#endif
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
            }
        }

        /// <summary>
        /// Convert Generated expression to SharpPy expression
        /// </summary>
        /// <summary>
        /// Convert binary operation from parser to SharpPy binary expression
        /// </summary>
        private static Expression ConvertBinaryOperation(dynamic binOp)
        {
            // Extract operator and operands
            string op = binOp.op.ToString();
            dynamic left = binOp.left;
            dynamic right = binOp.right;

            // Convert left operand
            Expression leftExpr = ConvertAnyExpression(left);

            // Convert right operand
            Expression rightExpr = ConvertAnyExpression(right);

            // BinaryOpExpression takes string operator directly
            return new BinaryOpExpression(leftExpr, op, rightExpr);
        }

        /// <summary>
        /// Convert comparison operation from parser to SharpPy comparison expression
        /// </summary>
        private static Expression ConvertComparisonOperation(dynamic compareOp)
        {
            // Extract operator and operands
            string op = compareOp.op.ToString();
            dynamic left = compareOp.left;
            dynamic right = compareOp.right;

            // Convert left operand
            Expression leftExpr = ConvertAnyExpression(left);

            // Convert right operand
            Expression rightExpr = ConvertAnyExpression(right);

            // CompareExpression takes string operator directly
            return new CompareExpression(leftExpr, op, rightExpr);
        }

        /// <summary>
        /// Convert chained comparison operation (x < 5 > 3) from parser to SharpPy chained comparison expression
        /// </summary>
        private static Expression ConvertChainedComparisonOperation(dynamic chainedCompareOp)
        {
            // Extract left operand
            dynamic left = chainedCompareOp.left;
            Expression leftExpr = ConvertAnyExpression(left);

            // Extract operators array
            string[] ops = ((object[])chainedCompareOp.ops).Cast<string>().ToArray();

            // Extract comparators array and convert each
            object[] comparators = (object[])chainedCompareOp.comparators;
            Expression[] comparatorExprs = comparators.Select(comp => ConvertAnyExpression(comp)).ToArray();

            // Create ChainedCompareExpression for CPython-compatible bytecode generation
            return new ChainedCompareExpression(
                leftExpr,
                ops.ToList(),
                comparatorExprs.ToList()
            );
        }

        /// <summary>
        /// Convert function call operation from parser to SharpPy call expression
        /// </summary>
        private static Expression ConvertCallOperation(dynamic callOp)
        {
            // Extract function and arguments
            var func = callOp.func;
            var args = callOp.args;

            // Convert function expression
            Expression functionExpr = ConvertAnyExpression(func);

            // Convert arguments
            var argExprs = new List<Expression>();
            if (args != null)
            {
                foreach (var arg in args)
                {
                    var argExpr = ConvertAnyExpression(arg);
                    argExprs.Add(argExpr);
                }
            }

            // Create call expression
            return new CallExpression(functionExpr, argExprs);
        }

        private static Expression ConvertAttributeAccess(dynamic attrExpr)
        {
            // Extract value and attribute name from { type = "attribute", value = <obj>, attr = <name> }
            var value = attrExpr.value;
            var attr = attrExpr.attr.ToString();

            Console.WriteLine($"[DEBUG] ConvertAttributeAccess: attr='{attr}', value type={value.GetType().Name}");

            // Convert the base object expression
            Expression valueExpr = ConvertAnyExpression(value);

            return new AttributeExpression(valueExpr, attr);
        }

        private static Expression ConvertSubscriptAccess(dynamic subscriptExpr)
        {
            // Extract value and slice from { type = "subscript", value = <obj>, slice = <index> }
            var value = subscriptExpr.value;
            var slice = subscriptExpr.slice;

            Console.WriteLine($"[DEBUG] ConvertSubscriptAccess: slice type={slice.GetType().Name}, value type={value.GetType().Name}");

            // Convert the base object expression
            Expression valueExpr = ConvertAnyExpression(value);

            // Convert the slice/index expression
            Expression sliceExpr = ConvertAnyExpression(slice);

            return new SubscriptExpression(valueExpr, sliceExpr);
        }

        /// <summary>
        /// Convert list literal expression
        /// </summary>
        private static Expression ConvertListLiteral(dynamic expr)
        {
            var elements = expr.elements;
            var convertedElements = new List<Expression>();

            if (elements != null)
            {
                foreach (var element in elements)
                {
                    convertedElements.Add(ConvertDynamicToExpression(element));
                }
            }

            // Create a list literal expression that will compile to BUILD_LIST bytecode
            return new ListExpression(convertedElements);
        }

        /// <summary>
        /// Convert tuple literal expression
        /// </summary>
        private static Expression ConvertTupleLiteral(dynamic expr)
        {
            var elements = expr.elements;
            var convertedElements = new List<Expression>();

            if (elements != null)
            {
                foreach (var element in elements)
                {
                    convertedElements.Add(ConvertAnyExpression(element));
                }
            }

            // Create a tuple literal expression that will compile to BUILD_TUPLE bytecode
            return new TupleExpression(convertedElements);
        }

        /// <summary>
        /// Convert any dynamic expression object to Expression
        /// </summary>
        private static Expression ConvertAnyExpression(dynamic expr)
        {
            string type = expr.type.ToString();

            return type switch
            {
                "number" => int.TryParse(expr.value.ToString(), out int intVal)
                    ? new ConstantExpression(new PyInt(intVal))
                    : double.TryParse(expr.value.ToString(), out double doubleVal)
                        ? new ConstantExpression(new PyFloat(doubleVal))
                        : throw new InvalidOperationException($"Invalid number: {expr.value}"),

                "name" => new NameExpression(expr.value.ToString()),

                "string" => new ConstantExpression(new PyString(expr.value.ToString().Trim('"'))),

                "binop" => ConvertBinaryOperation(expr),

                "compare" => ConvertComparisonOperation(expr),

                "chained_compare" => ConvertChainedComparisonOperation(expr),

                "call" => ConvertCallOperation(expr),

                "attribute" => ConvertAttributeAccess(expr),

                "subscript" => ConvertSubscriptAccess(expr),

                "list" => ConvertListLiteral(expr),

                "tuple" => ConvertTupleLiteral(expr),

                "lambda" => ConvertLambdaExpression(expr),

                "listcomp" => ConvertListComprehension(expr),

                "dictcomp" => ConvertDictComprehension(expr),

                "setcomp" => ConvertSetComprehension(expr),

                "genexp" => ConvertGeneratorExpression(expr),

                "dict" => ConvertDictLiteral(expr),

                "set" => ConvertSetLiteral(expr),

                _ => throw new NotSupportedException($"Unsupported expression type: {type}")
            };
        }

        /// <summary>
        /// Convert lambda expression to SharpPy lambda expression
        /// </summary>
        private static Expression ConvertLambdaExpression(dynamic expr)
        {
            // Extract lambda parameters and body
            var parameters = expr.parameters as List<string> ?? new List<string>();
            var body = expr.body;

            // Convert lambda body expression using the dynamic object converter
            var bodyExpr = ConvertDynamicToExpression(body);

            // Create lambda expression with parameter names and body
            return new LambdaExpression(parameters, bodyExpr);
        }

        /// <summary>
        /// Convert dynamic objects (from parser) to SharpPy expressions
        /// </summary>
        private static Expression ConvertDynamicToExpression(dynamic expr)
        {
            if (expr == null) return new ConstantExpression(PyNone.Instance);

            var type = expr.type as string;
            if (string.IsNullOrEmpty(type))
            {
                return new ConstantExpression(PyNone.Instance);
            }

            return type switch
            {
                "name" => new NameExpression(expr.value as string ?? ""),
                "number" => new ConstantExpression(new PyInt((int)Convert.ToInt64(expr.value ?? 0))),
                "string" => new ConstantExpression(new PyString(expr.value as string ?? "")),
                "binop" => ConvertBinaryOperation(expr),
                "list" => ConvertListLiteral(expr),
                "call" => ConvertCallOperation(expr),
                "slice" => ConvertSliceExpression(expr),
                "starred" => ConvertStarredExpression(expr),
                "namedexpr" => ConvertNamedExpression(expr),
                "subscript" => ConvertSubscriptExpression(expr),
                "tuple" => ConvertTupleExpression(expr),
                "dict" => ConvertDictLiteral(expr),
                "set" => ConvertSetExpression(expr),
                "await" => ConvertAwaitExpression(expr),
                // Pattern matching patterns
                "literal_pattern" => ConvertLiteralPattern(expr),
                "capture_pattern" => ConvertCapturePattern(expr),
                "wildcard_pattern" => ConvertWildcardPattern(expr),
                "value_pattern" => ConvertValuePattern(expr),
                "group_pattern" => ConvertGroupPattern(expr),
                "sequence_pattern" => ConvertSequencePattern(expr),
                "mapping_pattern" => ConvertMappingPattern(expr),
                "class_pattern" => ConvertClassPattern(expr),
                _ => new ConstantExpression(PyNone.Instance)
            };
        }


        private static Expression? ConvertExpression(GeneratedExpr expr)
        {
            // TODO: Implement specific expression conversions
            // This will handle all Python expression types (name, constant, binop, etc.)

            Console.WriteLine($"[DEBUG] ConvertExpression: Converting {expr.GetType()}");

            // Return a simple constant for now
            return new ConstantExpression(new PyString("expr"));
        }

        /// <summary>
        /// Convert list comprehension to SharpPy list comprehension expression
        /// </summary>
        private static Expression ConvertListComprehension(dynamic expr)
        {
            // Extract element and generators
            var element = ConvertDynamicToExpression(expr.element);
            var generators = ConvertComprehensionGenerators(expr.generators);

            // Create proper list comprehension AST node
            return new ListComprehension(element, generators);
        }

        /// <summary>
        /// Convert dict comprehension to SharpPy dict comprehension expression
        /// </summary>
        private static Expression ConvertDictComprehension(dynamic expr)
        {
            // Extract key, value and generators
            var key = ConvertDynamicToExpression(expr.key);
            var value = ConvertDynamicToExpression(expr.value);
            var generators = ConvertComprehensionGenerators(expr.generators);

            // Create proper dict comprehension AST node
            return new DictComprehension(key, value, generators);
        }

        /// <summary>
        /// Convert set comprehension to SharpPy set comprehension expression
        /// </summary>
        private static Expression ConvertSetComprehension(dynamic expr)
        {
            // Extract element and generators
            var element = ConvertDynamicToExpression(expr.element);
            var generators = ConvertComprehensionGenerators(expr.generators);

            // Create proper set comprehension AST node
            return new SetComprehension(element, generators);
        }

        /// <summary>
        /// Convert generator expression to SharpPy generator expression
        /// </summary>
        private static Expression ConvertGeneratorExpression(dynamic expr)
        {
            // Extract element and generators
            var element = ConvertDynamicToExpression(expr.element);
            var generators = ConvertComprehensionGenerators(expr.generators);

            // Create proper generator expression AST node
            return new GeneratorExpression(element, generators);
        }

        /// <summary>
        /// Convert await expression to SharpPy await expression
        /// </summary>
        private static Expression ConvertAwaitExpression(dynamic expr)
        {
            var value = ConvertDynamicToExpression(expr.value);
            return new AwaitExpression(value);
        }



        /// <summary>
        /// Convert slice expression to SharpPy slice expression
        /// </summary>
        private static Expression ConvertSliceExpression(dynamic expr)
        {
            Expression? start = null;
            Expression? stop = null;
            Expression? step = null;

            if (expr.start != null)
            {
                start = ConvertDynamicToExpression(expr.start);
            }

            if (expr.stop != null)
            {
                stop = ConvertDynamicToExpression(expr.stop);
            }

            if (expr.step != null)
            {
                step = ConvertDynamicToExpression(expr.step);
            }

            return new SliceExpression(start, stop, step);
        }

        /// <summary>
        /// Convert starred expression to SharpPy starred expression
        /// </summary>
        private static Expression ConvertStarredExpression(dynamic expr)
        {
            var value = ConvertDynamicToExpression(expr.value);
            return new StarredExpression(value);
        }

        /// <summary>
        /// Convert named expression (walrus operator) to SharpPy named expression
        /// </summary>
        private static Expression ConvertNamedExpression(dynamic expr)
        {
            var target = ConvertDynamicToExpression(expr.target);
            var value = ConvertDynamicToExpression(expr.value);

            return new NamedExpression(target, value);
        }

        /// <summary>
        /// Convert subscript expression to SharpPy subscript expression
        /// </summary>
        private static Expression ConvertSubscriptExpression(dynamic expr)
        {
            var value = ConvertDynamicToExpression(expr.value);
            var slice = ConvertDynamicToExpression(expr.slice);

            return new SubscriptExpression(value, slice);
        }

        /// <summary>
        /// Convert tuple expression to SharpPy tuple expression
        /// </summary>
        private static Expression ConvertTupleExpression(dynamic expr)
        {
            var elements = new List<Expression>();

            if (expr.elements != null)
            {
                foreach (var element in expr.elements)
                {
                    var convertedElement = ConvertDynamicToExpression(element);
                    elements.Add(convertedElement);
                }
            }

            return new TupleExpression(elements);
        }

        /// <summary>
        /// Convert set expression to SharpPy set expression
        /// </summary>
        private static Expression ConvertSetExpression(dynamic expr)
        {
            var elements = new List<Expression>();

            if (expr.elements != null)
            {
                foreach (var element in expr.elements)
                {
                    var convertedElement = ConvertDynamicToExpression(element);
                    elements.Add(convertedElement);
                }
            }

            return new SetExpression(elements);
        }

        /// <summary>
        /// Convert dict literal to SharpPy dict expression
        /// </summary>
        private static Expression ConvertDictLiteral(dynamic expr)
        {
            var pairs = new List<(Expression, Expression)>();

            if (expr.pairs != null)
            {
                foreach (var pair in expr.pairs)
                {
                    var key = ConvertDynamicToExpression(pair.key);
                    var value = ConvertDynamicToExpression(pair.value);
                    pairs.Add((key, value));
                }
            }

            return new DictExpression(pairs);
        }

        /// <summary>
        /// Convert set literal to SharpPy set expression
        /// </summary>
        private static Expression ConvertSetLiteral(dynamic expr)
        {
            var elements = new List<Expression>();

            if (expr.elements != null)
            {
                foreach (var element in expr.elements)
                {
                    var convertedElement = ConvertDynamicToExpression(element);
                    elements.Add(convertedElement);
                }
            }

            return new SetExpression(elements);
        }

        /// <summary>
        /// Convert comprehension generators (for_if_clauses)
        /// </summary>
        private static List<Comprehension> ConvertComprehensionGenerators(dynamic generators)
        {
            var result = new List<Comprehension>();

            if (generators != null)
            {
                foreach (var generator in generators)
                {
                    // Convert target (the variable being iterated)
                    Expression target;
                    if (generator.target is string targetName)
                    {
                        target = new NameExpression(targetName);
                    }
                    else
                    {
                        target = ConvertDynamicToExpression(generator.target);
                    }

                    // Convert iterable (what we're iterating over)
                    var iterable = ConvertDynamicToExpression(generator.iterable);

                    // Convert conditions (if clauses)
                    var conditions = new List<Expression>();
                    if (generator.conditions != null)
                    {
                        foreach (var condition in generator.conditions)
                        {
                            var convertedCondition = ConvertDynamicToExpression(condition);
                            conditions.Add(convertedCondition);
                        }
                    }

                    // Create proper Comprehension AST node
                    result.Add(new Comprehension(target, iterable, conditions));
                }
            }

            return result;
        }

        #region Pattern Matching Conversion Methods

        private static Expression ConvertLiteralPattern(dynamic expr)
        {
            // Literal pattern (e.g., case 42: or case "hello":)
            return ConvertDynamicToExpression(expr.value);
        }

        private static Expression ConvertCapturePattern(dynamic expr)
        {
            // Capture pattern (e.g., case x:)
            var name = expr.name as string ?? "";
            return new NameExpression(name);
        }

        private static Expression ConvertWildcardPattern(dynamic expr)
        {
            // Wildcard pattern (case _:)
            return new NameExpression("_");
        }

        private static Expression ConvertValuePattern(dynamic expr)
        {
            // Value pattern (e.g., case Color.RED:)
            return ConvertDynamicToExpression(expr.value);
        }

        private static Expression ConvertGroupPattern(dynamic expr)
        {
            // Group pattern (e.g., case (x):)
            return ConvertDynamicToExpression(expr.pattern);
        }

        private static Expression ConvertSequencePattern(dynamic expr)
        {
            // Sequence pattern (e.g., case [x, y]:)
            var patterns = new List<Expression>();
            if (expr.patterns != null)
            {
                foreach (var pattern in expr.patterns)
                {
                    patterns.Add(ConvertDynamicToExpression(pattern));
                }
            }
            return new MatchSequence(patterns);
        }

        private static Expression ConvertMappingPattern(dynamic expr)
        {
            // Mapping pattern (e.g., case {"key": value}:)
            var keys = new List<Expression>();
            var patterns = new List<Expression>();

            if (expr.keys != null && expr.patterns != null)
            {
                for (int i = 0; i < expr.keys.Count && i < expr.patterns.Count; i++)
                {
                    keys.Add(ConvertDynamicToExpression(expr.keys[i]));
                    patterns.Add(ConvertDynamicToExpression(expr.patterns[i]));
                }
            }

            return new MatchMapping(keys, patterns);
        }

        private static Expression ConvertClassPattern(dynamic expr)
        {
            // Class pattern (e.g., case Point(x, y):)
            var cls = ConvertDynamicToExpression(expr.cls);
            var patterns = new List<Expression>();

            if (expr.patterns != null)
            {
                foreach (var pattern in expr.patterns)
                {
                    patterns.Add(ConvertDynamicToExpression(pattern));
                }
            }

            var keywords = new List<string>();
            var kwdPatterns = new List<Expression>();

            if (expr.kwd_attrs != null && expr.kwd_patterns != null)
            {
                for (int i = 0; i < expr.kwd_attrs.Count && i < expr.kwd_patterns.Count; i++)
                {
                    keywords.Add(expr.kwd_attrs[i] as string ?? "");
                    kwdPatterns.Add(ConvertDynamicToExpression(expr.kwd_patterns[i]));
                }
            }

            return new MatchClass(cls, patterns, keywords, kwdPatterns);
        }

        #endregion
    }

    /// <summary>
    /// Configuration for generated CPython 3.12 compatible parser
    /// </summary>
    public static class GeneratedParserConfig
    {
        /// <summary>
        /// Initialize generated parser for production use
        /// </summary>
        public static void Initialize()
        {
            Console.WriteLine("[INFO] Using auto-generated CPython 3.12 compatible parser and tokenizer");
            Console.WriteLine("[INFO] Generated from Grammar/python.gram and Grammar/Tokens");
        }
    }
}