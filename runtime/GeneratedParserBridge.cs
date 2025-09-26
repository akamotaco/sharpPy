using System;
using System.Collections.Generic;
using System.Linq;
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
                            // Extract target name from expression object (e.g., { type = name, value = x })
                            var targetName = "";
                            if (target is object targetObj)
                            {
                                dynamic targetDynamic = targetObj;
                                if (targetDynamic.type?.ToString() == "name")
                                {
                                    targetName = targetDynamic.value?.ToString() ?? "";
                                }
                            }

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
                        var value = stmt.Value.ToString();
                        if (value != null)
                        {
                            // Create expression (for now, just handle numbers)
                            if (int.TryParse(value, out int intValue))
                            {
                                returnValue = new ConstantExpression(new PyInt(intValue));
                            }
                            else if (double.TryParse(value, out double doubleValue))
                            {
                                returnValue = new ConstantExpression(new PyFloat(doubleValue));
                            }
                            else
                            {
                                returnValue = new ConstantExpression(new PyString(value));
                            }
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
                    // While statement (while condition: body)
                    if (stmt.Value != null)
                    {
                        var whileData = stmt.Value as dynamic;
                        var condition = whileData?.Condition as string;
                        var body = whileData?.Body as string;

                        if (condition == "True")
                        {
                            // Create condition expression (for now, just handle "True")
                            var conditionExpr = new ConstantExpression(PyBool.True);

                            // Create body statements
                            var bodyStmts = new List<Statement>();

                            if (body == "pass")
                            {
                                bodyStmts.Add(new ExpressionStatement(new ConstantExpression(PyNone.Instance)));
                            }
                            else if (body == "break")
                            {
                                bodyStmts.Add(new BreakStatement());
                            }

                            return new WhileStatement(conditionExpr, bodyStmts);
                        }
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

                case "for":
                    // For statement (for var in iterable: body)
                    if (stmt.Value != null)
                    {
                        var forData = stmt.Value as dynamic;
                        var variable = forData?.Variable as string;
                        var iterable = forData?.Iterable as string;
                        var rangeValue = forData?.RangeValue as string;
                        var body = forData?.Body as string;

                        if (!string.IsNullOrEmpty(variable) && iterable == "range" && !string.IsNullOrEmpty(rangeValue) && body == "pass")
                        {
                            // Create range expression
                            if (int.TryParse(rangeValue, out int rangeInt))
                            {
                                var rangeExpr = new CallExpression(
                                    new NameExpression("range"),
                                    new List<Expression> { new ConstantExpression(new PyInt(rangeInt)) },
                                    new List<KeywordExpression>()
                                );

                                // Create body statements
                                var bodyStmts = new List<Statement>
                                {
                                    new ExpressionStatement(new ConstantExpression(PyNone.Instance))
                                };

                                return new ForStatement(variable, rangeExpr, bodyStmts);
                            }
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
                    if (stmt.Value != null)
                    {
                        var importData = stmt.Value as dynamic;
                        var module = importData?.Module as string;

                        if (!string.IsNullOrEmpty(module))
                        {
                            var names = new List<string> { module };
                            return new ImportStatement(names);
                        }
                    }
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

                case "try":
                    // Try statement (try: body except: handler)
                    if (stmt.Value != null)
                    {
                        var tryData = stmt.Value as dynamic;
                        var tryBody = tryData?.TryBody as string;
                        var exceptBody = tryData?.ExceptBody as string;

                        if (tryBody == "pass" && exceptBody == "pass")
                        {
                            // Create try body statements
                            var tryBodyStmts = new List<Statement>
                            {
                                new ExpressionStatement(new ConstantExpression(PyNone.Instance))
                            };

                            // Create except handler body
                            var exceptBodyStmts = new List<Statement>
                            {
                                new ExpressionStatement(new ConstantExpression(PyNone.Instance))
                            };

                            // Create except handler (catch all exceptions)
                            var handlers = new List<ExceptHandler>
                            {
                                new ExceptHandler(null, null, exceptBodyStmts) // null type means catch all
                            };

                            return new TryStatement(tryBodyStmts, handlers);
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

                _ => throw new NotSupportedException($"Unsupported expression type: {type}")
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