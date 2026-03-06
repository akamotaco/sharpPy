using System;
using System.Collections.Generic;
using System.Reflection;
using SharpPy.Generated;
// using SharpPy.Tokenizer.Generated; // Now using SharpPy.Generated
using SharpPy.Tools;

namespace SharpPy.Generated
{
    /// <summary>
    /// CPython 3.12 compatible generated parser bridge
    /// Uses auto-generated tokenizer and parser from Grammar/python.gram
    /// </summary>
    public static partial class PyParserRuntime
    {
        /// <summary>
        /// Main parsing entry point - uses auto-generated CPython 3.12 compatible tokenizer and parser
        /// </summary>
        public static List<GeneratedTokenInfo> LexerSource(string source, bool generateExtraTokens = false)
        {
#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] PyParserRuntime.LexerSource START");
            Console.WriteLine($"[DEBUG] GeneratedParserBridge: Using auto-generated CPython 3.12 tokenizer + parser");
#endif

#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine("[DEBUG] Creating tokenizer...");
#endif
            // Use generated tokenizer
            // CPython 3.12: generateExtraTokens = true for tokenize module (--tokens flag)
            // CPython 3.12: generateExtraTokens = false for parser (default)
            var tokenizer = new Tokenizer(source, generateExtraTokens);
#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine("[DEBUG] Calling tokenizer.Tokenize()...");
#endif
            var generatedTokens = tokenizer.Tokenize();
#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] Tokenize returned {generatedTokens.Count} tokens");
#endif
            return generatedTokens;
        }
        
        public static List<Statement> ParseSource(List<GeneratedTokenInfo> generatedTokens, string source, string filename = "<string>")
        {

#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine("[DEBUG] Creating parser...");
#endif
            // Use generated parser
            // CPython 3.12: COMMENT and NL tokens are not generated in parser mode (generateExtraTokens=false)
            var parser = new PyParser(generatedTokens, filename, source);
#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine("[DEBUG] Calling parser.ParseFile()...");
#endif
            GeneratedMod parseResult = parser.ParseFile();
#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] ParseFile returned: {parseResult?.GetType()?.Name ?? "null"}");
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
        /// Parse a single expression (for eval())
        /// CPython 3.12: Python/pythonrun.c:PyRun_StringFlags with Py_eval_input mode
        /// </summary>
        public static Expression ParseExpression(List<GeneratedTokenInfo> generatedTokens, string source, string filename = "<string>")
        {
#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine("[DEBUG] ParseExpression: Creating parser for eval mode...");
#endif
            var parser = new PyParser(generatedTokens, filename, source);

#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine("[DEBUG] ParseExpression: Calling parser.ParseEval()...");
#endif
            GeneratedMod parseResult = parser.ParseEval();

#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] ParseEval returned: {parseResult?.GetType()?.Name ?? "null"}");
#endif

            // Convert to SharpPy Expression
            return ConvertEvalToExpression(parseResult, filename);
        }

        /// <summary>
        /// Convert eval parse result to a single Expression
        /// CPython: Eval mode returns a single expression
        /// </summary>
        private static Expression ConvertEvalToExpression(GeneratedMod? parseResult, string filename)
        {
            if (parseResult == null)
                throw PySyntaxError.Create("invalid syntax", filename, 0);

            // ParseEval returns a GeneratedExpression wrapped in GeneratedMod
            if (parseResult is GeneratedExpression genExpr)
            {
                // Convert the expression using the proper conversion method
                return ConvertAnyExpression(genExpr.Body);
            }

            throw PySyntaxError.Create("eval() requires an expression", filename, 0);
        }


        /// <summary>
        /// Convert generated parser result to SharpPy AST
        /// This is where CPython 3.12 AST compatibility is implemented
        /// </summary>
        private static List<Statement> ConvertToSharpPyAST(GeneratedMod? parseResult, string filename)
        {
#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] ConvertToSharpPyAST: parseResult is {(parseResult == null ? "null" : "not null")}");
#endif
            if (parseResult == null)
            {
#if SHARPPY_DEBUG_AST_LOG
                Console.WriteLine("[DEBUG] ConvertToSharpPyAST: returning empty list (parseResult is null)");
#endif
                return new List<Statement>();
            }

            // CPython 3.12: mod_ty is a union (Module | Interactive | Expression | FunctionType)
            // SharpPy: GeneratedMod is abstract, cast to specific type
            if (parseResult is not GeneratedModule moduleResult)
            {
#if SHARPPY_DEBUG_AST_LOG
                Console.WriteLine($"[DEBUG] ConvertToSharpPyAST: parseResult is {parseResult.GetType().Name}, not GeneratedModule");
#endif
                // For now, only support Module mode (file parsing)
                // TODO: Support Interactive, Expression, FunctionType modes
                return new List<Statement>();
            }

#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] ConvertToSharpPyAST: moduleResult.Body is {(moduleResult.Body == null ? "null" : "not null")}");
            if (moduleResult.Body != null)
            {
                Console.WriteLine($"[DEBUG] ConvertToSharpPyAST: moduleResult.Body.Count = {moduleResult.Body.Count}");
            }

            // Phase 2: Start implementing AST conversion
            #if SHARPPY_DEBUG
            Console.WriteLine("[DEBUG] ConvertToSharpPyAST: Calling ConvertGeneratedAST...");
            #endif
#endif
            var result = ConvertGeneratedAST(moduleResult, filename);
#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] ConvertToSharpPyAST: ConvertGeneratedAST returned {result.Count} statements");
#endif
            return result;
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
#if SHARPPY_DEBUG_AST_LOG
                int stmtIndex = 0;
#endif
                foreach (var stmt in module.Body.AsEnumerable())
                {
#if SHARPPY_DEBUG_AST_LOG
                    Console.WriteLine($"[DEBUG] Module statement #{stmtIndex}: Type={stmt.GetType().Name}");
#endif
                    try
                    {
                        // Regular statement conversion - all assignments are already properly structured
                        var converted = ConvertStatement((GeneratedStmt)stmt, false, false);
                        if (converted != null)
                            statements.Add(converted);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Failed to convert statement (Type={stmt.GetType().Name}): {ex.Message}");
                        Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                        throw;
                    }
#if SHARPPY_DEBUG_AST_LOG
                    stmtIndex++;
#endif
                }
            }

            return statements;
        }


        /// <summary>
        /// Copy source location information from Generated AST node to runtime AST node
        /// CPython 3.12: Preserves lineno, col_offset, end_lineno, end_col_offset
        /// </summary>
        private static void CopySourceLocation(GeneratedPtr source, ASTNode target)
        {
            if (source is GeneratedStmt generatedStmt)
            {
                target.LineNo = generatedStmt.LineNo;
                target.ColOffset = generatedStmt.ColOffset;
                target.EndLineNo = generatedStmt.EndLineNo;
                target.EndColOffset = generatedStmt.EndColOffset;
            }
            else if (source is GeneratedExpr generatedExpr)
            {
                target.LineNo = generatedExpr.LineNo;
                target.ColOffset = generatedExpr.ColOffset;
                target.EndLineNo = generatedExpr.EndLineNo;
                target.EndColOffset = generatedExpr.EndColOffset;
            }
        }

        /// <summary>
        /// Convert Generated statement to SharpPy statement
        /// </summary>
        private static Statement? ConvertStatement(GeneratedStmt stmt, bool insideLoop = false, bool insideFunction = false)
        {
#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] ConvertStatement: Converting statement type '{stmt.GetType().Name}' (insideLoop: {insideLoop})");
#endif

            // Use pattern matching with concrete types instead of string-based type checks
            switch (stmt)
            {
                case GeneratedPass:
                    // Pass statement - CPython 3.12 compatible
                    {
                        var result = new PassStatement();
                        CopySourceLocation(stmt, result);
                        return result;
                    }

                case GeneratedBreak:
                    // Break statement - only valid inside loops
                    if (!insideLoop)
                    {
                        throw new PythonException(new PySyntaxError("'break' outside loop"));
                    }
                    {
                        var result = new BreakStatement();
                        CopySourceLocation(stmt, result);
                        return result;
                    }

                case GeneratedContinue:
                    // Continue statement - only valid inside loops
                    if (!insideLoop)
                    {
                        throw new PythonException(new PySyntaxError("'continue' not properly in loop"));
                    }
                    {
                        var result = new ContinueStatement();
                        CopySourceLocation(stmt, result);
                        return result;
                    }

                case GeneratedAnnAssign annAssign:
                    // Annotated assignment statement (name: type = value or name: type)
                    {
                        try
                        {
                            // Convert target (should be a name)
                            string? targetName = null;
                            if (annAssign.Target is GeneratedName nameExpr)
                            {
                                targetName = nameExpr.Id;
                            }

                            if (targetName == null)
                            {
#if SHARPPY_DEBUG_AST_LOG
                                Console.WriteLine($"[DEBUG] ConvertStatement AnnAssign: Failed to extract target name");
#endif
                                return null;
                            }

                            // Convert annotation
                            Expression annotationExpr = ConvertAnyExpression(annAssign.Annotation);

                            // Convert value (optional)
                            Expression? valueExpr = annAssign.Value != null ? ConvertAnyExpression(annAssign.Value) : null;

#if SHARPPY_DEBUG_AST_LOG
                            Console.WriteLine($"[DEBUG] ConvertStatement AnnAssign: Creating AnnAssignStatement with name='{targetName}'");
#endif

                            var result = new AnnAssignStatement(targetName, annotationExpr, valueExpr);
                            CopySourceLocation(stmt, result);
                            return result;
                        }
                        catch (Exception ex)
                        {
#if SHARPPY_DEBUG_AST_LOG
                            Console.WriteLine($"[DEBUG] ConvertStatement AnnAssign error: {ex.Message}");
#endif
                            return null;
                        }
                    }

                case GeneratedAssign assignStmt:
                    // Assignment statement (name = value OR a = b = c = value)
                    {
                        var targets = assignStmt.Targets;  // Already GeneratedExprSeq (List<GeneratedExpr>)
                        var valueExpr = assignStmt.Value;   // Already GeneratedExpr

#if SHARPPY_DEBUG_AST_LOG
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
                            var result = new AssignStatement(targetExprs, convertedValueExpr);
                            CopySourceLocation(stmt, result);
                            return result;
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case GeneratedAugAssign augAssign:
                    // Augmented assignment statement (name += value)
                    // CPython 3.12: operator is a type, not a string
                    {
                        var target = augAssign.Target;
                        var op = augAssign.Op;  // GeneratedOperator
                        var value = augAssign.Value;

#if SHARPPY_DEBUG_AST_LOG
                        Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Target={target != null}, Op={op?.GetType().Name}, Value={value != null}");
#endif

                        if (target != null && op != null && value != null)
                        {
                            // Convert target expression
                            var targetExpr = ConvertAnyExpression(target);

                            // Convert value expression
                            var valueExpr = ConvertAnyExpression(value);

                            // Convert operator
                            var opNode = ConvertGeneratedOperator(op);

                            // CPython 3.12: AugAssignStatement supports both Name and Attribute targets
                            if (targetExpr != null && opNode != null && valueExpr != null)
                            {
                                if (targetExpr is NameExpression nameExpr)
                                {
#if SHARPPY_DEBUG_AST_LOG
                                    Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Creating AugAssignStatement with Name target='{nameExpr.Name}', op={opNode.OperatorType}");
#endif
                                    var result = new AugAssignStatement(nameExpr.Name, opNode, valueExpr);
                                    CopySourceLocation(augAssign, result);
                                    return result;
                                }
                                else if (targetExpr is AttributeExpression attrExpr)
                                {
#if SHARPPY_DEBUG_AST_LOG
                                    Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Creating AugAssignStatement with Attribute target, op={opNode.OperatorType}");
#endif
                                    // CPython 3.12: AugAssignStatement with AttributeExpression target
                                    var result = new AugAssignStatement(targetExpr, opNode, valueExpr);
                                    CopySourceLocation(augAssign, result);
                                    return result;
                                }
                                else
                                {
#if SHARPPY_DEBUG_AST_LOG
                                    Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Creating AugAssignStatement with generic target, op={opNode.OperatorType}");
#endif
                                    // CPython 3.12: Support subscript and other targets
                                    var result = new AugAssignStatement(targetExpr, opNode, valueExpr);
                                    CopySourceLocation(augAssign, result);
                                    return result;
                                }
                            }
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case GeneratedExprStmt exprStmt:
                    // Expression statement (standalone expression)
                    // CPython 3.12: Python/compile.c:3915-3929 (compiler_stmt_expr)
                    // Yield and YieldFrom are ALWAYS expressions in the AST, wrapped in Expr statement
                    {
                        var valueExpr = exprStmt.Value;  // Already GeneratedExpr
                        if (valueExpr != null)
                        {
                            var expression = ConvertAnyExpression(valueExpr);
                            var exprResult = new ExpressionStatement(expression);
                            CopySourceLocation(stmt, exprResult);
                            return exprResult;
                        }
                        var emptyResult = new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                        CopySourceLocation(stmt, emptyResult);
                        return emptyResult;
                    }

                case GeneratedReturn returnStmt:
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

                        var result = new ReturnStatement(returnValue);
                        CopySourceLocation(returnStmt, result);
                        return result;
                    }

                case GeneratedAssert assertStmt:
                    // Assert statement (assert test [, msg]) - CPython 3.12 compatible
                    {
                        var testExpr = ConvertAnyExpression(assertStmt.Test);
                        Expression? msgExpr = null;

                        if (assertStmt.Msg != null)
                        {
                            msgExpr = ConvertAnyExpression(assertStmt.Msg);
                        }

                        var result = new AssertStatement(testExpr, msgExpr);
                        CopySourceLocation(assertStmt, result);
                        return result;
                    }

                case GeneratedRaise raiseStmt:
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

                        var result = new RaiseStatement(exceptionExpr, fromExpr);
                        CopySourceLocation(raiseStmt, result);
                        return result;
                    }

                case GeneratedIf ifStmt:
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
                        if (ifStmt.Orelse != null)
                        {
                            foreach (var elseStmt in ifStmt.Orelse.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)elseStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    elseStmts.Add(convertedStmt);
                            }
                        }

                        var result = new IfStatement(conditionExpr, bodyStmts, elseStmts);
                        CopySourceLocation(ifStmt, result);
                        return result;
                    }

                case GeneratedWhile whileStmt:
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
                        if (whileStmt.Orelse != null)
                        {
                            foreach (var elseStmt in whileStmt.Orelse.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)elseStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    elseStmts.Add(convertedStmt);
                            }
                        }

                        var result = new WhileStatement(conditionExpr, bodyStmts, elseStmts);
                        CopySourceLocation(whileStmt, result);
                        return result;
                    }

                case GeneratedFor forStmt:
                    // For statement (for target in iterable: body [else: elseBody])
                    {
                        // CPython 3.12: Convert target expression (supports Name, Tuple, List, etc.)
                        var targetExpr = ConvertAnyExpression(forStmt.Target);

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
                        if (forStmt.Orelse != null)
                        {
                            foreach (var elseStmt in forStmt.Orelse.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)elseStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    elseStmts.Add(convertedStmt);
                            }
                        }

                        var result = new ForStatement(targetExpr, iterableExpr, bodyStmts, elseStmts);
                        CopySourceLocation(forStmt, result);
                        return result;
                    }

                case GeneratedAsyncFor asyncForStmt:
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
                        if (asyncForStmt.Orelse != null)
                        {
                            foreach (var elseStmt in asyncForStmt.Orelse.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)elseStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    elseStmts.Add(convertedStmt);
                            }
                        }

                        var result = new AsyncForStatement(targetVar, iterableExpr, bodyStmts, elseStmts);
                        CopySourceLocation(asyncForStmt, result);
                        return result;
                    }

                case GeneratedTry tryStmt:
                    // Try statement (try: body except: handler)
                    {
#if SHARPPY_DEBUG_AST_LOG
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
                        if (tryStmt.Handlers != null)
                        {
                            foreach (var exceptBlock in tryStmt.Handlers.AsEnumerable())
                        {
                            var exceptData = (GeneratedExceptHandler)exceptBlock;
#if SHARPPY_DEBUG_AST_LOG
                            Console.WriteLine($"[DEBUG] exceptData type: {exceptData?.GetType()?.Name}");
#endif
                            // Convert except body statements
                            var exceptBodyStmts = new List<Statement>();
                            if (exceptData.Body != null)
                            {
                                foreach (var exceptStmt in exceptData.Body.AsEnumerable().Cast<GeneratedStmt>())
                                {
                                    var convertedStmt = ConvertStatement(exceptStmt, insideLoop, insideFunction);
                                    if (convertedStmt != null)
                                        exceptBodyStmts.Add(convertedStmt);
                                }
                            }

                            // CPython 3.12: Parse exception type and variable name
                            Expression? exceptionTypeExpr = null;
                            if (exceptData.Type != null)
                            {
                                exceptionTypeExpr = ConvertAnyExpression(exceptData.Type);
                            }

                            string? variableName = null;
                            try
                            {
                                variableName = exceptData.Name != null ? exceptData.Name.ToString() : null;
                            }
                            catch { }

                            exceptHandlersList.Add(new ExceptHandler(exceptionTypeExpr, variableName, exceptBodyStmts));
                        }
                        }

                        // Convert else and finally blocks
                        var elseStatements = new List<Statement>();
                        if (tryStmt.Orelse != null)
                        {
                            foreach (var elseStmt in tryStmt.Orelse.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)elseStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    elseStatements.Add(convertedStmt);
                            }
                        }

                        var finallyStatements = new List<Statement>();
                        if (tryStmt.Finalbody != null)
                        {
                            foreach (var finallyStmt in tryStmt.Finalbody.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)finallyStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    finallyStatements.Add(convertedStmt);
                            }
                        }

                        var result = new TryStatement(tryBodyStatements, exceptHandlersList, elseStatements, finallyStatements);
                        CopySourceLocation(tryStmt, result);
                        return result;
                    }

                case GeneratedTryStar tryStarStmt:
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
                                var handler = (GeneratedExceptHandler)handlerData;

                                // Convert except body statements
                                var exceptBodyStmts = new List<Statement>();
                                if (handler.Body != null)
                                {
                                    foreach (var exceptStmt in handler.Body.AsEnumerable().Cast<GeneratedStmt>())
                                    {
                                        var convertedStmt = ConvertStatement(exceptStmt, insideLoop, insideFunction);
                                        if (convertedStmt != null)
                                            exceptBodyStmts.Add(convertedStmt);
                                    }
                                }

                                // Handle exception type and variable name for except*
                                Expression? exceptionTypeExpr = null;
                                if (handler.Type != null)
                                {
                                    exceptionTypeExpr = ConvertAnyExpression(handler.Type);
                                }

                                string? variableName = handler.Name != null ? handler.Name.ToString() : null;

                                // Create except* handler (marked as exception group handler)
                                var exHandler = new ExceptHandler(exceptionTypeExpr, variableName, exceptBodyStmts, isStar: true);  // Mark as except* handler for PEP 654
                                exceptHandlers.Add(exHandler);
                            }
                        }

                        // Handle else and finally blocks
                        var elseStmts = new List<Statement>();
                        if (tryStarStmt.Orelse != null)
                        {
                            foreach (var elseStmt in tryStarStmt.Orelse.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)elseStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    elseStmts.Add(convertedStmt);
                            }
                        }

                        var finallyStmts = new List<Statement>();
                        if (tryStarStmt.Finalbody != null)
                        {
                            foreach (var finallyStmt in tryStarStmt.Finalbody.AsEnumerable())
                            {
                                var convertedStmt = ConvertStatement((GeneratedStmt)finallyStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    finallyStmts.Add(convertedStmt);
                            }
                        }

                        var result = new TryStatement(tryBodyStmts, exceptHandlers, elseStmts, finallyStmts);
                        CopySourceLocation(tryStarStmt, result);
                        return result;
                    }

                case GeneratedFunctionDef funcDef:
                    // Function definition (def name(): body)
                    {
                        var funcData = funcDef; // Use funcDef directly
                        var name = funcDef.Name;

                        if (!string.IsNullOrEmpty(name))
                        {
                            // CPython 3.12: Convert arguments to FunctionArguments
                            var functionArgs = ConvertFunctionArguments(funcData.Args);


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
#if SHARPPY_DEBUG_AST_LOG
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
#if SHARPPY_DEBUG_AST_LOG
                                            Console.WriteLine($"[DEBUG] Added decorator: {convertedDecorator}");
#endif
                                        }
                                    }
                                }
                            }

                            // CPython 3.12: Extract return type annotation if present
                            Expression? returnAnnotation = null;
                            if (funcDef.Returns != null)
                            {
                                returnAnnotation = ConvertAnyExpression(funcDef.Returns);
#if SHARPPY_DEBUG_AST_LOG
                                Console.WriteLine($"[DEBUG] Function '{name}' has return annotation: {returnAnnotation}");
#endif
                            }

                            // PEP 695: Convert type parameters if present
                            var typeParams = ConvertTypeParams(funcDef.TypeParams);

                            // CPython 3.12: Create function with FunctionArguments and return annotation
#if SHARPPY_DEBUG_AST_LOG
                            Console.WriteLine($"[DEBUG] Creating FunctionDefStatement with FunctionArguments: {functionArgs}");
#endif
                            var functionDef = new FunctionDefStatement(name, functionArgs, bodyStmts, typeParams, decoratorExpressions, returnAnnotation);
                            CopySourceLocation(funcDef, functionDef);
                            return functionDef;
                        }
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedAsyncFunctionDef asyncFuncDef:
                    // Async function definition (async def name(): body)
                    // Same structure as FunctionDef but creates AsyncFunctionDefStatement
                    {
                        var name = asyncFuncDef.Name;

                        if (!string.IsNullOrEmpty(name))
                        {
                            // Convert function body with insideFunction=true (same as FunctionDef)
                            var bodyStmts = new List<Statement>();
                            if (asyncFuncDef.Body != null)
                            {
                                foreach (var bodyItem in asyncFuncDef.Body)
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

                            // Convert arguments (same as FunctionDef)
                            FunctionArguments functionArgs = ConvertArguments(asyncFuncDef.Args);

                            // PEP 695: Convert type parameters if present (returns List<TypeParam>, convert to List<string>)
                            var typeParamObjs = ConvertTypeParams(asyncFuncDef.TypeParams);
                            var typeParams = typeParamObjs?.Select(tp => tp.Name).ToList();

                            // Convert decorators
                            var decoratorExpressions = new List<DecoratorExpression>();
                            if (asyncFuncDef.DecoratorList != null && asyncFuncDef.DecoratorList.Count > 0)
                            {
                                foreach (var decorator in asyncFuncDef.DecoratorList.AsEnumerable())
                                {
                                    if (decorator is GeneratedExpr decoratorExpr)
                                    {
                                        var convertedDecorator = ConvertAnyExpression(decoratorExpr);
                                        if (convertedDecorator != null)
                                        {
                                            decoratorExpressions.Add(new DecoratorExpression(convertedDecorator));
                                        }
                                    }
                                }
                            }

                            // Convert return type annotation
                            Expression? returnAnnotation = null;
                            if (asyncFuncDef.Returns != null)
                            {
                                returnAnnotation = ConvertAnyExpression(asyncFuncDef.Returns);
                            }

                            var result = new AsyncFunctionDefStatement(name, functionArgs, bodyStmts, typeParams, decoratorExpressions, returnAnnotation);
                            CopySourceLocation(asyncFuncDef, result);
                            return result;
                        }
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedClassDef classDef:
                    // Class definition from parser (class name: body)
                    {
#if SHARPPY_DEBUG_AST_LOG
                        Console.WriteLine($"[DEBUG] ConvertStatement: Processing class statement");
                        Console.WriteLine($"[DEBUG] classDef.Name: {classDef.Name}");
                        Console.WriteLine($"[DEBUG] classDef.Bases count: {classDef.Bases?.Count ?? 0}");
                        Console.WriteLine($"[DEBUG] classDef.Body count: {classDef.Body?.Count ?? 0}");
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
#if SHARPPY_DEBUG_AST_LOG
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
#if SHARPPY_DEBUG_AST_LOG
                                    Console.WriteLine($"[DEBUG] Class body statement conversion error: {ex.Message}");
#endif
                                }
                            }
                        }

                        // CPython 3.12: Extract metaclass from keywords
                        Expression? metaclassExpr = null;
                        if (classDef.Keywords != null)
                        {
                            foreach (var keyword in classDef.Keywords.AsEnumerable())
                            {
                                if (keyword is GeneratedKeyword kw && kw.Arg?.Value == "metaclass")
                                {
                                    metaclassExpr = ConvertAnyExpression(kw.Value);
#if SHARPPY_DEBUG_AST_LOG
                                    Console.WriteLine($"[DEBUG] Found metaclass keyword: {metaclassExpr?.GetType().Name}");
#endif
                                    break;
                                }
                            }
                        }

                        // PEP 695: Convert type parameters
                        var typeParams = ConvertTypeParams(classDef.TypeParams);

                        // CPython 3.12: Process decorators (Parser/Python.asdl:22 decorator_list)
                        var decoratorExpressions = new List<DecoratorExpression>();
                        if (classDef.DecoratorList != null && classDef.DecoratorList.Count > 0)
                        {
#if SHARPPY_DEBUG_AST_LOG
                            Console.WriteLine($"[DEBUG] Class '{className}' has {classDef.DecoratorList.Count} decorators");
#endif
                            foreach (var decorator in classDef.DecoratorList.AsEnumerable())
                            {
                                if (decorator is GeneratedExpr decoratorExpr)
                                {
                                    var convertedDecorator = ConvertAnyExpression(decoratorExpr);
                                    if (convertedDecorator != null)
                                    {
                                        decoratorExpressions.Add(new DecoratorExpression(convertedDecorator));
#if SHARPPY_DEBUG_AST_LOG
                                        Console.WriteLine($"[DEBUG] Added class decorator: {convertedDecorator}");
#endif
                                    }
                                }
                            }
                        }

#if SHARPPY_DEBUG_AST_LOG
                        Console.WriteLine($"[DEBUG] ConvertStatement: Creating ClassDefStatement with name='{className}', bases={baseClassExprs.Count}, body={classBodyStmts.Count}, metaclass={metaclassExpr != null}, typeParams={typeParams.Count}, decorators={decoratorExpressions.Count}");
#endif
                        var result = new ClassDefStatement(className, baseClassExprs, classBodyStmts, typeParams, metaclassExpr, decoratorExpressions);
                        CopySourceLocation(classDef, result);
                        return result;
                    }

                case GeneratedGlobal globalStmt:
                    // Global statement (global var1, var2, ...)
                    {
                        if (globalStmt.Names != null && globalStmt.Names.Count > 0)
                        {
                            // Performance: Eliminated LINQ - manual list conversion
                            var names = new List<string>();
                            foreach (var item in globalStmt.Names)
                            {
                                if (item is GeneratedIdentifier id)
                                {
                                    names.Add(id.Value);
                                }
                            }
                            var result = new GlobalStatement(names);
                            CopySourceLocation(globalStmt, result);
                            return result;
                        }
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedNonlocal nonlocalStmt:
                    // Nonlocal statement (nonlocal var1, var2, ...)
                    {
                        if (nonlocalStmt.Names != null && nonlocalStmt.Names.Count > 0)
                        {
                            // Performance: Eliminated LINQ - manual list conversion
                            var names = new List<string>();
                            foreach (var item in nonlocalStmt.Names)
                            {
                                if (item is GeneratedIdentifier id)
                                {
                                    names.Add(id.Value);
                                }
                            }
                            var result = new NonlocalStatement(names);
                            CopySourceLocation(nonlocalStmt, result);
                            return result;
                        }
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedDelete delStmt:
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
                            var result = new DeleteStatement(targets);
                            CopySourceLocation(delStmt, result);
                            return result;
                        }
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedImport importStmt:
                    // Import statement (import module)
                    {
#if SHARPPY_DEBUG_AST_LOG
                        Console.WriteLine($"[DEBUG] Import case triggered");
                        Console.WriteLine($"  importStmt.Names count: {importStmt.Names?.Count ?? 0}");
#endif
                        if (importStmt.Names != null && importStmt.Names.Count > 0)
                        {
                            var names = new List<string>();
                            foreach (var moduleObj in importStmt.Names)
                            {
                                string moduleName = "";

                                // CPython 3.12: ImportStatement.Names contains GeneratedAlias objects
                                // GeneratedAlias.Name is GeneratedIdentifier (string wrapper)
                                try
                                {
                                    if (moduleObj is GeneratedAlias alias && alias.Name != null)
                                    {
                                        // Get name from GeneratedIdentifier (implicit conversion to string)
                                        string nameStr = alias.Name;

                                        // Handle "as" clause if present
                                        if (alias.Asname != null)
                                        {
                                            string asnameStr = alias.Asname;
                                            if (!string.IsNullOrEmpty(asnameStr))
                                            {
                                                moduleName = $"{nameStr} as {asnameStr}";
                                            }
                                            else
                                            {
                                                moduleName = nameStr;
                                            }
                                        }
                                        else
                                        {
                                            moduleName = nameStr;
                                        }
                                    }
                                    else
                                    {
                                        // Fallback: try dynamic access
                                        var moduleObjDyn = moduleObj as dynamic;
                                        if (moduleObjDyn?.Name != null)
                                        {
                                            moduleName = moduleObjDyn.Name.ToString();
                                        }
                                        else
                                        {
                                            moduleName = moduleObj.ToString();
                                        }
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
#if SHARPPY_DEBUG_AST_LOG
                                Console.WriteLine($"[DEBUG] Import: Creating ImportStatement with {names.Count} modules: {string.Join(", ", names)}");
#endif
                                var result = new ImportStatement(names);
                                CopySourceLocation(importStmt, result);
                                return result;
                            }
                        }
#if SHARPPY_DEBUG_AST_LOG
                        Console.WriteLine($"[DEBUG] Import: No valid modules found - returning fallback");
#endif
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedImportFrom importFromStmt:
                    // From import statement (from module import name) - CPython 3.12 compatible
                    {
#if SHARPPY_DEBUG
                        Console.WriteLine($"[DEBUG] importFromStmt.Module type: {importFromStmt.Module?.GetType()?.Name}, value: {importFromStmt.Module}");
#endif
                        var module = importFromStmt.Module?.ToString();
#if SHARPPY_DEBUG
                        Console.WriteLine($"[DEBUG] Processing from_import: module={module}, level={importFromStmt.Level}, names={importFromStmt.Names?.Count ?? 0}");
#endif
                        var level = importFromStmt.Level ?? 0;  // CPython 3.12: None → 0 (absolute import)
                        var importAliases = new List<ImportAlias>();

                        if (importFromStmt.Names != null)
                        {
#if SHARPPY_DEBUG
                            Console.WriteLine($"[DEBUG] importFromStmt.Names type: {importFromStmt.Names.GetType().Name}");
#endif
                            int nameIndex = 0;
                            foreach (var nameItem in importFromStmt.Names)
                            {
#if SHARPPY_DEBUG
                                Console.WriteLine($"[DEBUG] Processing name item #{nameIndex}: type={nameItem?.GetType()?.Name}");
#endif
                                var alias = (GeneratedAlias)nameItem;
#if SHARPPY_DEBUG
                                Console.WriteLine($"[DEBUG] alias.Name type: {alias.Name?.GetType()?.Name}, value: {alias.Name}");
                                Console.WriteLine($"[DEBUG] alias.Asname type: {alias.Asname?.GetType()?.Name}, value: {alias.Asname}");
#endif
                                var name = alias.Name?.ToString();
                                var asName = alias.Asname?.ToString();
#if SHARPPY_DEBUG
                                Console.WriteLine($"[DEBUG] Converted: name={name}, asName={asName}");
#endif
                                nameIndex++;

                                if (!string.IsNullOrEmpty(name))
                                {
                                    importAliases.Add(new ImportAlias(name, asName));
#if SHARPPY_DEBUG
                                    Console.WriteLine($"[DEBUG] Added import alias: {name} as {asName ?? name}");
#endif
                                }
                            }
                        }

                        if (importAliases.Count > 0)
                        {
#if SHARPPY_DEBUG
                            Console.WriteLine($"[DEBUG] Creating ImportFromStatement: module={module}, level={level}, aliases={importAliases.Count}");
#endif
                            var result = new ImportFromStatement(module, importAliases, level);
                            CopySourceLocation(importFromStmt, result);
                            return result;
                        }

#if SHARPPY_DEBUG
                        Console.WriteLine("[DEBUG] Failed to parse from_import, returning placeholder");
#endif
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
                    }

                case GeneratedWith withStmt:
                    // With statement (with context_expr [as target]: body)
                    {
                        // Create with items
                        var items = new List<WithItem>();
                        if (withStmt.Items != null)
                        {
                            foreach (var item in withStmt.Items)
                            {
                                // GeneratedWithitem uses Pascal Case: ContextExpr, OptionalVars
                                var itemData = item as GeneratedWithitem;
                                if (itemData == null) continue;

                                var contextExpr = ConvertAnyExpression(itemData.ContextExpr);
                                Expression? optionalVars = itemData.OptionalVars != null ?
                                    ConvertAnyExpression(itemData.OptionalVars) : null;

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

                        var result = new WithStatement(items, bodyStmts);
                        CopySourceLocation(withStmt, result);
                        return result;
                    }

                case GeneratedAsyncWith asyncWithStmt:
                    // Async with statement (async with context_expr [as target]: body)
                    {
                        // Create with items
                        var items = new List<WithItem>();
                        if (asyncWithStmt.Items != null)
                        {
                            foreach (var item in asyncWithStmt.Items)
                            {
                                // GeneratedWithitem uses Pascal Case: ContextExpr, OptionalVars
                                var itemData = item as GeneratedWithitem;
                                if (itemData == null) continue;

                                var contextExpr = ConvertAnyExpression(itemData.ContextExpr);
                                Expression? optionalVars = itemData.OptionalVars != null ?
                                    ConvertAnyExpression(itemData.OptionalVars) : null;

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

                        var result = new AsyncWithStatement(items, bodyStmts);
                        CopySourceLocation(asyncWithStmt, result);
                        return result;
                    }

                case GeneratedMatch matchStmt:
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
                                var caseData = (GeneratedMatchCase)caseItem;

                                // Convert pattern (CPython 3.12: Use GeneratedPattern from ASDL)
                                var pattern = ConvertAnyExpression(caseData.Pattern);

                                // Convert guard (optional)
                                Expression? guard = caseData.Guard != null ?
                                    ConvertAnyExpression(caseData.Guard) : null;

                                // Convert body statements
                                var caseBodyStmts = new List<Statement>();
                                if (caseData.Body != null)
                                {
                                    foreach (var bodyStmt in caseData.Body.AsEnumerable().Cast<GeneratedStmt>())
                                    {
#if SHARPPY_DEBUG_AST_LOG
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
#if SHARPPY_DEBUG_AST_LOG
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
#if SHARPPY_DEBUG_AST_LOG
                                                    Console.WriteLine($"[DEBUG] List item not GeneratedStmt: {listItem?.GetType()?.Name}");
#endif
                                                }
                                            }
                                        }
                                        else
                                        {
#if SHARPPY_DEBUG_AST_LOG
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

                        var result = new MatchStatement(subject, cases);
                        CopySourceLocation(matchStmt, result);
                        return result;
                    }

                // TODO: Add GeneratedTypeAliasStmt case when type is available
                // case "type_alias": - removed as no GeneratedTypeAliasStmt exists yet

                default:
                    // Fallback for unhandled statement types
#if SHARPPY_DEBUG_AST_LOG
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
        /// Convert GeneratedOperator (from ASDL) to Runtime BinaryOperator
        /// CPython 3.12: Direct type mapping, no string conversion
        /// </summary>
        private static BinaryOperator ConvertGeneratedOperator(GeneratedOperator op)
        {
            return op switch
            {
                GeneratedAdd _ => Add.Instance,
                GeneratedSub _ => Sub.Instance,
                GeneratedMult _ => Mult.Instance,
                GeneratedDiv _ => Div.Instance,
                GeneratedFloorDiv _ => FloorDiv.Instance,
                GeneratedMod_ _ => Mod.Instance,  // Mod_ to avoid collision with 'mod' base type
                GeneratedPow _ => Pow.Instance,
                GeneratedLShift _ => LShift.Instance,
                GeneratedRShift _ => RShift.Instance,
                GeneratedBitOr _ => BitOr.Instance,
                GeneratedBitXor _ => BitXor.Instance,
                GeneratedBitAnd _ => BitAnd.Instance,
                GeneratedMatMult _ => MatMult.Instance,
                _ => throw new NotImplementedException($"Unknown GeneratedOperator: {op.GetType().Name}")
            };
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

            #if SHARPPY_DEBUG
            Console.WriteLine($"[DEBUG] ConvertAttributeAccess: attr='{attr}', value type={value.GetType().Name}");
            #endif

            // Convert the base object expression
            Expression valueExpr = ConvertAnyExpression(value);

            return new AttributeExpression(valueExpr, attr);
        }

        private static Expression ConvertSubscriptAccess(dynamic subscriptExpr)
        {
            // Extract value and slice from { type = "subscript", value = <obj>, slice = <index> }
            var value = subscriptExpr.value;
            var slice = subscriptExpr.slice;

            #if SHARPPY_DEBUG
            Console.WriteLine($"[DEBUG] ConvertSubscriptAccess: slice type={slice.GetType().Name}, value type={value.GetType().Name}");
            #endif

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
        /// Convert GeneratedAstNode (expr or pattern) to Expression
        /// </summary>
        private static Expression ConvertAnyExpression(GeneratedAstNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node), "Node cannot be null");
            }

            // Handle GeneratedExpr
            if (node is GeneratedExpr expr)
            {
                return ConvertGeneratedExpression(expr);
            }

            // Handle GeneratedPattern
            if (node is GeneratedPattern pattern)
            {
                return ConvertPattern(pattern);
            }

            throw new InvalidOperationException($"Cannot convert {node.GetType().Name} to Expression");
        }

        /// <summary>
        /// Convert GeneratedPattern to Expression for match statement
        /// CPython 3.12: Pattern types from ASDL
        /// </summary>
        private static Expression ConvertPattern(GeneratedPattern pattern)
        {
            return pattern switch
            {
                // MatchValue: pattern that matches a specific value
                GeneratedMatchValue mv => ConvertAnyExpression(mv.Value),

                // MatchSingleton: matches True, False, None
                GeneratedMatchSingleton ms => new ConstantExpression(ConvertGeneratedPyConstantToPyObject(ms.Value)),

                // MatchSequence: matches sequence patterns like [x, y, z]
                // MatchSequence: matches sequence patterns like [x, y, z]
                // CPython 3.12: Must use MatchSequence (pattern node), not List (expr node)
                GeneratedMatchSequence seq => new MatchSequence(
                    ConvertPatternList(seq.Patterns)
                ),

                // MatchMapping: matches dict patterns like {"key": value}
                // CPython 3.12: Must use MatchMapping (pattern node), not Dict (expr node)
                GeneratedMatchMapping map => new MatchMapping(
                    ConvertExprList(map.Keys),
                    ConvertPatternList(map.Patterns),
                    map.Rest?.ToString()
                ),

                // MatchClass: matches class patterns like Point(x=1, y=2) or Expr(value)
                // CPython 3.12: Must use MatchClass (pattern node), not CallExpression
                GeneratedMatchClass cls => new MatchClass(
                    ConvertAnyExpression(cls.Cls),
                    ConvertPatternList(cls.Patterns),
                    ConvertIdentifierListToStrings(cls.KwdAttrs),
                    ConvertPatternList(cls.KwdPatterns)
                ),

                // MatchStar: matches *rest pattern
                // CPython 3.12: MatchStar should be converted to StarPattern for pattern matching
                GeneratedMatchStar star => new StarPattern(
                    star.Name?.ToString() ?? "_"
                ),

                // MatchAs: matches pattern as name (or just name, or just wildcard)
                // CPython 3.12: MatchAs(pattern, name) | MatchAs(null, name) | MatchAs(null, null)
                //   MatchAs(pattern, name) => AsPattern(pattern, name)  # pattern as name
                //   MatchAs(null, name) => AsPattern(null, name)        # capture pattern (just name)
                //   MatchAs(null, null) => AsPattern(null, "_")         # wildcard (_)
                GeneratedMatchAs mas => new AsPattern(
                    mas.Pattern != null ? ConvertPattern(mas.Pattern) : null!,
                    mas.Name?.ToString() ?? "_"
                ),

                // MatchOr: matches pattern1 | pattern2 | ...
                GeneratedMatchOr mor => new OrPattern(
                    ConvertPatternList(mor.Patterns)
                ),

                _ => throw new NotImplementedException($"Pattern type {pattern.GetType().Name} not implemented")
            };
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
        /// Convert GeneratedExprContext to SharpPy ExprContext
        /// CPython 3.12: Load, Store, Del
        /// </summary>
        private static ExprContext ConvertContext(GeneratedExprContext genCtx)
        {
            return genCtx switch
            {
                GeneratedLoad => Load.Instance,
                GeneratedStore => Store.Instance,
                GeneratedDel => Del.Instance,
                _ => Load.Instance  // 기본값
            };
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
                GeneratedNamedExpr namedExpr => new NamedExpression(
                    ConvertAnyExpression(namedExpr.Target),
                    ConvertAnyExpression(namedExpr.Value)
                ),

                // Boolean operations
                // CPython 3.12: operators are types, not strings
                GeneratedBoolOp boolOp => new BoolOpExpression(
                    ConvertGeneratedBoolop(boolOp.Op),
                    ConvertExprList(boolOp.Values)
                ),

                GeneratedUnaryOp unaryOp => new UnaryOpExpression(
                    ConvertGeneratedUnaryop(unaryOp.Op),
                    ConvertAnyExpression(unaryOp.Operand)
                ),

                // F-strings
                GeneratedJoinedStr joinedStr => new JoinedStrExpression(
                    ConvertExprList(joinedStr.Values)
                ),

                GeneratedFormattedValue formattedValue => new FormattedValueExpression(
                    ConvertAnyExpression(formattedValue.Value),
                    formattedValue.Conversion,
                    formattedValue.FormatSpec != null ? ConvertAnyExpression(formattedValue.FormatSpec) : null
                ),

                // Basic expressions - CPython 3.12: Convert GeneratedPyConstant to PyObject
                GeneratedConstant constant => new ConstantExpression(
                    ConvertGeneratedPyConstantToPyObject(constant.Value)
                ),

                GeneratedName name => new NameExpression(name.Id, ConvertContext(name.Ctx)),

                // Binary and comparison operations
                GeneratedBinOp binOp => new BinOpExpression(
                    ConvertAnyExpression(binOp.Left),
                    ConvertGeneratedOperator(binOp.Op),  // Use GeneratedOperator → BinaryOperator converter
                    ConvertAnyExpression(binOp.Right)
                ),

                GeneratedCompare compare => new CompareExpression(
                    ConvertAnyExpression(compare.Left),
                    ConvertCmpopList(compare.Ops),  // CPython 3.12: All ops
                    ConvertExprList(compare.Comparators)  // CPython 3.12: All comparators
                ),

                // Function calls and attribute access
                GeneratedCall call => ConvertCallExpression(call),

                GeneratedAttribute attr => new AttributeExpression(
                    ConvertAnyExpression(attr.Value),
                    attr.Attr,
                    ConvertContext(attr.Ctx)
                ),

                GeneratedSubscript subscript => new SubscriptExpression(
                    ConvertAnyExpression(subscript.Value),
                    ConvertAnyExpression(subscript.Slice),
                    ConvertContext(subscript.Ctx)
                ),

                // Collections
                GeneratedList list => new ListExpression(
                    ConvertExprList(list.Elts),
                    ConvertContext(list.Ctx)
                ),

                GeneratedTuple tuple => new TupleExpression(
                    ConvertExprList(tuple.Elts),
                    ConvertContext(tuple.Ctx)
                ),

                GeneratedDict dict => new DictExpression(
                    ConvertDictPairs(dict.Keys, dict.Values)
                ),

                GeneratedSet set => new SetExpression(
                    ConvertExprList(set.Elts)
                ),

                // Comprehensions
                GeneratedListComp listComp => new ListComprehension(
                    ConvertAnyExpression(listComp.Elt),
                    ConvertComprehensionList(listComp.Generators)
                ),

                GeneratedSetComp setComp => new SetComprehension(
                    ConvertAnyExpression(setComp.Elt),
                    ConvertComprehensionList(setComp.Generators)
                ),

                GeneratedDictComp dictComp => new DictComprehension(
                    ConvertAnyExpression(dictComp.Key),
                    ConvertAnyExpression(dictComp.Value),
                    ConvertComprehensionList(dictComp.Generators)
                ),

                GeneratedGeneratorExp genExp => new GeneratorExpression(
                    ConvertAnyExpression(genExp.Elt),
                    ConvertComprehensionList(genExp.Generators)
                ),

                // Lambda expressions
                // CPython 3.12: Python/compile.c:2590-2650 (compiler_lambda)
                GeneratedLambda lambda => new LambdaExpression(
                    ConvertFunctionArgumentsToNames(lambda.Args),
                    ConvertAnyExpression(lambda.Body),
                    ConvertFunctionArgumentsDefaults(lambda.Args)
                ),

                // Ternary (if expression)
                GeneratedIfExp ifExp => new ConditionalExpression(
                    ConvertAnyExpression(ifExp.Test),
                    ConvertAnyExpression(ifExp.Body),
                    ConvertAnyExpression(ifExp.Orelse)
                ),

                // Async/await expressions
                GeneratedAwait awaitExpr => new AwaitExpression(
                    ConvertAnyExpression(awaitExpr.Value)
                ),

                GeneratedYield yieldExpr => new YieldExpression(
                    yieldExpr.Value != null ? ConvertAnyExpression(yieldExpr.Value) : null
                ),

                GeneratedYieldFrom yieldFrom => new YieldFromExpression(
                    ConvertAnyExpression(yieldFrom.Value)
                ),

                // Starred expression (unpacking)
                GeneratedStarred starred => new StarredExpression(
                    ConvertAnyExpression(starred.Value),
                    ConvertContext(starred.Ctx)
                ),

                // Slice expression
                GeneratedSlice slice => new SliceExpression(
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
        /// <summary>
        /// Convert GeneratedUnaryop (from ASDL) to Runtime UnaryOperator
        /// CPython 3.12: Direct type mapping
        /// </summary>
        private static UnaryOperator ConvertGeneratedUnaryop(GeneratedUnaryop op)
        {
            return op switch
            {
                GeneratedNot _ => Not.Instance,
                GeneratedUAdd _ => UAdd.Instance,
                GeneratedUSub _ => USub.Instance,
                GeneratedInvert _ => Invert.Instance,
                _ => throw new NotImplementedException($"Unknown GeneratedUnaryop: {op.GetType().Name}")
            };
        }

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
        /// Convert GeneratedBoolop (from ASDL) to Runtime BoolOperator
        /// CPython 3.12: Direct type mapping
        /// </summary>
        private static BoolOperator ConvertGeneratedBoolop(GeneratedBoolop op)
        {
            return op switch
            {
                GeneratedAnd _ => And.Instance,
                GeneratedOr _ => Or.Instance,
                _ => throw new NotImplementedException($"Unknown GeneratedBoolop: {op.GetType().Name}")
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
        /// Convert GeneratedCall to CallExpression with proper null checks
        /// </summary>
        private static CallExpression ConvertCallExpression(GeneratedCall call)
        {
#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] ConvertCallExpression: Func={call.Func != null}, Args={call.Args != null}, Keywords={call.Keywords != null}");
#endif

            // Convert function expression
            var funcExpr = ConvertAnyExpression(call.Func);
#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] ConvertCallExpression: funcExpr converted successfully");
#endif

            // Convert arguments
            var args = new List<Expression>();
            if (call.Args != null)
            {
#if SHARPPY_DEBUG_AST_LOG
                Console.WriteLine($"[DEBUG] ConvertCallExpression: Converting {call.Args.Count} args");
#endif
                foreach (var arg in call.Args.ToEnumerable<GeneratedExpr>())
                {
                    if (arg != null)
                    {
                        args.Add(ConvertAnyExpression(arg));
                    }
                }
            }

            // Convert keyword arguments
            var keywords = new List<KeywordExpression>();
            if (call.Keywords != null)
            {
#if SHARPPY_DEBUG_AST_LOG
                Console.WriteLine($"[DEBUG] ConvertCallExpression: Converting {call.Keywords.Count} keywords");
#endif
                foreach (var kw in call.Keywords.ToEnumerable<GeneratedKeyword>())
                {
                    if (kw != null)
                    {
                        keywords.Add(ConvertKeyword(kw));
                    }
                }
            }

#if SHARPPY_DEBUG_AST_LOG
            Console.WriteLine($"[DEBUG] ConvertCallExpression: Creating CallExpression with {args.Count} args, {keywords.Count} keywords");
#endif
            return new CallExpression(funcExpr, args, keywords);
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
            var ifs = ConvertExprList(comp.Ifs);

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

            // Performance: Eliminated LINQ - manual list conversions
            var posOnlyArgs = new List<Arg>();
            if (argsObj.Posonlyargs != null)
            {
                for (int i = 0; i < argsObj.Posonlyargs.Count; i++)
                {
                    if (argsObj.Posonlyargs[i] is GeneratedArg a)
                    {
                        posOnlyArgs.Add(ConvertArg(a));
                    }
                }
            }

            var args = new List<Arg>();
            if (argsObj.Args != null)
            {
                for (int i = 0; i < argsObj.Args.Count; i++)
                {
                    if (argsObj.Args[i] is GeneratedArg a)
                    {
                        args.Add(ConvertArg(a));
                    }
                }
            }

            var kwOnlyArgs = new List<Arg>();
            if (argsObj.Kwonlyargs != null)
            {
                for (int i = 0; i < argsObj.Kwonlyargs.Count; i++)
                {
                    if (argsObj.Kwonlyargs[i] is GeneratedArg a)
                    {
                        kwOnlyArgs.Add(ConvertArg(a));
                    }
                }
            }

            var defaults = new List<Expression?>();
            if (argsObj.Defaults != null)
            {
                for (int i = 0; i < argsObj.Defaults.Count; i++)
                {
                    if (argsObj.Defaults[i] is GeneratedExpr d)
                    {
                        defaults.Add(ConvertAnyExpression(d));
                    }
                }
            }

            var kwDefaults = new List<Expression?>();
            if (argsObj.KwDefaults != null)
            {
                for (int i = 0; i < argsObj.KwDefaults.Count; i++)
                {
                    if (argsObj.KwDefaults[i] != null && argsObj.KwDefaults[i] is GeneratedExpr d)
                    {
                        kwDefaults.Add(ConvertAnyExpression(d));
                    }
                    else
                    {
                        kwDefaults.Add(null);
                    }
                }
            }

            return new FunctionArguments
            {
                PosOnlyArgs = posOnlyArgs,
                Args = args,
                VarArg = argsObj.Vararg != null ? ConvertArg(argsObj.Vararg) : null,
                KwOnlyArgs = kwOnlyArgs,
                KwArg = argsObj.Kwarg != null ? ConvertArg(argsObj.Kwarg) : null,
                Defaults = defaults,
                KwDefaults = kwDefaults
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
        // via concrete typed expressions (GeneratedList, GeneratedTuple, etc.)

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

            #if SHARPPY_DEBUG
            Console.WriteLine($"[DEBUG] ConvertExpression: Converting {expr.GetType()}");
            #endif

            // Return a simple constant for now
            return new ConstantExpression(new PyStr("expr"));
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

        // NOTE: This function was removed as it was dead code.
        // String literal processing (including escape sequences) is now handled by:
        // - PyParserRuntime_ActionHelpers.DecodeStringLiteral() for quote removal and escape processing
        // - Converted to PyStr via GeneratedPyConstantString at line 2895
        //
        // TODO (Future work, not related to escape sequences):
        // - Bytes literal escape handling (currently uses UTF-8 encoding)
        // - F-string parsing (currently treats as regular strings)

        /// <summary>
        /// Helper method to convert default values to string representation
        /// </summary>
        private static string ConvertDefaultToString(object defaultValue)
        {
            if (defaultValue == null) return "None";

            #if SHARPPY_DEBUG
            Console.WriteLine($"[DEBUG] ConvertDefaultToString: {defaultValue} (Type: {defaultValue.GetType().Name})");
            #endif

            // Handle GeneratedConstant directly
            if (defaultValue is GeneratedConstant constantExpr)
            {
                var value = constantExpr.Value;
                #if SHARPPY_DEBUG
                Console.WriteLine($"[DEBUG] GeneratedConstant value: {value} (Type: {value?.GetType().Name})");
                #endif

                // CPython 3.12: Value is now GeneratedPyConstant (AST layer)
                return value switch
                {
                    GeneratedPyConstantNone => "None",
                    GeneratedPyConstantBool b => b.Value ? "True" : "False",
                    GeneratedPyConstantInt i => i.Value.ToString(),
                    GeneratedPyConstantFloat f => f.Value.ToString(),
                    GeneratedPyConstantString s => $"\"{s.Value}\"",
                    GeneratedPyConstantBytes bytes => $"b\"{System.Text.Encoding.UTF8.GetString(bytes.Value)}\"",
                    GeneratedPyConstantEllipsis => "...",
                    _ => value.ToString() ?? "None"
                };
            }

            // Handle other GeneratedExpr types (recurse through conversion)
            if (defaultValue is GeneratedExpr genExpr)
            {
                #if SHARPPY_DEBUG
                Console.WriteLine($"[DEBUG] GeneratedExpr type: {genExpr.GetType().Name}");
                #endif

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
                            else if (pyObj is PyStr pyStr) rawValue = pyStr.Value;
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
                    #if SHARPPY_DEBUG
                    Console.WriteLine($"[DEBUG] Error converting GeneratedExpr: {ex.Message}");
                    #endif
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
            // Performance: Eliminated LINQ - manual list iteration
            for (int i = 0; i < funcArgs.PosOnlyArgs.Count; i++)
            {
                names.Add(funcArgs.PosOnlyArgs[i].Name);
            }
            for (int i = 0; i < funcArgs.Args.Count; i++)
            {
                names.Add(funcArgs.Args[i].Name);
            }
            if (funcArgs.VarArg != null)
                names.Add("*" + funcArgs.VarArg.Name);
            for (int i = 0; i < funcArgs.KwOnlyArgs.Count; i++)
            {
                names.Add(funcArgs.KwOnlyArgs[i].Name);
            }
            if (funcArgs.KwArg != null)
                names.Add("**" + funcArgs.KwArg.Name);

            return names;
        }

        /// <summary>
        /// Convert GeneratedArguments.Defaults to List&lt;Expression&gt; for Lambda expressions
        /// CPython 3.12: Python/ast.c - ast_for_arguments()
        /// </summary>
        private static List<Expression> ConvertFunctionArgumentsDefaults(GeneratedArguments? argumentsData)
        {
            var defaults = new List<Expression>();
            if (argumentsData?.Defaults == null)
                return defaults;

            // GeneratedExprSeq iterates as GeneratedPtr, need explicit cast to GeneratedExpr
            for (int i = 0; i < argumentsData.Defaults.Count; i++)
            {
                var defaultExpr = (GeneratedExpr)argumentsData.Defaults[i];
                defaults.Add(ConvertAnyExpression(defaultExpr));
            }
            return defaults;
        }

        /// <summary>
        /// PEP 695: Convert GeneratedTypeParamSeq to List<TypeParam>
        /// Converts Generated AST type parameters to SharpPy AST type parameters
        /// </summary>
        private static List<TypeParam> ConvertTypeParams(GeneratedTypeParamSeq? typeParamSeq)
        {
            var result = new List<TypeParam>();

            if (typeParamSeq == null || typeParamSeq.Count == 0)
                return result;

            foreach (var genTypeParam in typeParamSeq)
            {
                TypeParam converted = genTypeParam switch
                {
                    GeneratedTypeVar typeVar => new TypeVarParam(
                        typeVar.Name.Value,
                        typeVar.Bound != null ? ConvertAnyExpression(typeVar.Bound) : null
                    ),
                    GeneratedParamSpec paramSpec => new ParamSpecParam(
                        paramSpec.Name.Value
                    ),
                    GeneratedTypeVarTuple typeVarTuple => new TypeVarTupleParam(
                        typeVarTuple.Name.Value
                    ),
                    _ => throw new NotImplementedException($"Unknown GeneratedTypeParam type: {genTypeParam.GetType().Name}")
                };

                result.Add(converted);
            }

            return result;
        }

        /// <summary>
        /// Convert GeneratedPyConstant (AST layer) to PyObject (Runtime layer)
        /// CPython 3.12: Bridges AST constant values to runtime objects
        /// </summary>
        private static PyObject ConvertGeneratedPyConstantToPyObject(GeneratedPyConstant value)
        {
            // CPython 3.12: Objects/complexobject.c - complex number constant handling
            return value switch
            {
                GeneratedPyConstantNone => PyNone.Instance,
                GeneratedPyConstantBool b => b.Value ? PyBool.True : PyBool.False,
                GeneratedPyConstantInt i => new PyInt(i.Value),
                GeneratedPyConstantFloat f => new PyFloat(f.Value),
                GeneratedPyConstantString s => new PyStr(s.Value),
                GeneratedPyConstantBytes bytes => new PyBytes(bytes.Value),
                GeneratedPyConstantComplex c => new PyComplex(c.Real, c.Imag),  // CPython 3.12: Parser/action_helpers.c:785
                GeneratedPyConstantEllipsis => PyEllipsis.Instance,  // CPython 3.12: Objects/sliceobject.c - _PyEllipsis_Type
                _ => throw new NotImplementedException($"Unknown GeneratedPyConstant type: {value.GetType().Name}")
            };
        }

        /// <summary>
        /// CPython 3.12: Parse constant value from PEG parser output
        /// Converts string representations to proper PyObject types
        /// </summary>
        private static PyObject ParseConstantValue(PyObject value, string? kind)
        {
            // If already a PyObject (not string), return as-is
            if (value is not PyStr strValue)
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
                return new PyStr(str);
            }

            // Fallback: keep as string
            return value;
        }

        /// <summary>
        /// Overload for object type (from GeneratedConstant.Value)
        /// </summary>
        private static PyObject ParseConstantValue(object? value, string? kind)
        {
            if (value == null)
                return PyNone.Instance;

            if (value is PyObject pyObj)
                return pyObj;

            // Convert to PyObject first, then parse
            return ParseConstantValue(ToPyObject(value), kind);
        }

        private static PyObject ToPyObject(object value)
        {
            return value switch
            {
                PyObject pyObj => pyObj,
                string str => new PyStr(str),
                int i => new PyInt(i),
                long l => new PyInt((int)l),
                double d => new PyFloat(d),
                bool b => b ? PyBool.True : PyBool.False,
                null => PyNone.Instance,
                _ => new PyStr(value.ToString() ?? "")
            };
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
                string str => new PyStr(str),
                int i => new PyInt(i),
                long l => new PyInt((int)l), // Cast long to int (may overflow for large values)
                double d => new PyFloat(d),
                float f => new PyFloat(f),
                bool b => b ? PyBool.True : PyBool.False,
                byte[] bytes => new PyBytesObject(bytes),
                _ => new PyStr(value.ToString() ?? "")
            };
        }

        // Performance: Eliminated LINQ - Helper methods for manual list conversions

        /// <summary>
        /// Convert GeneratedSeq of patterns to List<Expression>
        /// Replaces: seq?.ToEnumerable<GeneratedPattern>().Select(p => ConvertPattern(p)).ToList()
        /// </summary>
        private static List<Expression> ConvertPatternList(GeneratedSeq seq)
        {
            var result = new List<Expression>();
            if (seq != null)
            {
                for (int i = 0; i < seq.Count; i++)
                {
                    if (seq[i] is GeneratedPattern p)
                    {
                        result.Add(ConvertPattern(p));
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Convert GeneratedSeq of expressions to List<Expression>
        /// Replaces: seq?.ToEnumerable<GeneratedExpr>().Select(e => ConvertAnyExpression(e)).ToList()
        /// </summary>
        private static List<Expression> ConvertExprList(GeneratedSeq seq)
        {
            var result = new List<Expression>();
            if (seq != null)
            {
                for (int i = 0; i < seq.Count; i++)
                {
                    if (seq[i] is GeneratedExpr e)
                    {
                        result.Add(ConvertAnyExpression(e));
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Convert GeneratedSeq of identifiers to List<string>
        /// Replaces: seq?.ToEnumerable<GeneratedIdentifier>().Select(k => k.ToString()!).ToList()
        /// </summary>
        private static List<string> ConvertIdentifierListToStrings(GeneratedSeq seq)
        {
            var result = new List<string>();
            if (seq != null)
            {
                for (int i = 0; i < seq.Count; i++)
                {
                    if (seq[i] is GeneratedIdentifier id)
                    {
                        result.Add(id.ToString()!);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Convert GeneratedSeq of comprehensions to List<Comprehension>
        /// Replaces: seq.ToEnumerable<GeneratedComprehension>().Select(g => ConvertComprehension(g)).ToList()
        /// </summary>
        private static List<Comprehension> ConvertComprehensionList(GeneratedSeq seq)
        {
            var result = new List<Comprehension>();
            for (int i = 0; i < seq.Count; i++)
            {
                if (seq[i] is GeneratedComprehension c)
                {
                    result.Add(ConvertComprehension(c));
                }
            }
            return result;
        }

        /// <summary>
        /// Convert parallel GeneratedSeq keys and values to List of tuples
        /// Replaces: Keys.Zip(Values, (k, v) => (Key: ..., Value: ...)).ToList()
        /// CPython 3.12: key가 None인 경우 dict unpacking (**expr)을 나타냄
        /// </summary>
        private static List<(Expression Key, Expression Value)> ConvertDictPairs(GeneratedSeq keys, GeneratedSeq values)
        {
            var result = new List<(Expression, Expression)>();
            if (keys != null && values != null)
            {
                int count = Math.Min(keys.Count, values.Count);
                for (int i = 0; i < count; i++)
                {
                    // CPython 3.12: key가 None이면 **unpacking (ex: {**d})
                    // Python AST에서 Dict(keys=[None], values=[d]) 형태로 표현됨
                    if (values[i] is GeneratedExpr v)
                    {
                        Expression? keyExpr = null;
                        if (keys[i] is GeneratedExpr k)
                        {
                            keyExpr = ConvertAnyExpression(k);
                        }
                        // key가 null이면 keyExpr은 null 유지 (dict unpacking 표시)
                        result.Add((keyExpr!, ConvertAnyExpression(v)));
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Convert GeneratedSeq of cmpops to List<GeneratedCmpop>
        /// Replaces: seq.ToEnumerable<GeneratedCmpop>().ToList()
        /// </summary>
        private static List<GeneratedCmpop> ConvertCmpopList(GeneratedSeq seq)
        {
            var result = new List<GeneratedCmpop>();
            for (int i = 0; i < seq.Count; i++)
            {
                if (seq[i] is GeneratedCmpop c)
                {
                    result.Add(c);
                }
            }
            return result;
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