// enhanced_parser_continuation.cs
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    // Parser continuation - parsing methods with enhanced line/column tracking
    public partial class Parser
    {
        private ASTNode ParseClassDef()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.CLASS);
            string name = currentToken.Value;
            Expect(TokenType.IDENTIFIER);

            string baseClass = null;
            if (currentToken.Type == TokenType.LPAREN)
            {
                Advance(); // Skip '('
                if (currentToken.Type == TokenType.IDENTIFIER)
                {
                    baseClass = currentToken.Value;
                    Expect(TokenType.IDENTIFIER);
                }
                Expect(TokenType.RPAREN);
            }

            Expect(TokenType.COLON);
            SkipNewlines();

            var body = ParseBlock();
            return new ClassDefNode(name, body, baseClass, line, column);
        }

        private ASTNode ParseIf()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.IF);
            var condition = ParseExpression();
            Expect(TokenType.COLON);
            SkipNewlines();
            var thenBody = ParseBlock();

            // Handle DEDENT before elif/else
            if (currentToken.Type == TokenType.DEDENT)
            {
                // Check if next token is elif/else
                if (position + 1 < tokenCount)
                {
                    var nextToken = tokens[position + 1];
                    if (nextToken.Type == TokenType.ELIF || nextToken.Type == TokenType.ELSE)
                    {
                        Advance(); // Consume DEDENT
                    }
                }
            }

            // Skip any newlines between blocks
            SkipNewlines();

            var elifClauses = new List<(ASTNode, List<ASTNode>)>();
            while (currentToken.Type == TokenType.ELIF)
            {
                Advance(); // Skip 'elif'
                var elifCondition = ParseExpression();
                Expect(TokenType.COLON);
                SkipNewlines();
                var elifBody = ParseBlock();
                
                // Handle DEDENT after elif block
                if (currentToken.Type == TokenType.DEDENT)
                {
                    if (position + 1 < tokenCount)
                    {
                        var nextToken = tokens[position + 1];
                        if (nextToken.Type == TokenType.ELIF || nextToken.Type == TokenType.ELSE)
                        {
                            Advance(); // Consume DEDENT
                        }
                    }
                }
                
                SkipNewlines();
                elifClauses.Add((elifCondition, elifBody));
            }

            List<ASTNode> elseBody = null;
            if (currentToken.Type == TokenType.ELSE)
            {
                Advance(); // Skip 'else'
                Expect(TokenType.COLON);
                SkipNewlines();
                elseBody = ParseBlock();
            }

            return new IfNode(condition, thenBody, elifClauses, elseBody, line, column);
        }

        private ASTNode ParseFor()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.FOR);

            // Parse target variables (can be multiple: for i, item in ...)
            var variables = new List<string>();
            variables.Add(currentToken.Value);
            Expect(TokenType.IDENTIFIER);

            while (currentToken.Type == TokenType.COMMA)
            {
                Advance(); // Skip comma
                variables.Add(currentToken.Value);
                Expect(TokenType.IDENTIFIER);
            }

            Expect(TokenType.IN);
            var iterable = ParseExpression();
            Expect(TokenType.COLON);
            SkipNewlines();

            var body = ParseBlock();

            // If multiple variables, create a ForNode that unpacks tuples
            if (variables.Count == 1)
            {
                return new ForNode(variables[0], iterable, body, line, column);
            }
            else
            {
                return new MultiForNode(variables, iterable, body, line, column);
            }
        }

        private ASTNode ParseWhile()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.WHILE);
            var condition = ParseExpression();
            Expect(TokenType.COLON);
            SkipNewlines();

            var body = ParseBlock();
            return new WhileNode(condition, body, line, column);
        }

        private ASTNode ParseTry()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            //Console.WriteLine($"[ParseTry] Starting at line {line}");
            
            Expect(TokenType.TRY);
            Expect(TokenType.COLON);
            SkipNewlines();

            //Console.WriteLine($"[ParseTry] Before parsing try body, current token: {currentToken.Type} '{currentToken.Value}'");
            var tryBody = ParseBlock();
            //Console.WriteLine($"[ParseTry] After parsing try body, current token: {currentToken.Type} '{currentToken.Value}'");

            var exceptClauses = new List<(string, string, List<ASTNode>)>();
            List<ASTNode> elseBody = null;
            List<ASTNode> finallyBody = null;

            // 중요: ParseBlock 후 토큰 상태 확인 및 정리
            //Console.WriteLine($"[ParseTry] Before cleanup, current token: {currentToken.Type} at line {currentToken.Line}");
            
            // DEDENT, NEWLINE 등을 건너뛰기
            while (currentToken.Type == TokenType.NEWLINE || 
                currentToken.Type == TokenType.DEDENT ||
                currentToken.Type == TokenType.INDENT)
            {
                //Console.WriteLine($"[ParseTry] Skipping {currentToken.Type}");
                Advance();
            }
            
            //Console.WriteLine($"[ParseTry] After cleanup, current token: {currentToken.Type} '{currentToken.Value}' at line {currentToken.Line}");

            // except 절 처리
            while (currentToken.Type == TokenType.EXCEPT)
            {
                //Console.WriteLine($"[ParseTry] Found EXCEPT at line {currentToken.Line}");
                Advance(); // Skip 'except'
                
                string exceptionType = null;
                string variable = null;

                if (currentToken.Type == TokenType.IDENTIFIER)
                {
                    exceptionType = currentToken.Value;
                    Advance();
                    
                    if (currentToken.Type == TokenType.AS)
                    {
                        Advance(); // Skip 'as'
                        variable = currentToken.Value;
                        Expect(TokenType.IDENTIFIER);
                    }
                }

                Expect(TokenType.COLON);
                SkipNewlines();
                
                //Console.WriteLine($"[ParseTry] Before parsing except body");
                var exceptBody = ParseBlock();
                //Console.WriteLine($"[ParseTry] After parsing except body");
                
                exceptClauses.Add((exceptionType, variable, exceptBody));

                // 다음 except/else/finally를 위한 정리
                while (currentToken.Type == TokenType.NEWLINE || 
                    currentToken.Type == TokenType.DEDENT ||
                    currentToken.Type == TokenType.INDENT)
                {
                    Advance();
                }
            }

            // else 절 처리
            if (currentToken.Type == TokenType.ELSE)
            {
                //Console.WriteLine($"[ParseTry] Found ELSE at line {currentToken.Line}");
                Advance(); // Skip 'else'
                Expect(TokenType.COLON);
                SkipNewlines();
                elseBody = ParseBlock();
                
                // 정리
                while (currentToken.Type == TokenType.NEWLINE || 
                    currentToken.Type == TokenType.DEDENT ||
                    currentToken.Type == TokenType.INDENT)
                {
                    Advance();
                }
            }

            // finally 절 처리
            if (currentToken.Type == TokenType.FINALLY)
            {
                //Console.WriteLine($"[ParseTry] Found FINALLY at line {currentToken.Line}");
                Advance(); // Skip 'finally'
                Expect(TokenType.COLON);
                SkipNewlines();
                finallyBody = ParseBlock();
            }

            //Console.WriteLine($"[ParseTry] Completed, returning TryNode");
            return new TryNode(tryBody, exceptClauses, elseBody, finallyBody, line, column);
        }

        // NEW METHOD - Parse WITH statement
        private ASTNode ParseWith()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.WITH);

            // Parse context expression
            var contextExpr = ParseExpression();

            // Check for 'as' clause
            string variable = null;
            if (currentToken.Type == TokenType.AS)
            {
                Advance(); // Skip 'as'
                variable = currentToken.Value;
                Expect(TokenType.IDENTIFIER);
            }

            Expect(TokenType.COLON);
            SkipNewlines();

            // Parse body
            var body = ParseBlock();

            return new WithNode(contextExpr, variable, body, line, column);
        }

        // NEW METHOD - Parse DEL statement
        private ASTNode ParseDel()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.DEL);

            string variableName = currentToken.Value;
            Expect(TokenType.IDENTIFIER);

            return new DelNode(variableName, line, column);
        }

        private ASTNode ParseImport()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            if (currentToken.Type == TokenType.FROM)
            {
                // from module import name [as alias], name2 [as alias2], ...
                Expect(TokenType.FROM);

                // Parse module name (could be package.module)
                string moduleName = currentToken.Value;
                Expect(TokenType.IDENTIFIER);

                // Handle package.module notation
                while (currentToken.Type == TokenType.DOT)
                {
                    Advance(); // Skip '.'
                    moduleName += "." + currentToken.Value;
                    Expect(TokenType.IDENTIFIER);
                }

                Expect(TokenType.IMPORT);

                if (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "*")
                {
                    Advance(); // Skip '*'
                               // 빈 리스트와 특별한 이름 "*"를 사용하여 구분
                    var allItems = new List<(string, string)> { ("*", null) };
                    return new FromImportNode(moduleName, allItems, line, column);
                }
                // Check for "import *"
                // if (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "*")
                // {
                //     Advance(); // Skip '*'
                //     return new FromImportNode(moduleName, null, true, line, column); // ImportAll = true
                // }

                // Parse import items
                var importItems = new List<(string, string)>();

                do
                {
                    string itemName = currentToken.Value;
                    Expect(TokenType.IDENTIFIER);

                    string alias = null;
                    if (currentToken.Type == TokenType.AS)
                    {
                        Advance(); // Skip 'as'
                        alias = currentToken.Value;
                        Expect(TokenType.IDENTIFIER);
                    }

                    importItems.Add((itemName, alias));

                    if (currentToken.Type == TokenType.COMMA)
                    {
                        Advance(); // Skip ','
                    }
                    else
                    {
                        break;
                    }
                } while (currentToken.Type == TokenType.IDENTIFIER);

                return new FromImportNode(moduleName, importItems, line, column);
            }
            else
            {
                // import module [as alias]
                Expect(TokenType.IMPORT);

                // Parse module name (could be package.module)
                string moduleName = currentToken.Value;
                Expect(TokenType.IDENTIFIER);

                // Handle package.module notation
                while (currentToken.Type == TokenType.DOT)
                {
                    Advance(); // Skip '.'
                    moduleName += "." + currentToken.Value;
                    Expect(TokenType.IDENTIFIER);
                }

                string alias = null;
                if (currentToken.Type == TokenType.AS)
                {
                    Advance(); // Skip 'as'
                    alias = currentToken.Value;
                    Expect(TokenType.IDENTIFIER);
                }

                return new ImportNode(moduleName, alias, line, column);
            }
        }

        private ASTNode ParseReturn()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.RETURN);
            ASTNode value = null;
            if (currentToken.Type != TokenType.NEWLINE && currentToken.Type != TokenType.EOF)
                value = ParseExpressionOrTuple();
            return new ReturnNode(value, line, column);
        }

        private ASTNode ParseBreak()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.BREAK);
            return new BreakNode(line, column);
        }

        private ASTNode ParseContinue()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.CONTINUE);
            return new ContinueNode(line, column);
        }

        private ASTNode ParseRaise()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.RAISE);
            var exception = ParseExpression();
            return new RaiseNode(exception, line, column);
        }

        // MODIFIED - Handle compound assignments
        private ASTNode ParseExpressionStatement()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            // Parse the first expression
            var expr = ParseExpression();
            
            // 디버그: 파싱된 expression 타입 확인
            //Console.WriteLine($"[ParseExpressionStatement] Parsed expression type: {expr.GetType().Name}");
            if (expr is AttributeNode attr)
            {
                //Console.WriteLine($"  - AttributeNode: {attr.Object}.{attr.Attribute}");
            }
            else if (expr is VariableNode var)
            {
                //Console.WriteLine($"  - VariableNode: {var.Name}");
            }
            
            //Console.WriteLine($"[ParseExpressionStatement] Next token: {currentToken.Type} '{currentToken.Value}'");

            // Check if we have a comma-separated list on the left side
            if (currentToken.Type == TokenType.COMMA)
            {
                // This is a tuple on the left side
                var elements = new List<ASTNode> { expr };
                while (currentToken.Type == TokenType.COMMA)
                {
                    Advance(); // Skip comma
                    if (currentToken.Type == TokenType.ASSIGN)
                        break; // Trailing comma before assignment
                    elements.Add(ParseExpression());
                }

                // Now check for assignment
                if (currentToken.Type == TokenType.ASSIGN)
                {
                    // Multiple assignment: a, b, c = (1, 2, 3)
                    var names = new List<string>();
                    foreach (var element in elements)
                    {
                        if (element is VariableNode varElement)
                            names.Add(varElement.Name);
                        else
                            throw new PythonException("SyntaxError", $"Invalid assignment target", currentToken.Line, currentToken.Column);
                    }
                    Advance(); // Skip '='
                    var value = ParseExpressionOrTuple();
                    return new MultipleAssignmentNode(names, value, line, column);
                }
                else
                {
                    // It's just a tuple expression, not an assignment
                    return new TupleNode(elements, line, column);
                }
            }

            if (currentToken.Type == TokenType.COMPOUND_ASSIGN)
            {
                string op = currentToken.Value;
                //Console.WriteLine($"[ParseExpressionStatement] Found compound assignment: {op}");
                
                Advance(); // Skip compound assignment operator
                var value = ParseExpressionOrTuple();
                
                // Handle different types of left-hand expressions
                if (expr is VariableNode varNode)
                {
                    //Console.WriteLine($"[ParseExpressionStatement] Creating CompoundAssignmentNode for variable: {varNode.Name}");
                    return new CompoundAssignmentNode(varNode.Name, op, value, line, column);
                }
                else if (expr is AttributeNode attrNode)
                {
                    //Console.WriteLine($"[ParseExpressionStatement] Creating AttributeCompoundAssignmentNode for: {attrNode.Attribute}");
                    return new AttributeCompoundAssignmentNode(attrNode.Object, attrNode.Attribute, op, value, line, column);
                }
                else if (expr is IndexNode indexNode)
                {
                    //Console.WriteLine($"[ParseExpressionStatement] Creating IndexCompoundAssignmentNode");
                    return new IndexCompoundAssignmentNode(indexNode.Object, indexNode.Index, op, value, line, column);
                }
                else
                {
                    //Console.WriteLine($"[ParseExpressionStatement] ERROR: Unsupported expression type for compound assignment: {expr.GetType().Name}");
                    throw new PythonException("SyntaxError", 
                        $"Invalid target for compound assignment (got {expr.GetType().Name})", 
                        currentToken.Line, currentToken.Column);
                }
            }

            // Check for compound assignment operators (NEW)
            if (currentToken.Type == TokenType.COMPOUND_ASSIGN)
            {
                if (expr is VariableNode varNode)
                {
                    string op = currentToken.Value;
                    Advance(); // Skip compound assignment operator
                    var value = ParseExpressionOrTuple();
                    return new CompoundAssignmentNode(varNode.Name, op, value, line, column);
                }
                else
                {
                    throw new PythonException("SyntaxError", $"Invalid target for compound assignment", currentToken.Line, currentToken.Column);
                }
            }

            // Check for regular assignment
            if (currentToken.Type == TokenType.ASSIGN)
            {
                if (expr is VariableNode varNode)
                {
                    Advance(); // Skip '='
                    var value = ParseExpressionOrTuple();
                    return new AssignmentNode(varNode.Name, value, line, column);
                }
                else if (expr is IndexNode indexNode)
                {
                    Advance(); // Skip '='
                    var value = ParseExpressionOrTuple();
                    return new IndexAssignmentNode(indexNode.Object, indexNode.Index, value, line, column);
                }
                else if (expr is AttributeNode attrNode)
                {
                    Advance(); // Skip '='
                    var value = ParseExpressionOrTuple();
                    return new AttributeAssignmentNode(attrNode.Object, attrNode.Attribute, value, line, column);
                }
                else
                {
                    throw new PythonException("SyntaxError", $"Invalid assignment target", currentToken.Line, currentToken.Column);
                }
            }

            return expr;
        }

        private ASTNode ParseExpressionOrTuple()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var expressions = new List<ASTNode>();
            expressions.Add(ParseExpression());

            while (currentToken.Type == TokenType.COMMA)
            {
                Advance(); // Skip comma
                // Check if we've reached the end of the statement
                if (IsEndOfStatement())
                    break;
                expressions.Add(ParseExpression());
            }

            if (expressions.Count == 1)
                return expressions[0];
            else
                return new TupleNode(expressions, line, column);
        }

        private bool IsEndOfStatement()
        {
            return currentToken.Type == TokenType.NEWLINE ||
                   currentToken.Type == TokenType.EOF ||
                   currentToken.Type == TokenType.RBRACE ||
                   currentToken.Type == TokenType.RBRACKET ||
                   currentToken.Type == TokenType.RPAREN ||
                   currentToken.Type == TokenType.COLON ||
                   currentToken.Type == TokenType.ELSE ||
                   currentToken.Type == TokenType.ELIF ||
                   currentToken.Type == TokenType.EXCEPT ||
                   currentToken.Type == TokenType.FINALLY;
        }

        internal ASTNode ParseExpression() => ParseConditionalExpression();

        private ASTNode ParseConditionalExpression()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseOrExpression();

            // Check for conditional expression: value_if_true if condition else value_if_false
            if (currentToken.Type == TokenType.IF)
            {
                Advance(); // Skip 'if'
                var condition = ParseOrExpression(); // Parse condition

                if (currentToken.Type != TokenType.ELSE)
                {
                    throw new PythonException("SyntaxError", $"Expected 'else' in conditional expression", currentToken.Line, currentToken.Column);
                }

                Advance(); // Skip 'else'
                var falseValue = ParseConditionalExpression(); // Right-associative

                return new ConditionalExpressionNode(node, condition, falseValue, line, column);
            }

            return node;
        }

        private ASTNode ParseOrExpression()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseAndExpression();

            while (currentToken.Type == TokenType.OR)
            {
                Advance();
                node = new BinaryOpNode(node, "or", ParseAndExpression(), line, column);
            }

            return node;
        }

        private ASTNode ParseAndExpression()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseNotExpression();

            while (currentToken.Type == TokenType.AND)
            {
                Advance();
                node = new BinaryOpNode(node, "and", ParseNotExpression(), line, column);
            }

            return node;
        }

        private ASTNode ParseNotExpression()
        {
            if (currentToken.Type == TokenType.NOT)
            {
                int line = currentToken.Line;
                int column = currentToken.Column;
                Advance();
                return new UnaryOpNode("not", ParseNotExpression(), line, column);
            }

            return ParseInExpression();
        }

        private ASTNode ParseInExpression()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseIsExpression();

            while (currentToken.Type == TokenType.IN)
            {
                Advance();
                node = new BinaryOpNode(node, "in", ParseIsExpression(), line, column);
            }

            return node;
        }

        private ASTNode ParseIsExpression()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseComparison();

            while (currentToken.Type == TokenType.IS)
            {
                Advance(); // Skip 'is'

                // Check for "is not"
                if (currentToken.Type == TokenType.NOT)
                {
                    Advance(); // Skip 'not'
                    node = new BinaryOpNode(node, "is not", ParseComparison(), line, column);
                }
                else
                {
                    node = new BinaryOpNode(node, "is", ParseComparison(), line, column);
                }
            }

            return node;
        }

        private ASTNode ParseComparison()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseArithmetic();

            while (currentToken.Type == TokenType.OPERATOR &&
                   new[] { "==", "!=", "<", ">", "<=", ">=" }.Contains(currentToken.Value))
            {
                string op = currentToken.Value;
                Advance();
                node = new BinaryOpNode(node, op, ParseArithmetic(), line, column);
            }

            return node;
        }

        private ASTNode ParseArithmetic()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseTerm();

            while (currentToken.Type == TokenType.OPERATOR &&
                   new[] { "+", "-" }.Contains(currentToken.Value))
            {
                string op = currentToken.Value;
                Advance();
                node = new BinaryOpNode(node, op, ParseTerm(), line, column);
            }

            return node;
        }

        private ASTNode ParseTerm()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParsePower();

            while (currentToken.Type == TokenType.OPERATOR &&
                   new[] { "*", "/", "%" }.Contains(currentToken.Value))
            {
                string op = currentToken.Value;
                Advance();
                node = new BinaryOpNode(node, op, ParsePower(), line, column);
            }

            return node;
        }

        private ASTNode ParsePower()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            var node = ParseFactor();

            if (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "**")
            {
                Advance();
                node = new BinaryOpNode(node, "**", ParsePower(), line, column); // Right associative
            }

            return node;
        }

        private ASTNode ParseFactor()
        {
            if (currentToken.Type == TokenType.OPERATOR && currentToken.Value == "-")
            {
                int line = currentToken.Line;
                int column = currentToken.Column;
                Advance();
                return new UnaryOpNode("-", ParseFactor(), line, column);
            }

            return ParsePostfix();
        }

        private ASTNode ParsePostfix()
        {
            var node = ParsePrimary();

            while (true)
            {
                int line = currentToken.Line;
                int column = currentToken.Column;

                if (currentToken.Type == TokenType.LPAREN)
                {
                    // Function call
                    Advance(); // Skip '('
                    SkipNewlinesAndIndents(); // Skip newlines after opening paren

                    var arguments = new List<ASTNode>();

                    while (currentToken.Type != TokenType.RPAREN)
                    {
                        arguments.Add(ParseExpression());
                        SkipNewlinesAndIndents(); // Skip newlines after argument

                        if (currentToken.Type == TokenType.COMMA)
                        {
                            Advance(); // Skip comma
                            SkipNewlinesAndIndents(); // Skip newlines after comma

                            // Allow trailing comma
                            if (currentToken.Type == TokenType.RPAREN)
                                break;
                        }
                        else if (currentToken.Type != TokenType.RPAREN)
                        {
                            throw new PythonException("SyntaxError", $"Expected ',' or ')' in function call", currentToken.Line, currentToken.Column);
                        }
                    }

                    Expect(TokenType.RPAREN);
                    node = new FunctionCallNode(node, arguments, line, column);
                }
                else if (currentToken.Type == TokenType.LBRACKET)
                {
                    // Index access or slice
                    Advance(); // Skip '['

                    // Check for slice notation
                    var indices = new List<ASTNode>();
                    var hasColon = false;

                    // Parse first part (could be start index or just index)
                    if (currentToken.Type != TokenType.COLON && currentToken.Type != TokenType.RBRACKET)
                        indices.Add(ParseExpression());
                    else
                        indices.Add(null); // Empty start

                    // Check for colon (slice notation)
                    if (currentToken.Type == TokenType.COLON)
                    {
                        hasColon = true;
                        Advance(); // Skip ':'

                        // Parse stop index
                        if (currentToken.Type != TokenType.COLON && currentToken.Type != TokenType.RBRACKET)
                            indices.Add(ParseExpression());
                        else
                            indices.Add(null); // Empty stop

                        // Check for second colon (step)
                        if (currentToken.Type == TokenType.COLON)
                        {
                            Advance(); // Skip second ':'
                            if (currentToken.Type != TokenType.RBRACKET)
                                indices.Add(ParseExpression());
                            else
                                indices.Add(null); // Empty step
                        }
                        else
                        {
                            indices.Add(null); // No step specified
                        }
                    }

                    Expect(TokenType.RBRACKET);

                    if (hasColon)
                        node = new SliceNode(node, indices[0], indices[1], indices.Count > 2 ? indices[2] : null, line, column);
                    else
                        node = new IndexNode(node, indices[0], line, column);
                }
                else if (currentToken.Type == TokenType.DOT)
                {
                    // Attribute access
                    Advance(); // Skip '.'
                    string attribute = currentToken.Value;
                    Expect(TokenType.IDENTIFIER);
                    node = new AttributeNode(node, attribute, line, column);
                }
                else
                {
                    break;
                }
            }

            return node;
        }

        // MODIFIED - Handle f-strings and list comprehensions
        private ASTNode ParsePrimary()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            switch (currentToken.Type)
            {
                case TokenType.NUMBER:
                    string numberStr = currentToken.Value;
                    object numberValue;

                    // Parse as int or float
                    if (numberStr.Contains('.'))
                    {
                        numberValue = double.Parse(numberStr);
                    }
                    else
                    {
                        // Try to parse as int first, fallback to double if too large
                        if (int.TryParse(numberStr, out int intValue))
                            numberValue = intValue;
                        else
                            numberValue = double.Parse(numberStr);
                    }

                    Advance();
                    return new NumberNode(numberValue, line, column);

                case TokenType.STRING:
                    string strValue = currentToken.Value;
                    Advance();
                    return new StringNode(strValue, line, column);

                case TokenType.FSTRING:  // Handle f-strings (NEW)
                    string fstrValue = currentToken.Value;
                    Advance();
                    return new FStringNode(fstrValue, line, column);

                case TokenType.BOOLEAN:
                    bool boolValue = currentToken.Value == "True";
                    Advance();
                    return new BooleanNode(boolValue, line, column);

                case TokenType.NONE:
                    Advance();
                    return new NoneNode(line, column);

                case TokenType.IDENTIFIER:
                    string name = currentToken.Value;
                    Advance();
                    return new VariableNode(name, line, column);

                case TokenType.LAMBDA:
                    return ParseLambda();

                case TokenType.LBRACKET:
                    return ParseList();

                case TokenType.LBRACE:
                    return ParseDict();

                case TokenType.LPAREN:
                    Advance();

                    // Skip newlines after opening paren
                    SkipNewlinesAndIndents();

                    // Check if this is an empty tuple
                    if (currentToken.Type == TokenType.RPAREN)
                    {
                        Advance();
                        return new TupleNode(new List<ASTNode>(), line, column); // Empty tuple
                    }

                    var firstExpr = ParseExpression();

                    // Skip newlines after first expression
                    SkipNewlinesAndIndents();

                    // Check for tuple (has comma) vs parenthesized expression
                    if (currentToken.Type == TokenType.COMMA)
                    {
                        var elements = new List<ASTNode> { firstExpr };
                        while (currentToken.Type == TokenType.COMMA)
                        {
                            Advance(); // Skip comma
                            SkipNewlinesAndIndents(); // Skip newlines after comma

                            if (currentToken.Type == TokenType.RPAREN)
                                break; // Trailing comma
                            elements.Add(ParseExpression());
                            SkipNewlinesAndIndents(); // Skip newlines after element
                        }
                        Expect(TokenType.RPAREN);
                        return new TupleNode(elements, line, column);
                    }
                    else
                    {
                        Expect(TokenType.RPAREN);
                        return firstExpr; // Just a parenthesized expression
                    }

                default:
                    throw new PythonException("SyntaxError", $"Unexpected token: {currentToken.Type}", currentToken.Line, currentToken.Column);
            }
        }

        private ASTNode ParseLambda()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.LAMBDA);

            var parameters = new List<Parameter>();
            bool hasSeenDefault = false;
            
            if (currentToken.Type != TokenType.COLON)
            {
                do
                {
                    if (currentToken.Type != TokenType.IDENTIFIER)
                    {
                        throw new PythonException("SyntaxError", 
                            $"Expected parameter name in lambda", 
                            currentToken.Line, currentToken.Column);
                    }

                    string paramName = currentToken.Value;
                    Advance();

                    ASTNode defaultValue = null;
                    if (currentToken.Type == TokenType.ASSIGN)
                    {
                        Advance(); // Skip '='
                        defaultValue = ParseOrExpression(); // 람다에서는 조건식 제외
                        hasSeenDefault = true;
                    }
                    else if (hasSeenDefault)
                    {
                        throw new PythonException("SyntaxError", 
                            "non-default argument follows default argument", 
                            currentToken.Line, currentToken.Column);
                    }

                    parameters.Add(new Parameter(paramName, null, defaultValue));

                    if (currentToken.Type == TokenType.COMMA)
                    {
                        Advance();
                        if (currentToken.Type == TokenType.COLON)
                        {
                            throw new PythonException("SyntaxError", 
                                $"Expected parameter after ',' in lambda", 
                                currentToken.Line, currentToken.Column);
                        }
                    }
                    else
                    {
                        break;
                    }
                } while (currentToken.Type != TokenType.COLON);
            }

            Expect(TokenType.COLON);

            var body = ParseExpression();

            return new LambdaNode(parameters, body, line, column);
        }

        // MODIFIED - Handle list comprehensions
        private ASTNode ParseList()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.LBRACKET);

            // Skip any newlines after opening bracket
            SkipNewlinesAndIndents();

            // Check for empty list
            if (currentToken.Type == TokenType.RBRACKET)
            {
                Advance();
                return new ListNode(new List<ASTNode>(), line, column);
            }

            // Parse first expression - but NOT as a full conditional expression for list comprehension
            var firstExpr = ParseOrExpression(); // Use ParseOrExpression instead of ParseExpression to avoid conditional

            // Skip newlines
            SkipNewlinesAndIndents();

            // Check if this is a list comprehension (NEW)
            if (currentToken.Type == TokenType.FOR)
            {
                // This is a list comprehension: [expr for var in iterable if condition]
                Advance(); // Skip 'for'

                string variable = currentToken.Value;
                Expect(TokenType.IDENTIFIER);

                Expect(TokenType.IN);

                var iterable = ParseOrExpression(); // Parse iterable without conditional

                // Check for optional 'if' clause
                ASTNode condition = null;
                if (currentToken.Type == TokenType.IF)
                {
                    Advance(); // Skip 'if'
                    condition = ParseOrExpression(); // Parse condition
                }

                Expect(TokenType.RBRACKET);

                return new ListComprehensionNode(firstExpr, variable, iterable, condition, line, column);
            }

            // Regular list literal
            var elements = new List<ASTNode> { firstExpr };

            while (currentToken.Type == TokenType.COMMA)
            {
                Advance(); // Skip comma
                SkipNewlinesAndIndents(); // Skip newlines and indents after comma

                // Allow trailing comma
                if (currentToken.Type == TokenType.RBRACKET)
                    break;

                elements.Add(ParseExpression());
                SkipNewlinesAndIndents();
            }

            Expect(TokenType.RBRACKET);
            return new ListNode(elements, line, column);
        }

        private ASTNode ParseDict()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.LBRACE);
            var pairs = new List<(ASTNode, ASTNode)>();

            // Skip any newlines after opening brace
            SkipNewlinesAndIndents();

            while (currentToken.Type != TokenType.RBRACE)
            {
                var key = ParseExpression();
                SkipNewlinesAndIndents(); // Skip newlines before colon
                Expect(TokenType.COLON);
                SkipNewlinesAndIndents(); // Skip newlines after colon
                var value = ParseExpression();
                pairs.Add((key, value));

                // Skip newlines after value
                SkipNewlinesAndIndents();

                if (currentToken.Type == TokenType.COMMA)
                {
                    Advance(); // Skip comma
                    SkipNewlinesAndIndents(); // Skip newlines after comma

                    // Allow trailing comma
                    if (currentToken.Type == TokenType.RBRACE)
                        break;
                }
                else if (currentToken.Type != TokenType.RBRACE)
                {
                    throw new PythonException("SyntaxError", $"Expected ',' or '}}' in dict", currentToken.Line, currentToken.Column);
                }
            }

            Expect(TokenType.RBRACE);
            return new DictNode(pairs, line, column);
        }
    }
}