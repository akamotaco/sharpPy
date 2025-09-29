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

#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] Module statement {i}: Type='{stmt.StatementType}', Value={stmt.Value}");
#endif

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
                    bool isNameReference = false;

                    if (valueExpr is GeneratedExpr genExpr)
                    {
                        isNameReference = genExpr.ExpressionType == "Name";
                    }
                    else
                    {
                        // Legacy dynamic object handling
                        dynamic valueDynamic = valueExpr;
                        try
                        {
                            isNameReference = valueDynamic.type?.ToString() == "name";
                        }
                        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                        {
                            isNameReference = false;
                        }
                    }

                    if (!isNameReference)
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
                    string? targetName = null;

                    if (target is GeneratedExpr genExpr && genExpr.ExpressionType == "Name")
                    {
                        if (genExpr.Value is object valueObj)
                        {
                            var valueProperty = valueObj.GetType().GetProperty("value");
                            targetName = valueProperty?.GetValue(valueObj)?.ToString();
                        }
                    }
                    else
                    {
                        // Legacy dynamic object handling
                        dynamic targetDynamic = target;
                        try
                        {
                            if (targetDynamic.type?.ToString() == "name")
                            {
                                targetName = targetDynamic.value?.ToString();
                            }
                        }
                        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                        {
                            // Ignore and continue
                        }
                    }

                    if (!string.IsNullOrEmpty(targetName))
                    {
                        targetNames.Add(targetName);
#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] Added target name: '{targetName}', Total targets: {targetNames.Count}");
#endif
                    }
                }
            }

            // Second pass: find the actual value (in the last statement)
            var lastStmt = chainGroup[chainGroup.Count - 1];
            var lastAssignmentData = lastStmt.Value as dynamic;
            var lastValue = lastAssignmentData?.Value;

#if DEBUG_LOG
            string lastValueType = "null";
            if (lastValue != null)
            {
                if (lastValue is GeneratedExpr genExpr)
                {
                    lastValueType = genExpr.ExpressionType;
                }
                else
                {
                    try
                    {
                        lastValueType = (lastValue as dynamic).type?.ToString() ?? "unknown";
                    }
                    catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                    {
                        lastValueType = "no-type-property";
                    }
                }
            }
            Console.WriteLine($"[DEBUG] Last statement value: '{lastValue}', ValueType='{lastValueType}'");
#endif

            if (lastValue != null)
            {
                bool isNameValue = false;

                if (lastValue is GeneratedExpr genExpr)
                {
                    isNameValue = genExpr.ExpressionType == "Name";
                }
                else
                {
                    // Legacy dynamic object handling
                    dynamic lastValueDynamic = lastValue;
                    try
                    {
                        isNameValue = lastValueDynamic.type?.ToString() == "name";
                    }
                    catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                    {
                        isNameValue = false;
                    }
                }

                if (!isNameValue)
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

                case "annassign":
                    // Annotated assignment statement (name: type = value or name: type)
                    if (stmt.Value != null)
                    {
                        try
                        {
                            // Expected structure: { target: expr, annotation: expr, value: expr? }
                            var target = stmt.Value.GetType().GetProperty("target")?.GetValue(stmt.Value);
                            var annotation = stmt.Value.GetType().GetProperty("annotation")?.GetValue(stmt.Value);
                            var value = stmt.Value.GetType().GetProperty("value")?.GetValue(stmt.Value);

#if DEBUG_LOG
                            Console.WriteLine($"[DEBUG] ConvertStatement AnnAssign: target={target}, annotation={annotation}, value={value}");
#endif

                            // Convert target (should be a name)
                            string targetName = null;
                            if (target is GeneratedExpr targetExpr && targetExpr.ExpressionType == "Name")
                            {
                                targetName = targetExpr.GetType().GetProperty("id")?.GetValue(targetExpr) as string;
                            }

                            if (targetName == null)
                            {
#if DEBUG_LOG
                                Console.WriteLine($"[DEBUG] ConvertStatement AnnAssign: Failed to extract target name from {target}");
#endif
                                return null;
                            }

                            // Convert annotation
                            Expression annotationExpr = null;
                            if (annotation is GeneratedExpr annotationGeneratedExpr)
                            {
                                annotationExpr = ConvertAnyExpression(annotationGeneratedExpr);
                            }

                            // Convert value (optional)
                            Expression valueExpr = null;
                            if (value != null && value is GeneratedExpr valueGeneratedExpr)
                            {
                                valueExpr = ConvertAnyExpression(valueGeneratedExpr);
                            }

#if DEBUG_LOG
                            Console.WriteLine($"[DEBUG] ConvertStatement AnnAssign: Creating AnnAssignStatement with name='{targetName}', annotation={annotationExpr}, value={valueExpr}");
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
                    return null;

                case "assignment":
                    // Assignment statement (name = value)
                    if (stmt.Value != null)
                    {
                        var assignmentData = stmt.Value as dynamic;
                        var target = assignmentData?.Target;
                        var valueExpr = assignmentData?.Value;

#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Target='{target}', Value='{valueExpr}', ValueType={valueExpr?.GetType()}");
                        Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Target Type='{target?.GetType()}', is GeneratedExpr: {target is GeneratedExpr}");
                        if (target is GeneratedExpr genExprDebug)
                        {
                            Console.WriteLine($"[DEBUG] ConvertStatement Assignment: GeneratedExpr.ExpressionType='{genExprDebug.ExpressionType}'");
                        }
#endif

                        if (target != null && valueExpr != null)
                        {
                            // Extract variable name from target object
                            string? targetName = null;

                            // Handle GeneratedExpr target
                            if (target is GeneratedExpr genExpr)
                            {
                                if (genExpr.ExpressionType == "Name")
                                {
#if DEBUG_LOG
                                    Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Found GeneratedExpr target with ExpressionType='Name'");
#endif
                                    if (genExpr.Value is object valueObj)
                                    {
                                        var valueProperty = valueObj.GetType().GetProperty("value");
                                        targetName = valueProperty?.GetValue(valueObj)?.ToString();
#if DEBUG_LOG
                                        Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Extracted targetName='{targetName}' from GeneratedExpr");
#endif
                                    }
                                }
                                else if (genExpr.ExpressionType == "Attribute")
                                {
#if DEBUG_LOG
                                    Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Found GeneratedExpr target with ExpressionType='Attribute' (CPython 3.12 compatible)");
#endif
                                    // Handle attribute assignment: self.x = value
                                    // Convert to AttributeStatement instead of AssignStatement
                                    try
                                    {
                                        var attrExpr = ConvertAnyExpression(genExpr);
                                        if (attrExpr is AttributeExpression attributeExpr)
                                        {
#if DEBUG_LOG
                                            Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Converting attribute assignment {attributeExpr.Value}.{attributeExpr.Attr}");
#endif
                                            Expression convertedValueExpr = ConvertAnyExpression(valueExpr);
                                            return new AttributeStatement(attributeExpr.Value, attributeExpr.Attr, convertedValueExpr);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
#if DEBUG_LOG
                                        Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Failed to convert attribute assignment: {ex.Message}");
#endif
                                    }
                                }
                                else if (genExpr.ExpressionType == "Expression")
                                {
#if DEBUG_LOG
                                    Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Found GeneratedExpr target with ExpressionType='Expression', unwrapping...");
#endif

                                    // Check if this Expression wrapper contains an Attribute
                                    if (genExpr.Value is GeneratedExpr innerExpr)
                                    {
                                        if (innerExpr.ExpressionType == "Attribute")
                                        {
#if DEBUG_LOG
                                            Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Found wrapped Attribute in Expression (CPython 3.12 compatible)");
#endif
                                            // Handle wrapped attribute assignment: self.x = value
                                            try
                                            {
                                                var attrExpr = ConvertAnyExpression(innerExpr);
                                                if (attrExpr is AttributeExpression attributeExpr)
                                                {
#if DEBUG_LOG
                                                    Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Converting wrapped attribute assignment {attributeExpr.Value}.{attributeExpr.Attr}");
#endif
                                                    Expression convertedValueExpr = ConvertAnyExpression(valueExpr);
                                                    return new AttributeStatement(attributeExpr.Value, attributeExpr.Attr, convertedValueExpr);
                                                }
                                            }
                                            catch (Exception ex)
                                            {
#if DEBUG_LOG
                                                Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Failed to convert wrapped attribute assignment: {ex.Message}");
#endif
                                            }
                                        }
                                        else if (innerExpr.ExpressionType == "Name")
                                        {
                                            if (innerExpr.Value is object valueObj)
                                            {
                                                var valueProperty = valueObj.GetType().GetProperty("value");
                                                targetName = valueProperty?.GetValue(valueObj)?.ToString();
#if DEBUG_LOG
                                                Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Extracted targetName='{targetName}' from wrapped GeneratedExpr");
#endif
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Try to extract from the wrapper's Value using ConvertAnyExpression
                                        try
                                        {
                                            var unwrappedExpr = ConvertAnyExpression(genExpr.Value);
                                            if (unwrappedExpr is NameExpression nameExpr)
                                            {
                                                targetName = nameExpr.Name;
#if DEBUG_LOG
                                                Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Extracted targetName='{targetName}' from converted NameExpression");
#endif
                                            }
                                            else if (unwrappedExpr is AttributeExpression attrExpr)
                                            {
#if DEBUG_LOG
                                                Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Found AttributeExpression via unwrapping");
#endif
                                                Expression convertedValueExpr = ConvertAnyExpression(valueExpr);
                                                return new AttributeStatement(attrExpr.Value, attrExpr.Attr, convertedValueExpr);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
#if DEBUG_LOG
                                            Console.WriteLine($"[DEBUG] ConvertStatement Assignment: Failed to unwrap Expression: {ex.Message}");
#endif
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Handle legacy target object with { type = "name", value = "variable_name" } structure
                                if (target is object targetObj)
                                {
                                    var targetType = targetObj.GetType();
                                    try
                                    {
                                        var typeProperty = targetType.GetProperty("type");
                                        var valueProperty = targetType.GetProperty("value");

                                        if (typeProperty != null && valueProperty != null)
                                        {
                                            var typeValue = typeProperty.GetValue(targetObj)?.ToString();
                                            if (typeValue == "name")
                                            {
                                                targetName = valueProperty.GetValue(targetObj)?.ToString();
                                            }
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        // Ignore property access errors for legacy objects
                                    }
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

                case "aug_assign":
                    // Augmented assignment statement (name += value)
                    if (stmt.Value != null)
                    {
                        var augAssignData = stmt.Value as dynamic;
                        var target = augAssignData?.Target;
                        var op = augAssignData?.Op;
                        var value = augAssignData?.Value;

#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Target='{target}', Op='{op}', Value='{value}'");
#endif

                        // Extract target name
                        string targetName = null;
                        if (target is GeneratedExpr genExpr)
                        {
                            if (genExpr.ExpressionType == "Name" && genExpr.Value is { } nameValue)
                            {
                                dynamic dynNameValue = nameValue;
                                targetName = dynNameValue.id?.ToString();
#if DEBUG_LOG
                                Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Extracted targetName='{targetName}' from Name expression");
#endif
                            }
                            else if (genExpr.ExpressionType == "Expression" && genExpr.Value is { } exprValue)
                            {
                                dynamic dynExprValue = exprValue;
                                targetName = dynExprValue.name?.ToString();
#if DEBUG_LOG
                                Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Extracted targetName='{targetName}' from Expression");
#endif
                            }
                        }
                        else if (target is string strTarget)
                        {
                            targetName = strTarget;
#if DEBUG_LOG
                            Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Using string target='{targetName}'");
#endif
                        }
                        else
                        {
                            // Handle anonymous object case: { kind = "Name", id = "x" }
                            dynamic dynTarget = target;
                            if (dynTarget?.kind == "Name" && dynTarget?.id != null)
                            {
                                targetName = dynTarget.id.ToString();
#if DEBUG_LOG
                                Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Extracted targetName='{targetName}' from anonymous object");
#endif
                            }
                        }

                        // Extract operator
                        string operatorType = null;
                        if (op is { } opValue)
                        {
                            dynamic dynOp = opValue;
                            var opKind = dynOp.kind?.ToString();

                            // Map from operator kind to operator symbol
                            operatorType = opKind switch
                            {
                                "Add" => "+=",
                                "Sub" => "-=",
                                "Mult" => "*=",
                                "Div" => "/=",
                                "Mod" => "%=",
                                "Pow" => "**=",
                                "FloorDiv" => "//=",
                                "LShift" => "<<=",
                                "RShift" => ">>=",
                                "BitOr" => "|=",
                                "BitXor" => "^=",
                                "BitAnd" => "&=",
                                "MatMult" => "@=",
                                _ => opKind // fallback to original
                            };

#if DEBUG_LOG
                            Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Mapped operator '{opKind}' to '{operatorType}'");
#endif
                        }

                        // Convert value expression
                        if (targetName != null && operatorType != null && value != null)
                        {
                            var convertedValueExpr = ConvertAnyExpression(value);
                            if (convertedValueExpr != null)
                            {
#if DEBUG_LOG
                                Console.WriteLine($"[DEBUG] ConvertStatement AugAssign: Creating AugAssignStatement with target='{targetName}', op='{operatorType}'");
#endif
                                return new AugAssignStatement(targetName, operatorType, convertedValueExpr);
                            }
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "ann_assign":
                    // Annotated assignment statement (name: type = value)
                    if (stmt.Value != null)
                    {
                        var annAssignData = stmt.Value as dynamic;
                        var target = annAssignData?.Target;
                        var annotation = annAssignData?.Annotation;
                        var valueExpr = annAssignData?.Value;

#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] ConvertStatement AnnAssign: Target='{target}', Annotation='{annotation}', Value='{valueExpr}'");
#endif

                        if (target != null && annotation != null)
                        {
                            // Extract variable name from target object (similar to assignment case)
                            string? targetName = null;

                            if (target is GeneratedExpr genExpr)
                            {
                                if (genExpr.ExpressionType == "Name")
                                {
                                    if (genExpr.Value is object valueObj)
                                    {
                                        var valueProperty = valueObj.GetType().GetProperty("value");
                                        targetName = valueProperty?.GetValue(valueObj)?.ToString();
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(targetName))
                            {
                                // Convert annotation expression
                                Expression annotationExpression = ConvertAnyExpression(annotation);

                                // Convert value expression if present
                                Expression? valueExpression = null;
                                if (valueExpr != null)
                                {
                                    valueExpression = ConvertAnyExpression(valueExpr);
                                }

#if DEBUG_LOG
                                Console.WriteLine($"[DEBUG] ConvertStatement AnnAssign Final: Target='{targetName}', HasValue={valueExpression != null}");
#endif

                                // Create annotated assignment statement
                                return new AnnAssignStatement(targetName, annotationExpression, valueExpression);
                            }
                        }
                    }
                    return new ExpressionStatement(new ConstantExpression(PyNone.Instance));

                case "expression":
                    // Expression statement (standalone expression)
                    if (stmt.Value != null)
                    {
                        Console.WriteLine($"[DEBUG] ConvertStatement Expression: stmt.Value type = {stmt.Value.GetType().Name}, value = {stmt.Value}");
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
                        var returnData = stmt.Value;

                        // Handle GeneratedExpr directly
                        if (returnData is GeneratedExpr genExpr && genExpr.Value != null)
                        {
                            returnValue = ConvertAnyExpression(genExpr.Value);
                        }
                        // Handle dynamic objects with .value property
                        else if (returnData is not GeneratedExpr)
                        {
                            var dynamicData = returnData as dynamic;
                            if (dynamicData?.value != null)
                            {
                                returnValue = ConvertAnyExpression(dynamicData.value);
                            }
                        }
                    }

                    // If no value provided, return None
                    returnValue ??= new ConstantExpression(PyNone.Instance);

                    return new ReturnStatement(returnValue);

                case "raise":
                    // Raise statement (raise [expression] [from expression])
                    Expression? exceptionExpr = null;
                    Expression? fromExpr = null;

                    if (stmt.Value != null)
                    {
                        var raiseData = stmt.Value as dynamic;

                        // Handle ExceptionExpr
                        if (raiseData.ExceptionExpr != null)
                        {
                            if (raiseData.ExceptionExpr is GeneratedExpr genExpr)
                            {
                                exceptionExpr = ConvertAnyExpression(genExpr);
                            }
                        }

                        // Handle FromExpr (for "raise ... from ..." syntax)
                        if (raiseData.FromExpr != null)
                        {
                            if (raiseData.FromExpr is GeneratedExpr fromGenExpr)
                            {
                                fromExpr = ConvertAnyExpression(fromGenExpr);
                            }
                        }
                    }

                    return new RaiseStatement(exceptionExpr, fromExpr);

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
                                if (bodyStmt is GeneratedStmt generatedStmt)
                                {
                                    var convertedStmt = ConvertStatement(generatedStmt, insideLoop, insideFunction);
                                    if (convertedStmt != null)
                                        bodyStmts.Add(convertedStmt);
                                }
                                else if (bodyStmt is System.Collections.IEnumerable enumerable && !(bodyStmt is string))
                                {
                                    // Handle nested list of statements
                                    foreach (var nestedStmt in enumerable)
                                    {
                                        if (nestedStmt is GeneratedStmt nestedGeneratedStmt)
                                        {
                                            var convertedStmt = ConvertStatement(nestedGeneratedStmt, insideLoop, insideFunction);
                                            if (convertedStmt != null)
                                                bodyStmts.Add(convertedStmt);
                                        }
                                    }
                                }
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

                        // Extract target variable name from GeneratedExpr
                        string targetVar = "i"; // default
                        if (forData.target is GeneratedExpr targetExpr && targetExpr.ExpressionType == "Name")
                        {
                            if (targetExpr.Value is object targetValue)
                            {
                                var valueType = targetValue.GetType();
                                var idProperty = valueType.GetProperty("id");
                                var valueProperty = valueType.GetProperty("value");

                                if (idProperty != null)
                                {
                                    targetVar = idProperty.GetValue(targetValue)?.ToString() ?? "i";
                                }
                                else if (valueProperty != null)
                                {
                                    targetVar = valueProperty.GetValue(targetValue)?.ToString() ?? "i";
                                }
                            }
                        }

                        // Convert iterable expression
                        Expression iterableExpr = ConvertAnyExpression(forData.iter);

                        // Convert body statements
                        var bodyStmts = new List<Statement>();
                        if (forData.body != null)
                        {
                            foreach (var bodyStmt in forData.body)
                            {
                                // bodyStmt might be a GeneratedStmt or a List<Object> containing statements
                                if (bodyStmt is GeneratedStmt generatedStmt)
                                {
                                    var convertedStmt = ConvertStatement(generatedStmt, true, insideFunction); // insideLoop = true
                                    if (convertedStmt != null)
                                        bodyStmts.Add(convertedStmt);
                                }
                                else if (bodyStmt is System.Collections.IEnumerable enumerable && !(bodyStmt is string))
                                {
                                    // Handle nested list of statements
                                    foreach (var nestedStmt in enumerable)
                                    {
                                        if (nestedStmt is GeneratedStmt nestedGeneratedStmt)
                                        {
                                            var convertedStmt = ConvertStatement(nestedGeneratedStmt, true, insideFunction);
                                            if (convertedStmt != null)
                                                bodyStmts.Add(convertedStmt);
                                        }
                                        else
                                        {
                                            Console.WriteLine($"[DEBUG] Unexpected nested statement type: {nestedStmt?.GetType()}");
                                        }
                                    }
                                }
                                else
                                {
                                    // Handle other types if needed
                                    Console.WriteLine($"[DEBUG] Unexpected body statement type: {bodyStmt?.GetType()}");
                                }
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

                        // Extract target variable name from GeneratedExpr
                        string targetVar = "i"; // default
                        if (asyncForData.target is GeneratedExpr targetExpr && targetExpr.ExpressionType == "Name")
                        {
                            if (targetExpr.Value is object targetValue)
                            {
                                var valueType = targetValue.GetType();
                                var idProperty = valueType.GetProperty("id");
                                var valueProperty = valueType.GetProperty("value");

                                if (idProperty != null)
                                {
                                    targetVar = idProperty.GetValue(targetValue)?.ToString() ?? "i";
                                }
                                else if (valueProperty != null)
                                {
                                    targetVar = valueProperty.GetValue(targetValue)?.ToString() ?? "i";
                                }
                            }
                        }

                        // Convert iterable expression
                        Expression iterableExpr = ConvertAnyExpression(asyncForData.iter);

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
                    // Use GeneratedStmt specific properties instead of Value
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] try case: stmt.TryBody = {stmt.TryBody}, stmt.ExceptClauses = {stmt.ExceptClauses}");
#endif

                    // Convert try body statements
                    var tryBodyStatements = new List<Statement>();
                    if (stmt.TryBody != null)
                    {
                        foreach (var bodyStmt in stmt.TryBody)
                        {
                            var convertedStmt = ConvertStatement(bodyStmt as GeneratedStmt, insideLoop, insideFunction);
                            if (convertedStmt != null)
                                tryBodyStatements.Add(convertedStmt);
                        }
                    }

                    // Convert except blocks
                    var exceptHandlersList = new List<ExceptHandler>();
                    if (stmt.ExceptClauses != null)
                    {
                        foreach (var exceptBlock in stmt.ExceptClauses)
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
                                exceptHandlersList.Add(new ExceptHandler(exceptionTypeExpr, variableName, exceptBodyStmts));
                            }
                        }

                        // Convert finally block if present
                        var elseStatements = new List<Statement>();
                        var finallyStatements = new List<Statement>();
                        if (stmt.FinallyBody != null)
                        {
                            foreach (var finallyStmt in stmt.FinallyBody)
                            {
                                var convertedStmt = ConvertStatement(finallyStmt as GeneratedStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    finallyStatements.Add(convertedStmt);
                            }
                        }

                        return new TryStatement(tryBodyStatements, exceptHandlersList, elseStatements, finallyStatements);

                case "try_star":
                    // Try statement with except* handlers (PEP 654: Exception Groups)
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

                        // Convert except* handlers
                        var exceptHandlers = new List<ExceptHandler>();
                        if (tryData.handlers != null)
                        {
                            foreach (var handlerData in tryData.handlers)
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
                        if (tryData.orelse != null)
                        {
                            foreach (var elseStmt in tryData.orelse)
                            {
                                var convertedStmt = ConvertStatement(elseStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    elseStmts.Add(convertedStmt);
                            }
                        }

                        var finallyStmts = new List<Statement>();
                        if (tryData.finalbody != null)
                        {
                            foreach (var finallyStmt in tryData.finalbody)
                            {
                                var convertedStmt = ConvertStatement(finallyStmt, insideLoop, insideFunction);
                                if (convertedStmt != null)
                                    finallyStmts.Add(convertedStmt);
                            }
                        }

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
                            // Extract parameters from Arguments field
                            var parameters = new List<string>();
                            var defaultValues = new Dictionary<string, object>(); // Store defaults by parameter name
                            if (funcData.Arguments != null)
                            {
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

                            // Convert function body with insideFunction=true
                            var bodyStmts = new List<Statement>();
                            if (funcData.Body is List<object> bodyList)
                            {
                                foreach (var bodyItem in bodyList)
                                {
                                    if (bodyItem is GeneratedStmt bodyStmt)
                                    {
                                        var convertedStmt = ConvertStatement(bodyStmt, insideLoop, true); // insideFunction=true
                                        if (convertedStmt != null)
                                        {
                                            bodyStmts.Add(convertedStmt);
                                        }
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
                            if (stmt.Decorators != null && stmt.Decorators.Count > 0)
                            {
#if DEBUG_LOG
                                Console.WriteLine($"[DEBUG] Function '{name}' has {stmt.Decorators.Count} decorators");
#endif
                                // Convert decorators to expressions
                                foreach (var decorator in stmt.Decorators)
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

                            // Apply defaults to parameters (CPython style: defaults apply to last N parameters)
                            if (defaultValues.Count > 0)
                            {
                                Console.WriteLine($"[DEBUG] Applying {defaultValues.Count} defaults to {parameters.Count} parameters");
                                for (int i = 0; i < defaultValues.Count; i++)
                                {
                                    int paramIndex = parameters.Count - defaultValues.Count + i;
                                    if (paramIndex >= 0 && paramIndex < parameters.Count)
                                    {
                                        var defaultValueObj = defaultValues[i.ToString()];
                                        // Convert default value to string representation
                                        string defaultStr = ConvertDefaultToString(defaultValueObj);
                                        string originalParam = parameters[paramIndex];
                                        string paramWithDefault = $"{originalParam}={defaultStr}";
                                        parameters[paramIndex] = paramWithDefault;
                                        Console.WriteLine($"[DEBUG] Updated parameter: {originalParam} -> {paramWithDefault}");
                                    }
                                }
                            }

                            // Create function with decorators
                            Console.WriteLine($"[DEBUG] Creating FunctionDefStatement with {parameters.Count} parameters");
                            var functionDef = new FunctionDefStatement(name, parameters, bodyStmts, null, decoratorExpressions);
                            return functionDef;
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

                case "class":
                    // Class definition from parser (class name: body)
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] ConvertStatement: Processing class statement");
                    Console.WriteLine($"[DEBUG] stmt.Value: {stmt.Value}");
                    Console.WriteLine($"[DEBUG] stmt.Value type: {stmt.Value?.GetType()?.Name}");
                    Console.WriteLine($"[DEBUG] stmt properties:");
                    foreach (var prop in stmt.GetType().GetProperties())
                    {
                        try
                        {
                            var value = prop.GetValue(stmt);
                            Console.WriteLine($"[DEBUG]   {prop.Name}: {value} (type: {value?.GetType()?.Name})");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[DEBUG]   {prop.Name}: Error - {ex.Message}");
                        }
                    }
#endif
                    // Get class name directly from stmt
                    var className = stmt.ClassName ?? "";

                    // Get base classes directly from stmt
                    var baseClassExprs = new List<Expression>();
                    if (stmt.BaseClasses != null)
                    {
                        foreach (var baseClass in stmt.BaseClasses)
                        {
                            if (baseClass is string baseName)
                            {
                                baseClassExprs.Add(new NameExpression(baseName));
                            }
                            else
                            {
                                try
                                {
                                    var expr = ConvertAnyExpression(baseClass);
                                    if (expr != null)
                                        baseClassExprs.Add(expr);
                                }
                                catch (Exception ex)
                                {
#if DEBUG_LOG
                                    Console.WriteLine($"[DEBUG] Class base class conversion error: {ex.Message}");
#endif
                                }
                            }
                        }
                    }

                    // Get body statements directly from stmt
                    var classBodyStmts = new List<Statement>();
                    if (stmt.Body != null)
                    {
                        foreach (var bodyStmt in stmt.Body)
                        {
                            try
                            {
                                if (bodyStmt is GeneratedStmt generatedStmt)
                                {
                                    var convertedStmt = ConvertStatement(generatedStmt, insideLoop, insideFunction);
                                    if (convertedStmt != null)
                                        classBodyStmts.Add(convertedStmt);
                                }
                                else if (bodyStmt is IEnumerable<object> innerList && !(bodyStmt is string))
                                {
                                    // The body statements are wrapped in a List, unwrap them
                                    foreach (var innerStmt in innerList)
                                    {
                                        if (innerStmt is GeneratedStmt innerGeneratedStmt)
                                        {
                                            var convertedStmt = ConvertStatement(innerGeneratedStmt, insideLoop, insideFunction);
                                            if (convertedStmt != null)
                                                classBodyStmts.Add(convertedStmt);
                                        }
                                    }
                                }
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

                case "class_def":
                    // Class definition (class name: body)
                    if (stmt.Value != null)
                    {
                        var classData = stmt.Value as dynamic;
                        Console.WriteLine($"[DEBUG] Class definition data: {classData}");
                        Console.WriteLine($"[DEBUG] Class data type: {classData?.GetType()?.Name}");

                        // Try to get properties through reflection
                        var type = classData?.GetType();
                        if (type != null)
                        {
                            foreach (var prop in type.GetProperties())
                            {
                                try
                                {
                                    var value = prop.GetValue(classData);
                                    Console.WriteLine($"[DEBUG] Class property {prop.Name}: {value} (type: {value?.GetType()?.Name})");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[DEBUG] Class property {prop.Name}: Error - {ex.Message}");
                                }
                            }
                        }

                        // For now, return a placeholder
                        return new ExpressionStatement(new ConstantExpression(PyNone.Instance));
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
                        foreach (var moduleItem in (List<object>)stmt.ImportModules)
                        {
                            Console.WriteLine($"  Module: {moduleItem}");
                        }
                    }
#endif
                    if (stmt.ImportModules != null && stmt.ImportModules is List<object> modules && modules.Count > 0)
                    {
                        var names = new List<string>();
                        foreach (var moduleObj in modules)
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

                case "from_import":
                    // From import statement (from module import name) - CPython 3.12 compatible
                    Console.WriteLine($"[DEBUG] Processing from_import: module={stmt.FromModule}, level={stmt.ImportLevel}, names={stmt.ImportNames?.Count ?? 0}");

                    var module = stmt.FromModule;
                    var level = stmt.ImportLevel;
                    var importAliases = new List<ImportAlias>();

                    if (stmt.ImportNames != null)
                    {
                        foreach (var nameItem in stmt.ImportNames)
                        {
                            if (nameItem is Dictionary<string, object> nameDict)
                            {
                                var name = nameDict.ContainsKey("Name") ? nameDict["Name"]?.ToString() : null;
                                var asName = nameDict.ContainsKey("AsName") ? nameDict["AsName"]?.ToString() : null;

                                if (!string.IsNullOrEmpty(name))
                                {
                                    importAliases.Add(new ImportAlias(name, asName));
                                    Console.WriteLine($"[DEBUG] Added import alias: {name} as {asName ?? name}");
                                }
                            }
                            else if (nameItem is string simpleName)
                            {
                                importAliases.Add(new ImportAlias(simpleName));
                                Console.WriteLine($"[DEBUG] Added simple import: {simpleName}");
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

                case "type_alias":
                    // Type alias statement (PEP 695: type Point = tuple[float, float])
                    if (stmt.Value != null)
                    {
                        var aliasData = stmt.Value as dynamic;
                        var name = aliasData?.Name;
                        var value = aliasData?.Value;
                        var typeParams = aliasData?.TypeParams;

#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] ConvertStatement TypeAlias: Name='{name}', Value='{value}', TypeParams='{typeParams}'");
#endif

                        if (name != null && value != null)
                        {
                            // Convert type parameters if any
                            List<string> paramList = new List<string>();
                            if (typeParams != null)
                            {
                                // Handle type parameters conversion
                                if (typeParams is IEnumerable<dynamic> paramEnumerable)
                                {
                                    foreach (var param in paramEnumerable)
                                    {
                                        if (param?.ToString() != null)
                                        {
                                            paramList.Add(param.ToString());
                                        }
                                    }
                                }
                            }

                            // Convert value expression
                            var valueExpression = ConvertExpression(value);
                            if (valueExpression != null)
                            {
                                return new TypeAliasStatement(name.ToString(), valueExpression, paramList.Any() ? paramList : null);
                            }
                        }
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
            Console.WriteLine($"[DEBUG] ConvertAnyExpression: Input object type: {expr?.GetType()?.Name}, Value: {expr}");

            if (expr == null)
            {
                throw new ArgumentNullException(nameof(expr), "Expression cannot be null");
            }

            // Primary path: Handle GeneratedExpr objects (modern parser output)
            if (expr is GeneratedExpr genExpr)
            {
                Console.WriteLine($"[DEBUG] ConvertAnyExpression: Converting GeneratedExpr with type '{genExpr.ExpressionType}'");
                return ConvertGeneratedExpression(genExpr);
            }

            // Also check by type name in case dynamic binding interferes
            if (expr?.GetType()?.Name == "GeneratedExpr")
            {
                Console.WriteLine($"[DEBUG] ConvertAnyExpression: Found GeneratedExpr by type name");
                var exprType = expr.GetType().GetProperty("ExpressionType")?.GetValue(expr)?.ToString();
                Console.WriteLine($"[DEBUG] ConvertAnyExpression: ExpressionType = {exprType}");
                return ConvertGeneratedExpression(expr);
            }

            // TEMPORARY FIX: Handle legacy anonymous objects until parser consistency is achieved
            if (expr != null)
            {
                var type = expr.GetType();
                var typeProperty = type.GetProperty("type");
                var valueProperty = type.GetProperty("value");

                if (typeProperty != null && valueProperty != null)
                {
                    var typeValue = typeProperty.GetValue(expr)?.ToString();
                    var nameValue = valueProperty.GetValue(expr)?.ToString();

                    if (typeValue == "name" && !string.IsNullOrEmpty(nameValue))
                    {
                        Console.WriteLine($"[DEBUG] ConvertAnyExpression: Converting legacy anonymous name object: {nameValue}");
                        return new NameExpression(nameValue);
                    }
                }
            }

            // This should not happen - all parsers should return GeneratedExpr objects
            throw new InvalidOperationException($"ConvertAnyExpression received non-GeneratedExpr object: {expr?.GetType()?.Name}. Object: {expr}. All parsers should return GeneratedExpr objects. This indicates a parser inconsistency that needs to be fixed.");
        }

        /// <summary>
        /// Convert GeneratedExpr to SharpPy Expression
        /// </summary>
        private static Expression ConvertGeneratedExpression(GeneratedExpr genExpr)
        {
            return genExpr.ExpressionType switch
            {
                // Assignment expressions (walrus operator)
                "NamedExpr" => ConvertNamedExpressionFromGenerated(genExpr),

                // Boolean operations
                "BoolOp" => ConvertBoolOpFromGenerated(genExpr),
                "UnaryOp" => ConvertUnaryOpFromGenerated(genExpr),

                // F-strings
                "JoinedStr" => ConvertJoinedStrFromGenerated(genExpr),
                "FormattedValue" => ConvertFormattedValueFromGenerated(genExpr),

                // Basic expressions
                "Constant" => ConvertConstantFromGenerated(genExpr),
                "Name" => ConvertNameFromGenerated(genExpr),

                // Binary and comparison operations
                "BinOp" => ConvertBinOpFromGenerated(genExpr),
                "Compare" => ConvertCompareFromGenerated(genExpr),

                // Function calls and attribute access
                "Call" => ConvertCallFromGenerated(genExpr),
                "Attribute" => ConvertAttributeFromGenerated(genExpr),
                "Subscript" => ConvertSubscriptFromGenerated(genExpr),

                // Collections
                "List" => ConvertListFromGenerated(genExpr),
                "Tuple" => ConvertTupleFromGenerated(genExpr),
                "Dict" => ConvertDictFromGenerated(genExpr),
                "Set" => ConvertSetFromGenerated(genExpr),

                // Comprehensions
                "ListComp" => ConvertListCompFromGenerated(genExpr),
                "DictComp" => ConvertDictCompFromGenerated(genExpr),
                "SetComp" => ConvertSetCompFromGenerated(genExpr),
                "GeneratorExp" => ConvertGeneratorExpFromGenerated(genExpr),

                // Lambda expressions
                "Lambda" => ConvertLambdaFromGenerated(genExpr),

                // Async/await expressions
                "await" => ConvertAwaitFromGenerated(genExpr),

                // PEP 695 Type Parameters
                "type_var" => ConvertTypeVarFromGenerated(genExpr),
                "type_var_tuple" => ConvertTypeVarTupleFromGenerated(genExpr),
                "param_spec" => ConvertParamSpecFromGenerated(genExpr),

                "Expression" => ConvertAnyExpression(genExpr.Value), // Fallback for wrapped expressions
                _ => throw new NotSupportedException($"Unsupported GeneratedExpr type: {genExpr.ExpressionType}")
            };
        }

        /// <summary>
        /// Convert NamedExpr (walrus operator) from GeneratedExpr to SharpPy NamedExpression
        /// </summary>
        private static Expression ConvertNamedExpressionFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("NamedExpr value is null");
            }

            // Extract target and value from the anonymous object created by the parser
            var valueType = valueObj.GetType();
            var targetProperty = valueType.GetProperty("target");
            var valueProperty = valueType.GetProperty("value");

            if (targetProperty == null || valueProperty == null)
            {
                throw new InvalidOperationException("NamedExpr value object missing target or value property");
            }

            var targetName = targetProperty.GetValue(valueObj)?.ToString();
            var value = valueProperty.GetValue(valueObj);

            if (string.IsNullOrEmpty(targetName))
            {
                throw new InvalidOperationException("NamedExpr target name is null or empty");
            }

            // Convert the value expression
            var valueExpr = ConvertAnyExpression(value);

            // Create target expression (simple name)
            var targetExpr = new NameExpression(targetName);

            return new NamedExpression(targetExpr, valueExpr);
        }

        /// <summary>
        /// Convert BoolOp (and/or expressions) from GeneratedExpr to SharpPy BoolOpExpression
        /// </summary>
        private static Expression ConvertBoolOpFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("BoolOp value is null");
            }

            // Extract op and values from the anonymous object created by the parser
            var valueType = valueObj.GetType();
            var opProperty = valueType.GetProperty("op");
            var valuesProperty = valueType.GetProperty("values");

            if (opProperty == null || valuesProperty == null)
            {
                throw new InvalidOperationException("BoolOp value object missing op or values property");
            }

            var opValue = opProperty.GetValue(valueObj)?.ToString();
            var values = valuesProperty.GetValue(valueObj);

            if (opValue == null || values == null)
            {
                throw new InvalidOperationException("BoolOp op or values is null");
            }

            // Convert values list to expressions
            var expressions = new List<Expression>();
            if (values is System.Collections.IEnumerable valuesEnum)
            {
                foreach (var value in valuesEnum)
                {
                    expressions.Add(ConvertAnyExpression(value));
                }
            }
            else
            {
                throw new InvalidOperationException("BoolOp values is not enumerable");
            }

            // Create BoolOpExpression based on operator
            var boolOpString = opValue switch
            {
                "And" => "and",
                "Or" => "or",
                _ => throw new NotSupportedException($"Unsupported BoolOp: {opValue}")
            };

            return new BoolOpExpression(boolOpString, expressions);
        }

        /// <summary>
        /// Convert UnaryOp (not expressions) from GeneratedExpr to SharpPy UnaryOpExpression
        /// </summary>
        private static Expression ConvertUnaryOpFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("UnaryOp value is null");
            }

            // Extract op and operand from the anonymous object created by the parser
            var valueType = valueObj.GetType();
            var opProperty = valueType.GetProperty("op");
            var operandProperty = valueType.GetProperty("operand");

            if (opProperty == null || operandProperty == null)
            {
                throw new InvalidOperationException("UnaryOp value object missing op or operand property");
            }

            var opValue = opProperty.GetValue(valueObj)?.ToString();
            var operand = operandProperty.GetValue(valueObj);

            if (opValue == null || operand == null)
            {
                throw new InvalidOperationException("UnaryOp op or operand is null");
            }

            // Convert operand to expression
            var operandExpr = ConvertAnyExpression(operand);

            // Create UnaryOpExpression based on operator
            var unaryOpString = opValue switch
            {
                "Not" => "not",
                "UAdd" => "+",
                "USub" => "-",
                "Invert" => "~",
                _ => throw new NotSupportedException($"Unsupported UnaryOp: {opValue}")
            };

            return new UnaryOpExpression(unaryOpString, operandExpr);
        }

        /// <summary>
        /// Convert JoinedStr (f-string) from GeneratedExpr to SharpPy JoinedStrExpression
        /// </summary>
        private static Expression ConvertJoinedStrFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("JoinedStr value is null");
            }

            // Extract values from the anonymous object created by the parser
            var valueType = valueObj.GetType();
            var valuesProperty = valueType.GetProperty("values");

            if (valuesProperty == null)
            {
                throw new InvalidOperationException("JoinedStr value object missing values property");
            }

            var values = valuesProperty.GetValue(valueObj);

            if (values == null)
            {
                throw new InvalidOperationException("JoinedStr values is null");
            }

            // Convert values list to expressions
            var expressions = new List<Expression>();
            if (values is System.Collections.IEnumerable valuesEnum)
            {
                foreach (var value in valuesEnum)
                {
                    expressions.Add(ConvertAnyExpression(value));
                }
            }
            else
            {
                throw new InvalidOperationException("JoinedStr values is not enumerable");
            }

            return new JoinedStrExpression(expressions);
        }

        /// <summary>
        /// Convert FormattedValue from GeneratedExpr to SharpPy FormattedValueExpression
        /// </summary>
        private static Expression ConvertFormattedValueFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("FormattedValue value is null");
            }

            // Extract value, conversion, and format_spec from the anonymous object
            var valueType = valueObj.GetType();
            var valueProperty = valueType.GetProperty("value");
            var conversionProperty = valueType.GetProperty("conversion");
            var formatSpecProperty = valueType.GetProperty("format_spec");

            if (valueProperty == null)
            {
                throw new InvalidOperationException("FormattedValue value object missing value property");
            }

            var value = valueProperty.GetValue(valueObj);
            var conversion = conversionProperty?.GetValue(valueObj) ?? -1;
            var formatSpec = formatSpecProperty?.GetValue(valueObj);

            if (value == null)
            {
                throw new InvalidOperationException("FormattedValue value is null");
            }

            // Convert the expression
            var valueExpr = ConvertAnyExpression(value);

            // Convert format spec if present
            Expression? formatSpecExpr = null;
            if (formatSpec != null)
            {
                formatSpecExpr = ConvertAnyExpression(formatSpec);
            }

            return new FormattedValueExpression(valueExpr, (int)conversion, formatSpecExpr);
        }

        /// <summary>
        /// Convert Constant from GeneratedExpr to SharpPy ConstantExpression
        /// </summary>
        private static Expression ConvertConstantFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("Constant value is null");
            }

            // Extract value and kind from the anonymous object
            var valueType = valueObj.GetType();
            var valueProperty = valueType.GetProperty("value");
            var kindProperty = valueType.GetProperty("kind");

            if (valueProperty == null)
            {
                throw new InvalidOperationException("Constant value object missing value property");
            }

            var value = valueProperty.GetValue(valueObj);
            var kind = kindProperty?.GetValue(valueObj)?.ToString();

            if (value == null)
            {
                throw new InvalidOperationException("Constant value is null");
            }

            // Convert based on kind with robust parsing
            return kind switch
            {
                "string" => new ConstantExpression(new PyString(value.ToString() ?? "")),
                "int" => new ConstantExpression(new PyInt(ParseInt(value))),
                "float" => new ConstantExpression(new PyFloat(ParseFloat(value))),
                "number" => new ConstantExpression(new PyInt(ParseInt(value))),
                _ => new ConstantExpression(new PyString(value.ToString() ?? ""))
            };
        }

        /// <summary>
        /// Safely parse integer values from various formats
        /// </summary>
        private static int ParseInt(object value)
        {
            if (value is int intVal)
                return intVal;
            if (value is long longVal)
                return (int)longVal;
            if (value is float floatVal)
                return (int)floatVal;
            if (value is double doubleVal)
                return (int)doubleVal;

            // Handle string conversion
            var strVal = value.ToString() ?? "";

            // Try parsing as integer first
            if (int.TryParse(strVal, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var result))
                return result;

            // If that fails, try parsing as double and convert to int
            if (double.TryParse(strVal, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var doubleResult))
                return (int)doubleResult;

            throw new InvalidOperationException($"Cannot convert '{strVal}' to integer");
        }

        /// <summary>
        /// Safely parse float values from various formats
        /// </summary>
        private static double ParseFloat(object value)
        {
            if (value is double doubleVal)
                return doubleVal;
            if (value is float floatVal)
                return floatVal;
            if (value is int intVal)
                return intVal;
            if (value is long longVal)
                return longVal;

            // Handle string conversion with culture-invariant parsing
            var strVal = value.ToString() ?? "";

            if (double.TryParse(strVal, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var result))
                return result;

            throw new InvalidOperationException($"Cannot convert '{strVal}' to float");
        }

        /// <summary>
        /// Convert Name from GeneratedExpr to SharpPy NameExpression
        /// </summary>
        private static Expression ConvertNameFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("Name value is null");
            }

            // Try both "id" and "value" properties for backward compatibility
            var idProperty = valueObj.GetType().GetProperty("id");
            var valueProperty = valueObj.GetType().GetProperty("value");

            string? name = null;
            if (idProperty != null)
            {
                name = idProperty.GetValue(valueObj)?.ToString();
            }
            else if (valueProperty != null)
            {
                name = valueProperty.GetValue(valueObj)?.ToString();
            }
            else
            {
                throw new InvalidOperationException("Name value object missing both id and value properties");
            }
            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException("Name value is null or empty");
            }

            return new NameExpression(name);
        }

        /// <summary>
        /// Convert BinOp from GeneratedExpr to SharpPy BinaryOpExpression
        /// </summary>
        private static Expression ConvertBinOpFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("BinOp value is null");
            }

            var valueType = valueObj.GetType();
            var leftProperty = valueType.GetProperty("left");
            var opProperty = valueType.GetProperty("op");
            var rightProperty = valueType.GetProperty("right");

            if (leftProperty == null || opProperty == null || rightProperty == null)
            {
                throw new InvalidOperationException("BinOp value object missing required properties");
            }

            var left = leftProperty.GetValue(valueObj);
            var op = opProperty.GetValue(valueObj)?.ToString();
            var right = rightProperty.GetValue(valueObj);

            if (left == null || op == null || right == null)
            {
                throw new InvalidOperationException("BinOp properties cannot be null");
            }

            var leftExpr = ConvertAnyExpression(left);
            var rightExpr = ConvertAnyExpression(right);

            return new BinaryOpExpression(leftExpr, op, rightExpr);
        }

        /// <summary>
        /// Convert Compare from GeneratedExpr to SharpPy CompareExpression
        /// </summary>
        private static Expression ConvertCompareFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("Compare value is null");
            }

            var valueType = valueObj.GetType();
            var leftProperty = valueType.GetProperty("left");
            var opsProperty = valueType.GetProperty("ops");
            var comparatorsProperty = valueType.GetProperty("comparators");

            if (leftProperty == null || opsProperty == null || comparatorsProperty == null)
            {
                throw new InvalidOperationException("Compare value object missing required properties");
            }

            var left = leftProperty.GetValue(valueObj);
            var ops = opsProperty.GetValue(valueObj) as IEnumerable<object>;
            var comparators = comparatorsProperty.GetValue(valueObj) as IEnumerable<object>;

            if (left == null || ops == null || comparators == null)
            {
                throw new InvalidOperationException("Compare properties cannot be null");
            }

            var leftExpr = ConvertAnyExpression(left);
            var opList = ops.Select(op => op.ToString()).ToList();
            var comparatorList = comparators.Select(comp => ConvertAnyExpression(comp)).ToList();

            // Use ChainedCompareExpression for multiple comparisons, CompareExpression for single
            if (opList.Count == 1 && comparatorList.Count == 1)
            {
                return new CompareExpression(leftExpr, opList[0], comparatorList[0]);
            }
            else
            {
                return new ChainedCompareExpression(leftExpr, opList, comparatorList);
            }
        }

        /// <summary>
        /// Convert Call from GeneratedExpr to SharpPy CallExpression
        /// </summary>
        private static Expression ConvertCallFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("Call value is null");
            }

            var valueType = valueObj.GetType();
            var funcProperty = valueType.GetProperty("func");
            var argsProperty = valueType.GetProperty("args");

            if (funcProperty == null || argsProperty == null)
            {
                throw new InvalidOperationException("Call value object missing required properties");
            }

            var func = funcProperty.GetValue(valueObj);
            var args = argsProperty.GetValue(valueObj) as IEnumerable<object>;

            if (func == null)
            {
                throw new InvalidOperationException("Call function cannot be null");
            }

            var functionExpr = ConvertAnyExpression(func);
            var argList = new List<Expression>();

            if (args != null)
            {
                foreach (var arg in args)
                {
                    var argExpr = ConvertAnyExpression(arg);
                    argList.Add(argExpr);
                }
            }

            return new CallExpression(functionExpr, argList);
        }

        /// <summary>
        /// Convert Attribute from GeneratedExpr to SharpPy AttributeExpression
        /// </summary>
        private static Expression ConvertAttributeFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("Attribute value is null");
            }

            var valueType = valueObj.GetType();
            var valueProperty = valueType.GetProperty("value");
            var attrProperty = valueType.GetProperty("attr");

            if (valueProperty == null || attrProperty == null)
            {
                throw new InvalidOperationException("Attribute value object missing required properties");
            }

            var value = valueProperty.GetValue(valueObj);
            var attr = attrProperty.GetValue(valueObj)?.ToString();

            if (value == null || string.IsNullOrEmpty(attr))
            {
                throw new InvalidOperationException("Attribute properties cannot be null");
            }

            var valueExpr = ConvertAnyExpression(value);
            return new AttributeExpression(valueExpr, attr);
        }

        /// <summary>
        /// Convert Subscript from GeneratedExpr to SharpPy SubscriptExpression
        /// </summary>
        private static Expression ConvertSubscriptFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("Subscript value is null");
            }

            var valueType = valueObj.GetType();
            var valueProperty = valueType.GetProperty("value");
            var sliceProperty = valueType.GetProperty("slice");

            if (valueProperty == null || sliceProperty == null)
            {
                throw new InvalidOperationException("Subscript value object missing required properties");
            }

            var value = valueProperty.GetValue(valueObj);
            var slice = sliceProperty.GetValue(valueObj);

            if (value == null || slice == null)
            {
                throw new InvalidOperationException("Subscript properties cannot be null");
            }

            var valueExpr = ConvertAnyExpression(value);
            var sliceExpr = ConvertAnyExpression(slice);

            return new SubscriptExpression(valueExpr, sliceExpr);
        }

        /// <summary>
        /// Convert List from GeneratedExpr to SharpPy ListExpression
        /// </summary>
        private static Expression ConvertListFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("List value is null");
            }

            var valueType = valueObj.GetType();
            var elementsProperty = valueType.GetProperty("elements");

            if (elementsProperty == null)
            {
                throw new InvalidOperationException("List value object missing elements property");
            }

            var elements = elementsProperty.GetValue(valueObj) as IEnumerable<object>;
            var convertedElements = new List<Expression>();

            if (elements != null)
            {
                foreach (var element in elements)
                {
                    convertedElements.Add(ConvertAnyExpression(element));
                }
            }

            return new ListExpression(convertedElements);
        }

        /// <summary>
        /// Convert Tuple from GeneratedExpr to SharpPy TupleExpression
        /// </summary>
        private static Expression ConvertTupleFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("Tuple value is null");
            }

            var valueType = valueObj.GetType();
            var elementsProperty = valueType.GetProperty("elements");

            if (elementsProperty == null)
            {
                throw new InvalidOperationException("Tuple value object missing elements property");
            }

            var elements = elementsProperty.GetValue(valueObj) as IEnumerable<object>;
            var convertedElements = new List<Expression>();

            if (elements != null)
            {
                foreach (var element in elements)
                {
                    convertedElements.Add(ConvertAnyExpression(element));
                }
            }

            return new TupleExpression(convertedElements);
        }

        /// <summary>
        /// Convert Dict from GeneratedExpr to SharpPy DictExpression
        /// </summary>
        private static Expression ConvertDictFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("Dict value is null");
            }

            var valueType = valueObj.GetType();
            var pairsProperty = valueType.GetProperty("pairs");

            if (pairsProperty == null)
            {
                throw new InvalidOperationException("Dict value object missing pairs property");
            }

            var pairs = pairsProperty.GetValue(valueObj) as IEnumerable<object>;
            var items = new List<(Expression Key, Expression Value)>();

            if (pairs != null)
            {
                foreach (var pair in pairs)
                {
                    var pairType = pair.GetType();
                    var keyProperty = pairType.GetProperty("key");
                    var valueProperty = pairType.GetProperty("value");

                    if (keyProperty != null && valueProperty != null)
                    {
                        var key = keyProperty.GetValue(pair);
                        var value = valueProperty.GetValue(pair);

                        if (key != null && value != null)
                        {
                            var convertedKey = ConvertAnyExpression(key);
                            var convertedValue = ConvertAnyExpression(value);
                            items.Add((convertedKey, convertedValue));
                        }
                    }
                }
            }

            return new DictExpression(items);
        }

        /// <summary>
        /// Convert Set from GeneratedExpr to SharpPy SetExpression
        /// </summary>
        private static Expression ConvertSetFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value is not object valueObj)
            {
                throw new InvalidOperationException("Set value is null");
            }

            var valueType = valueObj.GetType();
            var elementsProperty = valueType.GetProperty("elements");

            if (elementsProperty == null)
            {
                throw new InvalidOperationException("Set value object missing elements property");
            }

            var elements = elementsProperty.GetValue(valueObj) as IEnumerable<object>;
            var convertedElements = new List<Expression>();

            if (elements != null)
            {
                foreach (var element in elements)
                {
                    convertedElements.Add(ConvertAnyExpression(element));
                }
            }

            return new SetExpression(convertedElements);
        }

        /// <summary>
        /// Convert ListComp from GeneratedExpr to SharpPy ListComprehension
        /// </summary>
        private static Expression ConvertListCompFromGenerated(GeneratedExpr genExpr)
        {
            // Delegate to existing method
            return ConvertListComprehension(genExpr.Value);
        }

        /// <summary>
        /// Convert DictComp from GeneratedExpr to SharpPy DictComprehension
        /// </summary>
        private static Expression ConvertDictCompFromGenerated(GeneratedExpr genExpr)
        {
            // Delegate to existing method
            return ConvertDictComprehension(genExpr.Value);
        }

        /// <summary>
        /// Convert SetComp from GeneratedExpr to SharpPy SetComprehension
        /// </summary>
        private static Expression ConvertSetCompFromGenerated(GeneratedExpr genExpr)
        {
            // Delegate to existing method
            return ConvertSetComprehension(genExpr.Value);
        }

        /// <summary>
        /// Convert GeneratorExp from GeneratedExpr to SharpPy GeneratorExpression
        /// </summary>
        private static Expression ConvertGeneratorExpFromGenerated(GeneratedExpr genExpr)
        {
            // Delegate to existing method
            return ConvertGeneratorExpression(genExpr.Value);
        }

        /// <summary>
        /// Convert Lambda from GeneratedExpr to SharpPy LambdaExpression
        /// </summary>
        private static Expression ConvertLambdaFromGenerated(GeneratedExpr genExpr)
        {
            // Delegate to existing method
            return ConvertLambdaExpression(genExpr.Value);
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

            // If this is a GeneratedExpr, delegate to ConvertAnyExpression
            if (expr is GeneratedExpr)
            {
                return ConvertAnyExpression(expr);
            }

            // Handle legacy dynamic objects with .type property
            string type;
            try
            {
                type = expr.type as string;
                if (string.IsNullOrEmpty(type))
                {
                    return new ConstantExpression(PyNone.Instance);
                }
            }
            catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                // Object doesn't have .type property, might be a GeneratedExpr that wasn't caught above
                return ConvertAnyExpression(expr);
            }

            return type switch
            {
                "name" => new NameExpression(expr.value as string ?? ""),
                "number" => new ConstantExpression(new PyInt((int)Convert.ToInt64(expr.value ?? 0))),
                "string" => ConvertStringLiteral(expr.value as string ?? ""),
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
        /// Convert Await from GeneratedExpr to SharpPy AwaitExpression
        /// </summary>
        private static Expression ConvertAwaitFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value == null)
            {
                throw new InvalidOperationException("Await expression value is null");
            }

            var awaitedExpr = ConvertAnyExpression(genExpr.Value);
            return new AwaitExpression(awaitedExpr);
        }

        /// <summary>
        /// Convert TypeVar from GeneratedExpr to SharpPy TypeVarExpression
        /// </summary>
        private static Expression ConvertTypeVarFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value == null)
            {
                throw new InvalidOperationException("TypeVar expression value is null");
            }

            var typeVarData = genExpr.Value;
            string name = "";
            Expression? bound = null;

            // Extract name and bound from anonymous object
            if (typeVarData.GetType().GetProperty("name")?.GetValue(typeVarData) is string nameValue)
            {
                name = nameValue;
            }

            if (typeVarData.GetType().GetProperty("bound")?.GetValue(typeVarData) is object boundValue && boundValue != null)
            {
                bound = ConvertAnyExpression(boundValue);
            }

            return new TypeVarExpression(name, bound);
        }

        /// <summary>
        /// Convert TypeVarTuple from GeneratedExpr to SharpPy TypeVarTupleExpression
        /// </summary>
        private static Expression ConvertTypeVarTupleFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value == null)
            {
                throw new InvalidOperationException("TypeVarTuple expression value is null");
            }

            var typeVarTupleData = genExpr.Value;
            string name = "";

            // Extract name from anonymous object
            if (typeVarTupleData.GetType().GetProperty("name")?.GetValue(typeVarTupleData) is string nameValue)
            {
                name = nameValue;
            }

            return new TypeVarTupleExpression(name);
        }

        /// <summary>
        /// Convert ParamSpec from GeneratedExpr to SharpPy ParamSpecExpression
        /// </summary>
        private static Expression ConvertParamSpecFromGenerated(GeneratedExpr genExpr)
        {
            if (genExpr.Value == null)
            {
                throw new InvalidOperationException("ParamSpec expression value is null");
            }

            var paramSpecData = genExpr.Value;
            string name = "";

            // Extract name from anonymous object
            if (paramSpecData.GetType().GetProperty("name")?.GetValue(paramSpecData) is string nameValue)
            {
                name = nameValue;
            }

            return new ParamSpecExpression(name);
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

            // Handle GeneratedExpr
            if (defaultValue is GeneratedExpr genExpr)
            {
                Console.WriteLine($"[DEBUG] GeneratedExpr: Type='{genExpr.ExpressionType}', Value={genExpr.Value} (ValueType: {genExpr.Value?.GetType().Name})");

                // Handle nested GeneratedExpr (Expression wrapping Constant)
                if (genExpr.ExpressionType == "Expression" && genExpr.Value is GeneratedExpr nestedExpr)
                {
                    Console.WriteLine($"[DEBUG] Nested GeneratedExpr: Type='{nestedExpr.ExpressionType}', Value={nestedExpr.Value}");
                    return ConvertDefaultToString(nestedExpr); // Recursive call
                }

                if (genExpr.ExpressionType == "Constant" && genExpr.Value != null)
                {
                    var value = genExpr.Value;
                    Console.WriteLine($"[DEBUG] Constant value: {value} (Type: {value.GetType().Name})");

                    // Try Dictionary first
                    if (value is Dictionary<string, object> constDict && constDict.ContainsKey("value"))
                    {
                        var actualValue = constDict["value"];
                        Console.WriteLine($"[DEBUG] Dict value: {actualValue}");
                        return actualValue?.ToString() ?? "None";
                    }

                    // Handle anonymous object with 'value' property (common in generated code)
                    try
                    {
                        var valueProperty = value.GetType().GetProperty("value");
                        if (valueProperty != null)
                        {
                            var actualValue = valueProperty.GetValue(value);
                            Console.WriteLine($"[DEBUG] Anonymous object value: {actualValue} (Type: {actualValue?.GetType().Name})");

                            // Handle different types appropriately
                            if (actualValue is string str3)
                            {
                                Console.WriteLine($"[DEBUG] Returning string: \"{str3}\"");
                                return $"\"{str3}\"";
                            }
                            if (actualValue is int || actualValue is long || actualValue is double || actualValue is float)
                            {
                                Console.WriteLine($"[DEBUG] Returning number: {actualValue}");
                                return actualValue.ToString();
                            }
                            if (actualValue is bool boolean3)
                            {
                                Console.WriteLine($"[DEBUG] Returning boolean: {(boolean3 ? "True" : "False")}");
                                return boolean3 ? "True" : "False";
                            }

                            Console.WriteLine($"[DEBUG] Returning default: {actualValue}");
                            return actualValue?.ToString() ?? "None";
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DEBUG] Error accessing 'value' property: {ex.Message}");
                    }

                    // Handle direct constant values
                    if (value is int || value is long || value is double || value is float)
                        return value.ToString();
                    if (value is string str2) return $"\"{str2}\"";
                    if (value is bool boolean2) return boolean2 ? "True" : "False";

                    return value.ToString() ?? "None";
                }
                return genExpr.Value?.ToString() ?? "None";
            }

            // Handle direct values
            if (defaultValue is string str) return $"\"{str}\"";
            if (defaultValue is int || defaultValue is long || defaultValue is double || defaultValue is float)
                return defaultValue.ToString();
            if (defaultValue is bool boolean) return boolean ? "True" : "False";

            return defaultValue.ToString() ?? "None";
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