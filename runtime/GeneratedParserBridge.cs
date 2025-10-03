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
    public static partial class GeneratedParserBridge
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
        private static List<Statement> ConvertToSharpPyAST(GeneratedModule? parseResult, string filename)
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
        private static List<Statement> ConvertGeneratedAST(GeneratedModule parseResult, string filename)
        {
            // Convert module to statement list
            return ConvertModuleBody(parseResult);
        }

        /// <summary>
        /// Convert Generated module body to statement list
        /// </summary>
        private static List<Statement> ConvertModuleBody(GeneratedModule module)
        {
            var statements = new List<Statement>();

            if (module.Body != null)
            {
                // Convert each statement in the module body
                foreach (var stmt in module.Body.AsEnumerable())
                {
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] Module statement: Type={stmt.GetType().Name}");
#endif
                    // Regular statement conversion - all assignments are already properly structured
                    var converted = ConvertStatement((GeneratedStmt)stmt, false, false);
                    if (converted != null)
                        statements.Add(converted);
                }
            }

            return statements;
        }


        /// <summary>
        /// Convert Generated statement to SharpPy statement
        /// </summary>
        private static Statement? ConvertStatement(GeneratedStmt stmt, bool insideLoop = false, bool insideFunction = false)
        {
#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] ConvertStatement: Converting statement type '{stmt.GetType().Name}' (insideLoop: {insideLoop})");
#endif

            // Use pattern matching with concrete types instead of string-based type checks
            switch (stmt)
            {
                case GeneratedPassStmt:
                    // Pass statement - represented as expression statement with None
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case GeneratedBreakStmt:
                    // Break statement - only valid inside loops
                    if (!insideLoop)
                    {
                        throw new PythonException(new PySyntaxError("'break' outside loop"));
                    }
                    return new BreakStatement();

                case GeneratedContinueStmt:
                    // Continue statement - only valid inside loops
                    if (!insideLoop)
                    {
                        throw new PythonException(new PySyntaxError("'continue' not properly in loop"));
                    }
                    return new ContinueStatement();

                case GeneratedAnnAssignStmt annAssign:
                    // Annotated assignment statement (name: type = value or name: type)
                    {
                        try
                        {
                            // Convert target (should be a name)
                            string? targetName = null;
                            if (annAssign.Target is GeneratedNameExpr nameExpr)
                            {
                                targetName = nameExpr.Id;
                            }

                            if (targetName == null)
                            {
#if DEBUG_LOG
                                Console.WriteLine($"[DEBUG] ConvertStatement AnnAssign: Failed to extract target name");
#endif
                                return null;
                            }

                            // Convert annotation
                            Expression annotationExpr = ConvertAnyExpression(annAssign.Annotation);

                            // Convert value (optional)
                            Expression? valueExpr = annAssign.Value != null ? ConvertAnyExpression(annAssign.Value) : null;

#if DEBUG_LOG
                            Console.WriteLine($"[DEBUG] ConvertStatement AnnAssign: Creating AnnAssignStatement with name='{targetName}'");
#endif

                            return new AnnAssignStatement(targetName, annotationExpr, valueExpr);
                        }
                        catch (Exception ex)
                        {
#if DEBUG_LOG
                            Console.WriteLine($"[DEBUG] ConvertStatement AnnAssign error: {ex.Message}");
#endif
                            return null;
                        }
                    }

                case GeneratedAssignStmt assignStmt:
                    // Assignment statement (name = value OR a = b = c = value)
                    {
                        var targets = assignStmt.Targets;  // Already GeneratedExprSeq (List<GeneratedExpr>)
                        var valueExpr = assignStmt.Value;   // Already GeneratedExpr

#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Targets.Count={targets?.Count ?? 0}, Value={valueExpr != null}");
#endif

                        if (targets != null && targets.Count > 0 && valueExpr != null)
                        {
                            // CPython 3.12: Convert all targets with Store context
                            var targetExprs = new List<Expression>();
                            foreach (var target in targets.AsEnumerable())
                            {
                                if (target is GeneratedExpr targetExpr)
                                {
                                    var convertedTarget = ConvertAnyExpression(targetExpr);
                                    if (convertedTarget != null)
                                    {
                                        // CPython 3.12: Assignment targets have Store context
                                        SetExprContext(convertedTarget, Store.Instance);
                                        targetExprs.Add(convertedTarget);
                                    }
                                }
                            }

                            // CPython 3.12: Convert value with Load context (default)
                            Expression convertedValueExpr = ConvertAnyExpression(valueExpr);

                            // CPython 3.12: Return AssignStatement (handles both single and chained)
                            return new AssignStatement(targetExprs, convertedValueExpr);
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case GeneratedAugAssignStmt augAssign:
                    // Augmented assignment statement (name += value)
                    {
                        var target = augAssign.Target;  // Already GeneratedExpr
                        var op = augAssign.Op;          // Already string (operator)
                        var value = augAssign.Value;    // Already GeneratedExpr

#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Target={target != null}, Op='{op}', Value={value != null}");
#endif

                        if (target != null && !string.IsNullOrEmpty(op) && value != null)
                        {
                            // Convert target expression
                            var targetExpr = ConvertAnyExpression(target);

                            // Convert value expression
                            var valueExpr = ConvertAnyExpression(value);

                            if (targetExpr is NameExpression nameExpr)
                            {
#if DEBUG_LOG
                                Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Creating AugAssignStatement with target='{nameExpr.Name}', op='{op}'");
#endif
                                return new AugAssignStatement(nameExpr.Name, op, valueExpr);
                            }
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case GeneratedExprStmt exprStmt:
                    // Expression statement (standalone expression)
                    {
                        var valueExpr = exprStmt.Value;  // Already GeneratedExpr
                        if (valueExpr != null)
                        {
                            var expression = ConvertAnyExpression(valueExpr);
                            return new ExpressionStatement(expression);
                        }
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedReturnStmt returnStmt:
                    // Return statement (return [expression]) - only valid inside functions
                    if (!insideFunction)
                    {
                        throw new PythonException(new PySyntaxError("'return' outside function"));
                    }
                    {
                        var valueExpr = returnStmt.Value;  // Already GeneratedExpr?
                        Expression returnValue;

                        if (valueExpr != null)
                        {
                            returnValue = ConvertAnyExpression(valueExpr);
                        }
                        else
                        {
                            returnValue = new ConstantExpression(PyNone.Instance);
                        }

                        return new ReturnStatement(returnValue);
                    }

                case GeneratedRaiseStmt raiseStmt:
                    // Raise statement (raise [expression] [from expression])
                    {
                        Expression? exceptionExpr = null;
                        Expression? fromExpr = null;

                        if (raiseStmt.Exc != null)
                        {
                            exceptionExpr = ConvertAnyExpression(raiseStmt.Exc);
                        }

                        if (raiseStmt.Cause != null)
                        {
                            fromExpr = ConvertAnyExpression(raiseStmt.Cause);
                        }

                        return new RaiseStatement(exceptionExpr, fromExpr);
                    }

                case GeneratedIfStmt ifStmt:
                    // If statement (if condition: body)
                    {
                        // Convert condition expression
                        Expression conditionExpr = ConvertAnyExpression(ifStmt.Test);

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        foreach (var bodyStmt in ifStmt.Body.AsEnumerable())
                        {
                            var convertedStmt = ConvertStatement((GeneratedStmt)bodyStmt, insideLoop, insideFunction);
                            if (convertedStmt != null)
                                bodyStmts.Add(convertedStmt);
                        }

                        // Convert else clause (orelse)
                        var elseStmts = new List<Statement>();
                        foreach (var elseStmt in ifStmt.OrElse.AsEnumerable())
                        {
                            var convertedStmt = ConvertStatement((GeneratedStmt)elseStmt, insideLoop, insideFunction);
                            if (convertedStmt != null)
                                elseStmts.Add(convertedStmt);
                        }

                        return new IfStatement(conditionExpr, bodyStmts, elseStmts);
                    }

                case GeneratedWhileStmt whileStmt:
                    // While statement (while condition: body [else: elseBody])
                    {
                        // Convert condition expression
                        Expression conditionExpr = ConvertAnyExpression(whileStmt.Test);

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        foreach (var bodyStmt in whileStmt.Body.AsEnumerable())
                        {
                            var convertedStmt = ConvertStatement((GeneratedStmt)bodyStmt, true, insideFunction); // insideLoop = true
                            if (convertedStmt != null)
                                bodyStmts.Add(convertedStmt);
                        }

                        // Convert optional else statements (Python while-else construct)
                        var elseStmts = new List<Statement>();
                        foreach (var elseStmt in whileStmt.OrElse.AsEnumerable())
                        {
                            var convertedStmt = ConvertStatement((GeneratedStmt)elseStmt, insideLoop, insideFunction);
                            if (convertedStmt != null)
                                elseStmts.Add(convertedStmt);
                        }

                        return new WhileStatement(conditionExpr, bodyStmts, elseStmts);
                    }

                case GeneratedForStmt forStmt:
                    // For statement (for target in iterable: body [else: elseBody])
                    {
                        // Convert target to get variable name
                        var targetExpr = ConvertAnyExpression(forStmt.Target);
                        string targetVar = "i"; // default
                        if (targetExpr is NameExpression nameExpr)
                        {
                            targetVar = nameExpr.Name;
                        }

                        // Convert iterable expression
                        Expression iterableExpr = ConvertAnyExpression(forStmt.Iter);

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        foreach (var bodyStmt in forStmt.Body.AsEnumerable())
                        {
                            var convertedStmt = ConvertStatement((GeneratedStmt)bodyStmt, true, insideFunction); // insideLoop = true
                            if (convertedStmt != null)
                                bodyStmts.Add(convertedStmt);
                        }

                        // Convert optional else statements (Python for-else construct)
                        var elseStmts = new List<Statement>();
                        foreach (var elseStmt in forStmt.OrElse.AsEnumerable())
                        {
                            var convertedStmt = ConvertStatement((GeneratedStmt)elseStmt, insideLoop, insideFunction);
                            if (convertedStmt != null)
                                elseStmts.Add(convertedStmt);
                        }

                        return new ForStatement(targetVar, iterableExpr, bodyStmts, elseStmts);
                    }

                case GeneratedAsyncForStmt asyncForStmt:
                    // Async for statement (async for target in iterable: body [else: elseBody])
                    {
                        // Convert target to get variable name
                        var targetExpr = ConvertAnyExpression(asyncForStmt.Target);
                        string targetVar = "i"; // default
                        if (targetExpr is NameExpression nameExpr)
                        {
                            targetVar = nameExpr.Name;
                        }

                        // Convert iterable expression
                        Expression iterableExpr = ConvertAnyExpression(asyncForStmt.Iter);

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        foreach (var bodyStmt in asyncForStmt.Body.AsEnumerable())
                        {
                            var convertedStmt = ConvertStatement((GeneratedStmt)bodyStmt, true, insideFunction); // insideLoop = true
                            if (convertedStmt != null)
                                bodyStmts.Add(convertedStmt);
                        }

                        // Convert optional else statements
                        var elseStmts = new List<Statement>();
                        foreach (var elseStmt in asyncForStmt.OrElse.AsEnumerable())
                        {
                            var convertedStmt = ConvertStatement((GeneratedStmt)elseStmt, insideLoop, insideFunction);
                            if (convertedStmt != null)
                                elseStmts.Add(convertedStmt);
                        }

                        return new AsyncForStatement(targetVar, iterableExpr, bodyStmts, elseStmts);
                    }

                case GeneratedTryStmt tryStmt:
                    // Try statement (try: body except: handler)
                    {
#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] try case: tryStmt.Body count = {tryStmt.Body?.Count}, Handlers count = {tryStmt.Handlers?.Count}");
#endif

                        // Convert try body statements
                        var tryBodyStatements = new List<Statement>();
                        foreach (var bodyStmt in tryStmt.Body.AsEnumerable())
                        {
                            var convertedStmt = ConvertStatement((GeneratedStmt)bodyStmt, insideLoop, insideFunction);
                            if (convertedStmt != null)
                                tryBodyStatements.Add(convertedStmt);
                        }

                        // Convert except blocks
                        var exceptHandlersList = new List<ExceptHandler>();
                        foreach (var exceptBlock in tryStmt.Handlers.AsEnumerable())
                        {
                            var exceptData = exceptBlock as dynamic;
#if DEBUG_LOG
                            Console.WriteLine($"[DEBUG] exceptData type: {exceptData?.GetType()?.Name}");
#endif
                            // Convert except body statements
                            var exceptBodyStmts = new List<Statement>();
                            if (exceptData.body != null)
                            {
                                foreach (var exceptStmt in (exceptData.body as IEnumerable<GeneratedPtr>).Cast<GeneratedStmt>())
                                {
                                    var convertedStmt = ConvertStatement(exceptStmt, insideLoop, insideFunction);
                                    if (convertedStmt != null)
                                        exceptBodyStmts.Add(convertedStmt);
                                }
                            }

                            // CPython 3.12: Parse exception type and variable name
                            Expression? exceptionTypeExpr = null;
                            if (exceptData.type != null)
                            {
                                if (exceptData.type is string typeStr)
                                {
                                    exceptionTypeExpr = new NameExpression(typeStr);
                                }
                                else if (exceptData.type is GeneratedExpr)
                                {
                                    exceptionTypeExpr = ConvertAnyExpression(exceptData.type);
                                }
                            }

                            string? variableName = null;
                            try
                            {
                                variableName = exceptData.name != null ? exceptData.name.ToString() : null;
                            }
                            catch { }

                            exceptHandlersList.Add(new ExceptHandler(exceptionTypeExpr, variableName, exceptBodyStmts));
                        }

                        // Convert else and finally blocks
                        var elseStatements = new List<Statement>();
                        foreach (var elseStmt in tryStmt.OrElse.AsEnumerable())
                        {
                            var convertedStmt = ConvertStatement((GeneratedStmt)elseStmt, insideLoop, insideFunction);
                            if (convertedStmt != null)
                                elseStatements.Add(convertedStmt);
                        }

                        var finallyStatements = new List<Statement>();
                        foreach (var finallyStmt in tryStmt.FinallyBody.AsEnumerable())
                        {
                            var convertedStmt = ConvertStatement((GeneratedStmt)finallyStmt, insideLoop, insideFunction);
                            if (convertedStmt != null)
                                finallyStatements.Add(convertedStmt);
                        }

                        return new TryStatement(tryBodyStatements, exceptHandlersList, elseStatements, finallyStatements);
                    }

                case GeneratedTryStarStmt tryStarStmt:
                    // Try statement with except* handlers (PEP 654: Exception Groups)
                    {
                        // Convert try body statements
                        var tryBodyStmts = new List<Statement>();
                        if (tryStarStmt.Body != null)
                        {
                            foreach (var bodyStmt in tryStarStmt.Body.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)bodyStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    tryBodyStmts.Add(convertedStmt);
                            }
                        }

                        // Convert except* handlers
                        var exceptHandlers = new List<ExceptHandler>();
                        if (tryStarStmt.Handlers != null)
                        {
                            foreach (var handlerData in tryStarStmt.Handlers.AsEnumerable())
                            {
                                var handler = handlerData as dynamic;

                                // Convert except body statements
                                var exceptBodyStmts = new List<Statement>();
                                if (handler.body != null)
                                {
                                    foreach (var exceptStmt in handler.body)
                                    {
                                        var convertedStmt = ConvertStatement(exceptStmt, insideLoop, insideFunction);
                                        if (convertedStmt != null)
                                            exceptBodyStmts.Add(convertedStmt);
                                    }
                                }

                                // Handle exception type and variable name for except*
                                Expression? exceptionTypeExpr = null;
                                if (handler.type != null)
                                {
                                    exceptionTypeExpr = ConvertAnyExpression(handler.type);
                                }

                                string? variableName = handler.name != null ? handler.name.ToString() : null;

                                // Create except* handler (marked as exception group handler)
                                var exHandler = new ExceptHandler(exceptionTypeExpr, variableName, exceptBodyStmts, isStar: true);  // Mark as except* handler for PEP 654
                                exceptHandlers.Add(exHandler);
                            }
                        }

                        // Handle else and finally blocks
                        var elseStmts = new List<Statement>();
                        if (tryStarStmt.OrElse != null)
                        {
                            foreach (var elseStmt in tryStarStmt.OrElse.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)elseStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    elseStmts.Add(convertedStmt);
                            }
                        }

                        var finallyStmts = new List<Statement>();
                        if (tryStarStmt.FinallyBody != null)
                        {
                            foreach (var finallyStmt in tryStarStmt.FinallyBody.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)finallyStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    finallyStmts.Add(convertedStmt);
                            }
                        }

                        return new TryStatement(tryBodyStmts, exceptHandlers, elseStmts, finallyStmts);
                    }

                case GeneratedFunctionDefStmt funcDef:
                    // Function definition (def name(): body)
                    {
                        var funcData = funcDef; // Use funcDef directly
                        var name = funcDef.Name;

                        if (!string.IsNullOrEmpty(name))
                        {
                            // CPython 3.12: Convert arguments to FunctionArguments
                            var functionArgs = ConvertFunctionArguments(funcData.Arguments);

                            // OLD CODE BELOW - will be removed after testing
                            /*
                            // Debug Arguments structure
                            Console.WriteLine($"[DEBUG] Arguments type: {funcData.Arguments.GetType().Name}");
                            Console.WriteLine($"[DEBUG] Arguments value: {funcData.Arguments}");

                                // Arguments structure parsing - try Dictionary first
                                if (funcData.Arguments is Dictionary<string, object> argsDict)
                                {
                                    Console.WriteLine($"[DEBUG] Arguments is Dictionary with keys: [{string.Join(", ", argsDict.Keys)}]");
                                    foreach (var kvp in argsDict)
                                    {
                                        Console.WriteLine($"[DEBUG] Key: {kvp.Key}, Value: {kvp.Value} (Type: {kvp.Value?.GetType().Name})");

                                        // Look for posonlyargs (CPython 3.12 positional-only parameters)
                                        if (kvp.Key == "posonlyargs")
                                        {
                                            if (kvp.Value is List<object> posonlyArgsList)
                                            {
                                                Console.WriteLine($"[DEBUG] Found posonlyargs list with {posonlyArgsList.Count} items");
                                                foreach (var param in posonlyArgsList)
                                                {
                                                    if (param != null)
                                                    {
                                                        Console.WriteLine($"[DEBUG] Processing positional-only parameter: {param} (Type: {param.GetType().Name})");

                                                        if (param is GeneratedExpr genExpr && genExpr.Value != null)
                                                        {
                                                            var valueObj = genExpr.Value;
                                                            var argProperty = valueObj.GetType().GetProperty("arg");
                                                            if (argProperty != null)
                                                            {
                                                                var extractedArg = argProperty.GetValue(valueObj);
                                                                var extractedParamName = extractedArg?.ToString();
                                                                if (!string.IsNullOrEmpty(extractedParamName))
                                                                {
                                                                    Console.WriteLine($"[DEBUG] Adding positional-only parameter: {extractedParamName}");
                                                                    parameters.Add(extractedParamName + " [posonly]"); // Mark as positional-only
                                                                }
                                                            }
                                                        }
                                                        else
                                                        {
                                                            Console.WriteLine($"[DEBUG] Adding positional-only parameter (toString): {param}");
                                                            parameters.Add(param.ToString() + " [posonly]");
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        // Look for parameter-related keys
                                        if (kvp.Key == "args" || kvp.Key == "arguments" || kvp.Key == "parameters" || kvp.Key == "params")
                                        {
                                            if (kvp.Value is List<object> paramList)
                                            {
                                                Console.WriteLine($"[DEBUG] Found parameter list with {paramList.Count} items");
                                                foreach (var param in paramList)
                                                {
                                                    Console.WriteLine($"[DEBUG] Processing param: {param} (Type: {param?.GetType().Name})");
                                                    if (param is string paramName)
                                                    {
                                                        Console.WriteLine($"[DEBUG] Adding string parameter: {paramName}");
                                                        parameters.Add(paramName);
                                                    }
                                                    else if (param != null)
                                                    {
                                                        // Try to extract parameter name from complex objects
                                                        if (param is Dictionary<string, object> paramDict)
                                                        {
                                                            Console.WriteLine($"[DEBUG] Parameter is Dictionary with keys: [{string.Join(", ", paramDict.Keys)}]");
                                                            if (paramDict.ContainsKey("arg"))
                                                            {
                                                                Console.WriteLine($"[DEBUG] Adding arg parameter: {paramDict["arg"]}");
                                                                parameters.Add(paramDict["arg"].ToString());
                                                            }
                                                            else if (paramDict.ContainsKey("name"))
                                                            {
                                                                Console.WriteLine($"[DEBUG] Adding name parameter: {paramDict["name"]}");
                                                                parameters.Add(paramDict["name"].ToString());
                                                            }
                                                            else
                                                            {
                                                                Console.WriteLine($"[DEBUG] No 'arg' or 'name' key found in parameter dict");
                                                            }
                                                        }
                                                        else
                                                        {
                                                            // Handle GeneratedExpr parameters (especially with PEP 695 type params)
                                                            if (param is GeneratedExpr genExpr && (genExpr.ExpressionType == "Name" || genExpr.ExpressionType == "arg"))
                                                            {
                                                                if (genExpr.Value is object valueObj)
                                                                {
                                                                    Console.WriteLine($"[DEBUG] GeneratedExpr.Value type: {valueObj.GetType().Name}");

                                                                    // Try 'arg' property first (for function parameters)
                                                                    var argProperty = valueObj.GetType().GetProperty("arg");
                                                                    if (argProperty != null)
                                                                    {
                                                                        var argValue = argProperty.GetValue(valueObj);
                                                                        Console.WriteLine($"[DEBUG] Found 'arg' property: '{argValue}' (type: {argValue?.GetType().Name})");
                                                                        var extractedParamName = argValue?.ToString();
                                                                        if (!string.IsNullOrEmpty(extractedParamName))
                                                                        {
                                                                            Console.WriteLine($"[DEBUG] Adding extracted parameter name from 'arg': {extractedParamName}");
                                                                            parameters.Add(extractedParamName);
                                                                        }
                                                                        else
                                                                        {
                                                                            Console.WriteLine($"[DEBUG] 'arg' property is empty, using toString: {param}");
                                                                            parameters.Add(param.ToString());
                                                                        }
                                                                    }
                                                                    else
                                                                    {
                                                                        // Fallback to 'value' property (for other cases)
                                                                        var valueProperty = valueObj.GetType().GetProperty("value");
                                                                        if (valueProperty != null)
                                                                        {
                                                                            var extractedValue = valueProperty.GetValue(valueObj);
                                                                            Console.WriteLine($"[DEBUG] Found 'value' property: '{extractedValue}' (type: {extractedValue?.GetType().Name})");
                                                                            var extractedParamName = extractedValue?.ToString();
                                                                            if (!string.IsNullOrEmpty(extractedParamName))
                                                                            {
                                                                                Console.WriteLine($"[DEBUG] Adding extracted parameter name from 'value': {extractedParamName}");
                                                                                parameters.Add(extractedParamName);
                                                                            }
                                                                            else
                                                                            {
                                                                                Console.WriteLine($"[DEBUG] 'value' property is empty, using toString: {param}");
                                                                                parameters.Add(param.ToString());
                                                                            }
                                                                        }
                                                                        else
                                                                        {
                                                                            Console.WriteLine($"[DEBUG] No 'arg' or 'value' property found in {valueObj.GetType().Name}, using toString: {param}");
                                                                            parameters.Add(param.ToString());
                                                                        }
                                                                    }
                                                                }
                                                                else
                                                                {
                                                                    Console.WriteLine($"[DEBUG] GeneratedExpr.Value is null, using toString: {param}");
                                                                    parameters.Add(param.ToString());
                                                                }
                                                            }
                                                            else
                                                            {
                                                                // Debug info for non-GeneratedExpr case
                                                                if (param is GeneratedExpr genExprDebug)
                                                                {
                                                                    Console.WriteLine($"[DEBUG] GeneratedExpr with ExpressionType '{genExprDebug.ExpressionType}' != 'Name', using toString: {param}");
                                                                }
                                                                else
                                                                {
                                                                    Console.WriteLine($"[DEBUG] Not a GeneratedExpr (type: {param?.GetType().Name}), using toString: {param}");
                                                                }
                                                                parameters.Add(param.ToString());
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine("[DEBUG] Parameter is null, skipping");
                                                    }
                                                }
                                            }
                                            else if (kvp.Value != null)
                                            {
                                                Console.WriteLine($"[DEBUG] Parameter value is not a list: {kvp.Value}");
                                            }
                                        }

                                        // Look for defaults
                                        if (kvp.Key == "defaults")
                                        {
                                            if (kvp.Value is List<object> defaultsList)
                                            {
                                                Console.WriteLine($"[DEBUG] Found defaults list with {defaultsList.Count} items");
                                                // Store defaults by index - we'll match them to parameters later
                                                for (int i = 0; i < defaultsList.Count; i++)
                                                {
                                                    var defaultValue = defaultsList[i];
                                                    Console.WriteLine($"[DEBUG] Processing default {i}: {defaultValue} (Type: {defaultValue?.GetType().Name})");
                                                    defaultValues[i.ToString()] = defaultValue;
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    // Try alternative parsing approaches
                                    try
                                    {
                                        var argsData = funcData.Arguments as dynamic;
                                        if (argsData != null && argsData.args != null)
                                        {
                                            Console.WriteLine("[DEBUG] Found args field via dynamic");
                                            foreach (var arg in argsData.args)
                                            {
                                                var argData = arg as dynamic;
                                                if (argData != null && argData.arg != null)
                                                {
                                                    parameters.Add(argData.arg.ToString());
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[DEBUG] Dynamic parsing failed: {ex.Message}");

                                        // Final fallback - try as List<object>
                                        if (funcData.Arguments is List<object> argsList)
                                        {
                                            Console.WriteLine("[DEBUG] Arguments is List<object>");
                                            foreach (var arg in argsList)
                                            {
                                                if (arg is string argName)
                                                {
                                                    parameters.Add(argName);
                                                }
                                                else if (arg != null)
                                                {
                                                    parameters.Add(arg.ToString());
                                                }
                                            }
                                        }
                                    }
                                }

                                Console.WriteLine($"[DEBUG] Extracted function parameters: [{string.Join(", ", parameters)}]");
                            }
                            else
                            {
                                Console.WriteLine("[DEBUG] funcData.Arguments is null");
                            }
                            */
                            // END OF OLD CODE

                            // Convert function body with insideFunction=true
                            var bodyStmts = new List<Statement>();
                            if (funcDef.Body != null)
                            {
                                foreach (var bodyItem in funcDef.Body)
                                {
                                    var convertedStmt = ConvertStatement((GeneratedStmt)bodyItem, insideLoop, true); // insideFunction=true
                                    if (convertedStmt != null)
                                    {
                                        bodyStmts.Add(convertedStmt);
                                    }
                                }
                            }

                            // If no body statements, add a pass statement
                            if (bodyStmts.Count == 0)
                            {
                                bodyStmts.Add(new ExpressionStatement(new ConstantExpression(PyNone.Instance)));
                            }

                            // Process decorators if present
                            var decoratorExpressions = new List<DecoratorExpression>();
                            if (funcDef.DecoratorList != null && funcDef.DecoratorList.Count > 0)
                            {
#if DEBUG_LOG
                                Console.WriteLine($"[DEBUG] Function '{name}' has {funcDef.DecoratorList.Count} decorators");
#endif
                                // Convert decorators to expressions
                                foreach (var decorator in funcDef.DecoratorList.AsEnumerable())
                                {
                                    if (decorator is GeneratedExpr decoratorExpr)
                                    {
                                        var convertedDecorator = ConvertAnyExpression(decoratorExpr);
                                        if (convertedDecorator != null)
                                        {
                                            // Wrap the expression in a DecoratorExpression
                                            decoratorExpressions.Add(new DecoratorExpression(convertedDecorator));
#if DEBUG_LOG
                                            Console.WriteLine($"[DEBUG] Added decorator: {convertedDecorator}");
#endif
                                        }
                                    }
                                }
                            }

                            // CPython 3.12: Create function with FunctionArguments
                            Console.WriteLine($"[DEBUG] Creating FunctionDefStatement with FunctionArguments: {functionArgs}");
                            var functionDef = new FunctionDefStatement(name, functionArgs, bodyStmts, null, decoratorExpressions);
                            return functionDef;
                        }
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedAsyncFunctionDefStmt asyncFuncDef:
                    // Async function definition (async def name(): body)
                    {
                        var name = asyncFuncDef.Name;

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
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedClassDefStmt classDef:
                    // Class definition from parser (class name: body)
                    {
#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] ConvertStatement: Processing class statement");
                        Console.WriteLine($"[DEBUG] classDef.Name: {classDef.Name}");
                        Console.WriteLine($"[DEBUG] classDef.Bases count: {classDef.Bases.Count}");
                        Console.WriteLine($"[DEBUG] classDef.Body count: {classDef.Body.Count}");
#endif
                        // Get class name
                        var className = classDef.Name ?? "";

                        // Get base classes
                        var baseClassExprs = new List<Expression>();
                        if (classDef.Bases != null)
                        {
                            foreach (var baseClass in classDef.Bases.AsEnumerable())
                            {
                                try
                                {
                                    if (baseClass is GeneratedExpr baseExpr)
                                    {
                                        var expr = ConvertAnyExpression(baseExpr);
                                        if (expr != null)
                                            baseClassExprs.Add(expr);
                                    }
                                }
                                catch (Exception ex)
                                {
#if DEBUG_LOG
                                    Console.WriteLine($"[DEBUG] Class base class conversion error: {ex.Message}");
#endif
                                }
                            }
                        }

                        // Get body statements
                        var classBodyStmts = new List<Statement>();
                        if (classDef.Body != null)
                        {
                            foreach (var bodyStmt in classDef.Body.AsEnumerable())
                            {
                                try
                                {
                                    var convertedStmt = ConvertStatement((GeneratedStmt)bodyStmt, insideLoop, insideFunction);
                                    if (convertedStmt != null)
                                        classBodyStmts.Add(convertedStmt);
                                }
                                catch (Exception ex)
                                {
#if DEBUG_LOG
                                    Console.WriteLine($"[DEBUG] Class body statement conversion error: {ex.Message}");
#endif
                                }
                            }
                        }

#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] ConvertStatement: Creating ClassDefStatement with name='{className}', bases={baseClassExprs.Count}, body={classBodyStmts.Count}");
#endif
                        return new ClassDefStatement(className, baseClassExprs, classBodyStmts);
                    }

                case GeneratedGlobalStmt globalStmt:
                    // Global statement (global var1, var2, ...)
                    {
                        if (globalStmt.Names != null && globalStmt.Names.Count > 0)
                        {
                            return new GlobalStatement(globalStmt.Names);
                        }
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedNonlocalStmt nonlocalStmt:
                    // Nonlocal statement (nonlocal var1, var2, ...)
                    {
                        if (nonlocalStmt.Names != null && nonlocalStmt.Names.Count > 0)
                        {
                            return new NonlocalStatement(nonlocalStmt.Names);
                        }
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedDeleteStmt delStmt:
                    // Delete statement (del var)
                    {
                        var targets = new List<Expression>();
                        foreach (var target in delStmt.Targets.AsEnumerable())
                        {
                            if (target is GeneratedExpr targetGenExpr)
                            {
                                var targetExpr = ConvertAnyExpression(targetGenExpr);
                                targets.Add(targetExpr);
                            }
                        }
                        if (targets.Count > 0)
                        {
                            return new DeleteStatement(targets);
                        }
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedImportStmt importStmt:
                    // Import statement (import module)
                    {
#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] Import case triggered");
                        Console.WriteLine($"  importStmt.Names count: {importStmt.Names?.Count ?? 0}");
#endif
                        if (importStmt.Names != null && importStmt.Names.Count > 0)
                        {
                            var names = new List<string>();
                            foreach (var moduleObj in importStmt.Names)
                            {
                                string moduleName = "";

                                // Try to extract module name from dynamic object
                                try
                                {
                                    var moduleObjDyn = moduleObj as dynamic;
                                    if (moduleObjDyn?.name != null)
                                    {
                                        moduleName = moduleObjDyn.name.ToString();
                                    }
                                    else
                                    {
                                        moduleName = moduleObj.ToString();
                                    }
                                }
                                catch
                                {
                                    moduleName = moduleObj.ToString();
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
                    }

                case GeneratedImportFromStmt importFromStmt:
                    // From import statement (from module import name) - CPython 3.12 compatible
                    {
                        Console.WriteLine($"[DEBUG] Processing from_import: module={importFromStmt.Module}, level={importFromStmt.Level}, names={importFromStmt.Names?.Count ?? 0}");

                        var module = importFromStmt.Module;
                        var level = importFromStmt.Level;
                        var importAliases = new List<ImportAlias>();

                        if (importFromStmt.Names != null)
                        {
                            foreach (var nameItem in importFromStmt.Names)
                        {
                            if (nameItem is GeneratedAlias alias)
                            {
                                var name = alias.Name;
                                var asName = alias.AsName;

                                if (!string.IsNullOrEmpty(name))
                                {
                                    importAliases.Add(new ImportAlias(name, asName));
                                    Console.WriteLine($"[DEBUG] Added import alias: {name} as {asName ?? name}");
                                }
                            }
                            else if (nameItem != null)
                            {
                                // Handle anonymous objects with Name and AsName properties
                                var nameObj = nameItem as dynamic;
                                try
                                {
                                    var name = nameObj?.Name?.ToString();
                                    var asName = nameObj?.AsName?.ToString();

                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        importAliases.Add(new ImportAlias(name, string.IsNullOrEmpty(asName) ? null : asName));
                                        Console.WriteLine($"[DEBUG] Added dynamic import: {name} as {asName ?? name}");
                                    }
                                    else
                                    {
                                        // Fallback for other formats
                                        importAliases.Add(new ImportAlias(nameItem.ToString()));
                                        Console.WriteLine($"[DEBUG] Added fallback import: {nameItem}");
                                    }
                                }
                                catch
                                {
                                    // Final fallback
                                    importAliases.Add(new ImportAlias(nameItem.ToString()));
                                    Console.WriteLine($"[DEBUG] Added fallback import: {nameItem}");
                                }
                            }
                        }
                        }

                        if (importAliases.Count > 0)
                        {
                            Console.WriteLine($"[DEBUG] Creating ImportFromStatement: module={module}, level={level}, aliases={importAliases.Count}");
                            return new ImportFromStatement(module, importAliases, level);
                        }

                        Console.WriteLine("[DEBUG] Failed to parse from_import, returning placeholder");
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedWithStmt withStmt:
                    // With statement (with context_expr [as target]: body)
                    {
                        // Create with items
                        var items = new List<WithItem>();
                        if (withStmt.Items != null)
                        {
                            foreach (var item in withStmt.Items)
                            {
                                var itemData = item as dynamic;
                                var contextExpr = ConvertAnyExpression(itemData.context_expr);
                                Expression? optionalVars = itemData.optional_vars != null ?
                                    ConvertAnyExpression(itemData.optional_vars) : null;

                                items.Add(new WithItem(contextExpr, optionalVars));
                            }
                        }

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        if (withStmt.Body != null)
                        {
                            foreach (var bodyStmt in withStmt.Body.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)bodyStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    bodyStmts.Add(convertedStmt);
                            }
                        }

                        return new WithStatement(items, bodyStmts);
                    }

                case GeneratedAsyncWithStmt asyncWithStmt:
                    // Async with statement (async with context_expr [as target]: body)
                    {
                        // Create with items
                        var items = new List<WithItem>();
                        if (asyncWithStmt.Items != null)
                        {
                            foreach (var item in asyncWithStmt.Items)
                            {
                                var itemData = item as dynamic;
                                var contextExpr = ConvertAnyExpression(itemData.context_expr);
                                Expression? optionalVars = itemData.optional_vars != null ?
                                    ConvertAnyExpression(itemData.optional_vars) : null;

                                items.Add(new WithItem(contextExpr, optionalVars));
                            }
                        }

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        if (asyncWithStmt.Body != null)
                        {
                            foreach (var bodyStmt in asyncWithStmt.Body.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)bodyStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    bodyStmts.Add(convertedStmt);
                            }
                        }

                        return new AsyncWithStatement(items, bodyStmts);
                    }

                case GeneratedMatchStmt matchStmt:
                    // Match statement (match subject: case pattern: body)
                    {
                        // Convert subject expression
                        var subject = ConvertAnyExpression(matchStmt.Subject);

                        // Convert match cases
                        var cases = new List<MatchCase>();
                        if (matchStmt.Cases != null)
                        {
                            foreach (var caseItem in matchStmt.Cases)
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

                // TODO: Add GeneratedTypeAliasStmt case when type is available
                // case "type_alias": - removed as no GeneratedTypeAliasStmt exists yet

                default:
                    // Fallback for unhandled statement types
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] ConvertStatement: Unhandled statement type '{stmt.GetType().Name}'");
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

            // CPython 3.12: Convert string operator to BinaryOperator node
            var opNode = ConvertToBinaryOperator(op);
            return new BinOpExpression(leftExpr, opNode, rightExpr);
        }

        /// <summary>
        /// Convert string operator to CPython 3.12 compatible BinaryOperator node
        /// </summary>
        private static BinaryOperator ConvertToBinaryOperator(string op)
        {
            return op switch
            {
                "+" => Add.Instance,
                "-" => Sub.Instance,
                "*" => Mult.Instance,
                "/" => Div.Instance,
                "//" => FloorDiv.Instance,
                "%" => Mod.Instance,
                "**" => Pow.Instance,
                "<<" => LShift.Instance,
                ">>" => RShift.Instance,
                "|" => BitOr.Instance,
                "^" => BitXor.Instance,
                "&" => BitAnd.Instance,
                "@" => MatMult.Instance,
                _ => throw new NotImplementedException($"Unknown binary operator: {op}")
            };
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
            Expression[] comparatorExprs = comparators.Select(comp => ConvertAnyExpressionDynamic(comp)).ToArray();

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
        /// Convert GeneratedExpr to Expression
        /// </summary>
        private static Expression ConvertAnyExpression(GeneratedExpr expr)
        {
            if (expr == null)
            {
                throw new ArgumentNullException(nameof(expr), "Expression cannot be null");
            }

            // Handle GeneratedExpr objects (modern parser output)
            return ConvertGeneratedExpression(expr);
        }

        /// <summary>
        /// Legacy method for backward compatibility - converts dynamic to Expression
        /// This should be phased out
        /// </summary>
        [Obsolete("Use ConvertAnyExpression(GeneratedExpr) instead")]
        private static Expression ConvertAnyExpressionDynamic(dynamic expr)
        {
            if (expr == null)
            {
                throw new ArgumentNullException(nameof(expr), "Expression cannot be null");
            }

            // Primary path: Handle GeneratedExpr objects (modern parser output)
            if (expr is GeneratedExpr genExpr)
            {
                return ConvertGeneratedExpression(genExpr);
            }

            // Also check by type name in case dynamic binding interferes
            if (expr?.GetType()?.Name == "GeneratedExpr")
            {
                var exprType = expr.GetType().GetProperty("ExpressionType")?.GetValue(expr)?.ToString();
                return ConvertGeneratedExpression(expr);
            }

            // CPython 3.12: All parsers must return GeneratedExpr objects for consistency
            throw new InvalidOperationException($"ConvertAnyExpression received non-GeneratedExpr object: {expr?.GetType()?.Name}. Object: {expr}. All parsers should return GeneratedExpr objects. This indicates a parser inconsistency that needs to be fixed.");
        }

        /// <summary>
        /// Convert GeneratedExpr to SharpPy Expression using pattern matching
        /// CPython 3.12: All expression types have concrete Generated classes
        /// </summary>
        private static Expression ConvertGeneratedExpression(GeneratedExpr genExpr)
        {
            return genExpr switch
            {
                // Assignment expressions (walrus operator)
                GeneratedNamedExprExpr namedExpr => new NamedExpression(
                    ConvertAnyExpression(namedExpr.Target),
                    ConvertAnyExpression(namedExpr.Value)
                ),

                // Boolean operations
                GeneratedBoolOpExpr boolOp => new BoolOpExpression(
                    ConvertToBoolOperator(boolOp.Op),
                    boolOp.Values.AsEnumerable().Select(v => ConvertAnyExpression(v)).ToList()
                ),

                GeneratedUnaryOpExpr unaryOp => new UnaryOpExpression(
                    ConvertToUnaryOperator(unaryOp.Op),
                    ConvertAnyExpression(unaryOp.Operand)
                ),

                // F-strings
                GeneratedJoinedStrExpr joinedStr => new JoinedStrExpression(
                    joinedStr.Values.AsEnumerable().Select(v => ConvertAnyExpression(v)).ToList()
                ),

                GeneratedFormattedValueExpr formattedValue => new FormattedValueExpression(
                    ConvertAnyExpression(formattedValue.Value),
                    formattedValue.Conversion,
                    formattedValue.FormatSpec != null ? ConvertAnyExpression(formattedValue.FormatSpec) : null
                ),

                // Basic expressions - CPython 3.12: Convert value to PyObject if needed
                GeneratedConstantExpr constant => new ConstantExpression(
                    constant.Value is PyObject pyObj ? pyObj : ParseConstantValue(constant.Value, constant.Kind)
                ),

                GeneratedNameExpr name => new NameExpression(name.Id),

                // Binary and comparison operations
                GeneratedBinOpExpr binOp => new BinOpExpression(
                    ConvertAnyExpression(binOp.Left),
                    ConvertBinaryOp(binOp.Op),
                    ConvertAnyExpression(binOp.Right)
                ),

                GeneratedCompareExpr compare => new CompareExpression(
                    ConvertAnyExpression(compare.Left),
                    string.Join(" ", compare.Ops),
                    ConvertAnyExpression(compare.Comparators.AsEnumerable().First())
                ),

                // Function calls and attribute access
                GeneratedCallExpr call => new CallExpression(
                    ConvertAnyExpression(call.Func),
                    call.Args.AsEnumerable().Select(a => ConvertAnyExpression(a)).ToList(),
                    call.Keywords.AsEnumerable().Select(k => ConvertKeyword(k)).ToList()
                ),

                GeneratedAttributeExpr attr => new AttributeExpression(
                    ConvertAnyExpression(attr.Value),
                    attr.Attr
                ),

                GeneratedSubscriptExpr subscript => new SubscriptExpression(
                    ConvertAnyExpression(subscript.Value),
                    ConvertAnyExpression(subscript.Slice)
                ),

                // Collections
                GeneratedListExpr list => new ListExpression(
                    list.Elements.AsEnumerable().Select(e => ConvertAnyExpression(e)).ToList()
                ),

                GeneratedTupleExpr tuple => new TupleExpression(
                    tuple.Elements.AsEnumerable().Select(e => ConvertAnyExpression(e)).ToList()
                ),

                GeneratedDictExpr dict => new DictExpression(
                    dict.Keys.AsEnumerable().Zip(dict.Values.AsEnumerable(), (k, v) => (
                        Key: ConvertAnyExpression(k),
                        Value: ConvertAnyExpression(v))
                    ).ToList()
                ),

                GeneratedSetExpr set => new SetExpression(
                    set.Elements.AsEnumerable().Select(e => ConvertAnyExpression(e)).ToList()
                ),

                // Comprehensions
                GeneratedListCompExpr listComp => new ListComprehension(
                    ConvertAnyExpression(listComp.Element),
                    listComp.Generators.AsEnumerable().Select(g => ConvertComprehension(g)).ToList()
                ),

                GeneratedSetCompExpr setComp => new SetComprehension(
                    ConvertAnyExpression(setComp.Element),
                    setComp.Generators.AsEnumerable().Select(g => ConvertComprehension(g)).ToList()
                ),

                GeneratedDictCompExpr dictComp => new DictComprehension(
                    ConvertAnyExpression(dictComp.Key),
                    ConvertAnyExpression(dictComp.Value),
                    dictComp.Generators.AsEnumerable().Select(g => ConvertComprehension(g)).ToList()
                ),

                GeneratedGeneratorExpExpr genExp => new GeneratorExpression(
                    ConvertAnyExpression(genExp.Element),
                    genExp.Generators.AsEnumerable().Select(g => ConvertComprehension(g)).ToList()
                ),

                // Lambda expressions
                GeneratedLambdaExpr lambda => new LambdaExpression(
                    ConvertFunctionArgumentsToNames(lambda.Arguments),
                    ConvertAnyExpression(lambda.Body)
                ),

                // Ternary (if expression)
                GeneratedIfExpExpr ifExp => new ConditionalExpression(
                    ConvertAnyExpression(ifExp.Test),
                    ConvertAnyExpression(ifExp.Body),
                    ConvertAnyExpression(ifExp.OrElse)
                ),

                // Async/await expressions
                GeneratedAwaitExpr awaitExpr => new AwaitExpression(
                    ConvertAnyExpression(awaitExpr.Value)
                ),

                GeneratedYieldExpr yieldExpr => new YieldExpression(
                    yieldExpr.Value != null ? ConvertAnyExpression(yieldExpr.Value) : null
                ),

                GeneratedYieldFromExpr yieldFrom => new YieldFromExpression(
                    ConvertAnyExpression(yieldFrom.Value)
                ),

                // Starred expression (unpacking)
                GeneratedStarredExpr starred => new StarredExpression(
                    ConvertAnyExpression(starred.Value)
                ),

                // Slice expression
                GeneratedSliceExpr slice => new SliceExpression(
                    slice.Lower != null ? ConvertAnyExpression(slice.Lower) : null,
                    slice.Upper != null ? ConvertAnyExpression(slice.Upper) : null,
                    slice.Step != null ? ConvertAnyExpression(slice.Step) : null
                ),

                _ => throw new NotSupportedException($"Unsupported GeneratedExpr type: {genExpr.GetType().Name}")
            };
        }

        /// <summary>
        /// Convert operator string to CPython 3.12 UnaryOperator node
        /// </summary>
        private static UnaryOperator ConvertToUnaryOperator(string op)
        {
            return op switch
            {
                "Not" => Not.Instance,
                "UAdd" => UAdd.Instance,
                "USub" => USub.Instance,
                "Invert" => Invert.Instance,
                _ => throw new NotImplementedException($"Unknown unary operator: {op}")
            };
        }

        /// <summary>
        /// Convert operator string to CPython 3.12 BoolOperator node
        /// </summary>
        private static BoolOperator ConvertToBoolOperator(string op)
        {
            return op switch
            {
                "And" => And.Instance,
                "Or" => Or.Instance,
                "and" => And.Instance,
                "or" => Or.Instance,
                _ => throw new NotImplementedException($"Unknown boolean operator: {op}")
            };
        }

        /// <summary>
        /// Convert binary operator string to CPython 3.12 BinaryOperator node
        /// </summary>
        private static BinaryOperator ConvertBinaryOp(string op)
        {
            return op switch
            {
                "Add" => Add.Instance,
                "Sub" => Sub.Instance,
                "Mult" => Mult.Instance,
                "Div" => Div.Instance,
                "FloorDiv" => FloorDiv.Instance,
                "Mod" => Mod.Instance,
                "Pow" => Pow.Instance,
                "LShift" => LShift.Instance,
                "RShift" => RShift.Instance,
                "BitOr" => BitOr.Instance,
                "BitXor" => BitXor.Instance,
                "BitAnd" => BitAnd.Instance,
                "MatMult" => MatMult.Instance,
                _ => throw new NotImplementedException($"Unknown binary operator: {op}")
            };
        }

        /// <summary>
        /// CPython 3.12: Set expression context recursively
        /// Handles Name, Tuple, List, Attribute, Subscript
        /// </summary>
        private static void SetExprContext(Expression expr, ExprContext ctx)
        {
            switch (expr)
            {
                case NameExpression name:
                    name.Ctx = ctx;
                    break;
                case TupleExpression tuple:
                    tuple.Elements.ForEach(e => SetExprContext(e, ctx));
                    break;
                case ListExpression list:
                    list.Elements.ForEach(e => SetExprContext(e, ctx));
                    break;
                case AttributeExpression attr:
                    // Only the final attribute has the context
                    // The value part is always Load
                    break;
                case SubscriptExpression subscript:
                    // Only the subscript target has context
                    // The slice is always Load
                    break;
            }
        }

        /// <summary>
        /// Convert operator string to CPython 3.12 ComparisonOperator node
        /// </summary>
        private static ComparisonOperator ConvertToComparisonOperator(string op)
        {
            return op switch
            {
                "==" => Eq.Instance,
                "!=" => NotEq.Instance,
                "<" => Lt.Instance,
                "<=" => LtE.Instance,
                ">" => Gt.Instance,
                ">=" => GtE.Instance,
                "is" => Is.Instance,
                "is not" => IsNot.Instance,
                "in" => In.Instance,
                "not in" => NotIn.Instance,
                _ => throw new NotImplementedException($"Unknown comparison operator: {op}")
            };
        }

        /// <summary>
        /// Convert GeneratedKeyword to KeywordExpression
        /// </summary>
        private static KeywordExpression ConvertKeyword(GeneratedKeyword keyword)
        {
            if (keyword == null)
            {
                throw new ArgumentNullException(nameof(keyword));
            }

            if (keyword.Value == null)
            {
                throw new InvalidOperationException("Keyword value cannot be null");
            }

            return new KeywordExpression(keyword.Arg, ConvertAnyExpression(keyword.Value));
        }

        /// <summary>
        /// Convert GeneratedComprehension to Comprehension
        /// </summary>
        private static Comprehension ConvertComprehension(GeneratedComprehension comp)
        {
            if (comp == null)
            {
                throw new ArgumentNullException(nameof(comp));
            }

            var target = ConvertAnyExpression(comp.Target);
            var iter = ConvertAnyExpression(comp.Iter);
            var ifs = comp.Ifs?.AsEnumerable().Select(ifExpr => ConvertAnyExpression(ifExpr)).ToList() ?? new List<Expression>();

            // Note: isAsync is tracked in comp.IsAsync but not used in Comprehension constructor
            return new Comprehension(target, iter, ifs);
        }

        /// <summary>
        /// Convert GeneratedArguments to FunctionArguments
        /// </summary>
        private static FunctionArguments ConvertArguments(GeneratedArguments argsObj)
        {
            if (argsObj == null)
            {
                throw new ArgumentNullException(nameof(argsObj));
            }

            return new FunctionArguments
            {
                PosOnlyArgs = argsObj.PosOnlyArgs?.Select(a => ConvertArg(a)).ToList() ?? new List<Arg>(),
                Args = argsObj.Args?.Select(a => ConvertArg(a)).ToList() ?? new List<Arg>(),
                VarArg = argsObj.VarArg != null ? ConvertArg(argsObj.VarArg) : null,
                KwOnlyArgs = argsObj.KwOnlyArgs?.Select(a => ConvertArg(a)).ToList() ?? new List<Arg>(),
                KwArg = argsObj.KwArg != null ? ConvertArg(argsObj.KwArg) : null,
                Defaults = argsObj.Defaults?.AsEnumerable().Select(d => ConvertAnyExpression(d)).ToList() ?? new List<Expression?>(),
                KwDefaults = argsObj.KwDefaults?.AsEnumerable().Select(d => d != null ? ConvertAnyExpression(d) : null).ToList() ?? new List<Expression?>()
            };
        }

        /// <summary>
        /// Convert GeneratedArg to Arg
        /// </summary>
        private static Arg ConvertArg(GeneratedArg argObj)
        {
            if (argObj == null)
            {
                throw new ArgumentNullException(nameof(argObj));
            }

            if (string.IsNullOrEmpty(argObj.Arg))
            {
                throw new InvalidOperationException("Arg name cannot be null or empty");
            }

            return new Arg(argObj.Arg, argObj.Annotation != null ? ConvertAnyExpression(argObj.Annotation) : null);
        }

        // OBSOLETE METHODS REMOVED - These used .Value property which no longer exists
        // List, Tuple, Dict, Set conversions now handled directly in ConvertAnyExpression
        // via concrete typed expressions (GeneratedListExpr, GeneratedTupleExpr, etc.)

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

            // CPython 3.12: All expressions should be GeneratedExpr
            if (expr is GeneratedExpr)
            {
                return ConvertAnyExpression(expr);
            }

            // All parsers should return GeneratedExpr objects
            throw new InvalidOperationException($"ConvertDynamicToExpression received non-GeneratedExpr object: {expr?.GetType()?.Name}. Parser should generate GeneratedExpr objects.");
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
            // Extract element and generators (CPython uses 'elt' in AST)
            var element = ConvertDynamicToExpression(expr.elt);
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

        // OBSOLETE: ConvertAwaitFromGenerated, ConvertTypeVarFromGenerated,
        // ConvertTypeVarTupleFromGenerated, ConvertParamSpecFromGenerated removed
        // These used .Value property which no longer exists - conversions now
        // handled directly via concrete typed expressions

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
                    // Convert target (the variable being iterated) with Store context (CPython 3.12)
                    Expression target;
                    if (generator.target is string targetName)
                    {
                        target = new NameExpression(targetName, Store.Instance);
                    }
                    else
                    {
                        target = ConvertDynamicToExpression(generator.target);
                        // If it's a NameExpression, ensure Store context
                        if (target is NameExpression nameExpr && nameExpr.Ctx?.ContextType != "Store")
                        {
                            target = new NameExpression(nameExpr.Name, Store.Instance);
                        }
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

        /// <summary>
        /// Convert string literal with proper handling of bytes literals (CPython 3.12 compatible)
        /// </summary>
        private static Expression ConvertStringLiteral(string literal)
        {
            // Handle empty string
            if (string.IsNullOrEmpty(literal))
                return new ConstantExpression(new PyString(""));

            // Check for string prefixes (b, r, f, br, rb, fr, rf)
            string prefix = "";
            string content = literal;

            // Extract prefix
            int quoteStart = -1;
            for (int i = 0; i < literal.Length; i++)
            {
                if (literal[i] == '"' || literal[i] == '\'')
                {
                    quoteStart = i;
                    break;
                }
                else if (char.IsLetter(literal[i]))
                {
                    prefix += char.ToLower(literal[i]);
                }
                else
                {
                    break;
                }
            }

            // Extract content (remove quotes)
            if (quoteStart >= 0)
            {
                content = literal.Substring(quoteStart);
                if (content.Length >= 2)
                {
                    // Handle triple quotes
                    if (content.StartsWith("\"\"\"") && content.EndsWith("\"\"\"") && content.Length >= 6)
                    {
                        content = content.Substring(3, content.Length - 6);
                    }
                    else if (content.StartsWith("'''") && content.EndsWith("'''") && content.Length >= 6)
                    {
                        content = content.Substring(3, content.Length - 6);
                    }
                    // Handle single quotes
                    else if ((content.StartsWith("\"") && content.EndsWith("\"")) ||
                             (content.StartsWith("'") && content.EndsWith("'")))
                    {
                        content = content.Substring(1, content.Length - 2);
                    }
                }
            }

            // Create appropriate object based on prefix
            if (prefix.Contains("b"))
            {
                // Bytes literal (b"..." or br"..." or rb"...")
                try
                {
                    // For now, create bytes from UTF-8 encoding
                    // TODO: Handle proper bytes literal parsing with escape sequences
                    var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                    return new ConstantExpression(new PyBytesObject(bytes));
                }
                catch
                {
                    // Fallback to empty bytes
                    return new ConstantExpression(new PyBytesObject(new byte[0]));
                }
            }
            else if (prefix.Contains("f"))
            {
                // F-string literal - for now treat as regular string
                // TODO: Implement proper f-string parsing
                return new ConstantExpression(new PyString(content));
            }
            else
            {
                // Regular string literal (may include r prefix for raw strings)
                if (prefix.Contains("r"))
                {
                    // Raw string - don't process escape sequences
                    // TODO: Implement proper raw string handling
                }
                return new ConstantExpression(new PyString(content));
            }
        }

        /// <summary>
        /// Helper method to convert default values to string representation
        /// </summary>
        private static string ConvertDefaultToString(object defaultValue)
        {
            if (defaultValue == null) return "None";

            Console.WriteLine($"[DEBUG] ConvertDefaultToString: {defaultValue} (Type: {defaultValue.GetType().Name})");

            // Handle GeneratedConstantExpr directly
            if (defaultValue is GeneratedConstantExpr constantExpr)
            {
                var value = constantExpr.Value;
                Console.WriteLine($"[DEBUG] GeneratedConstantExpr value: {value} (Type: {value?.GetType().Name})");

                // CPython 3.12: Value is now PyObject
                if (value == null || value is PyNone) return "None";
                if (value is PyString pyStr) return $"\"{pyStr.Value}\"";
                if (value is PyInt pyInt) return pyInt.Value.ToString();
                if (value is PyFloat pyFloat) return pyFloat.Value.ToString();
                if (value is PyBool pyBool) return pyBool.Value ? "True" : "False";

                return value.ToString() ?? "None";
            }

            // Handle other GeneratedExpr types (recurse through conversion)
            if (defaultValue is GeneratedExpr genExpr)
            {
                Console.WriteLine($"[DEBUG] GeneratedExpr type: {genExpr.GetType().Name}");

                // Try to convert to Expression and extract value
                try
                {
                    var expr = ConvertAnyExpression(genExpr);
                    if (expr is ConstantExpression constExpr)
                    {
                        var value = constExpr.Value;
                        if (value == null) return "None";

                        // Handle PyObject wrappers
                        object? rawValue = value;
                        if (value is PyObject pyObj)
                        {
                            // Extract the actual C# value from PyObject
                            if (pyObj is PyInt pyInt) rawValue = pyInt.Value;
                            else if (pyObj is PyFloat pyFloat) rawValue = pyFloat.Value;
                            else if (pyObj is PyString pyStr) rawValue = pyStr.Value;
                            else if (pyObj is PyBool pyBool) rawValue = pyBool.Value;
                            else rawValue = pyObj.ToString();
                        }

                        if (rawValue == null) return "None";
                        if (rawValue is string s) return $"\"{s}\"";
                        if (rawValue is bool b) return b ? "True" : "False";
                        return rawValue.ToString() ?? "None";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG] Error converting GeneratedExpr: {ex.Message}");
                }

                return genExpr.ToString() ?? "None";
            }

            // Handle direct values
            if (defaultValue is string str2) return $"\"{str2}\"";
            if (defaultValue is int || defaultValue is long || defaultValue is double || defaultValue is float)
                return defaultValue.ToString()!;
            if (defaultValue is bool boolean2) return boolean2 ? "True" : "False";

            return defaultValue.ToString() ?? "None";
        }

        /// <summary>
        /// Convert GeneratedArguments to a simple list of parameter names for lambda expressions
        /// </summary>
        private static List<string> ConvertFunctionArgumentsToNames(GeneratedArguments argumentsData)
        {
            var funcArgs = ConvertFunctionArguments(argumentsData);
            var names = new List<string>();

            // Collect all parameter names in order: posonly, regular, vararg, kwonly, kwarg
            names.AddRange(funcArgs.PosOnlyArgs.Select(a => a.Name));
            names.AddRange(funcArgs.Args.Select(a => a.Name));
            if (funcArgs.VarArg != null)
                names.Add("*" + funcArgs.VarArg.Name);
            names.AddRange(funcArgs.KwOnlyArgs.Select(a => a.Name));
            if (funcArgs.KwArg != null)
                names.Add("**" + funcArgs.KwArg.Name);

            return names;
        }

        /// <summary>
        /// CPython 3.12: Parse constant value from PEG parser output
        /// Converts string representations to proper PyObject types
        /// </summary>
        private static PyObject ParseConstantValue(PyObject value, string? kind)
        {
            // If already a PyObject (not string), return as-is
            if (value is not PyString strValue)
                return value;

            string str = strValue.Value;

            // CPython 3.12: Parse based on kind hint
            if (kind == "number")
            {
                // Try int first
                if (int.TryParse(str, out int intVal))
                    return new PyInt(intVal);

                // Try float
                if (double.TryParse(str, out double floatVal))
                    return new PyFloat(floatVal);
            }
            else if (kind == "string")
            {
                return new PyString(str);
            }

            // Fallback: keep as string
            return value;
        }

        /// <summary>
        /// Convert a .NET object value to a PyObject instance
        /// </summary>
        private static PyObject ObjectToPyObject(object? value)
        {
            if (value == null)
                return PyNone.Instance;

            return value switch
            {
                PyObject pyObj => pyObj,
                string str => new PyString(str),
                int i => new PyInt(i),
                long l => new PyInt((int)l), // Cast long to int (may overflow for large values)
                double d => new PyFloat(d),
                float f => new PyFloat(f),
                bool b => b ? PyBool.True : PyBool.False,
                byte[] bytes => new PyBytesObject(bytes),
                _ => new PyString(value.ToString() ?? "")
            };
        }
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