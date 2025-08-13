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
            List<ASTNode> elseBody = null;

            if (currentToken.Type == TokenType.ELSE)
            {
                Advance();
                Expect(TokenType.COLON);
                SkipNewlines();
                elseBody = ParseBlock();
            }

            return new IfNode(condition, thenBody, elseBody, line, column);
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

            Expect(TokenType.TRY);
            Expect(TokenType.COLON);
            SkipNewlines();

            var tryBody = ParseBlock();
            var exceptClauses = new List<(string, string, List<ASTNode>)>();
            List<ASTNode> elseBody = null;
            List<ASTNode> finallyBody = null;

            while (currentToken.Type == TokenType.EXCEPT)
            {
                Advance();
                string exceptionType = null;
                string variable = null;

                if (currentToken.Type == TokenType.IDENTIFIER)
                {
                    exceptionType = currentToken.Value;
                    Advance();
                    if (currentToken.Type == TokenType.AS)
                    {
                        Advance();
                        variable = currentToken.Value;
                        Expect(TokenType.IDENTIFIER);
                    }
                }

                Expect(TokenType.COLON);
                SkipNewlines();
                var exceptBody = ParseBlock();
                exceptClauses.Add((exceptionType, variable, exceptBody));
            }

            if (currentToken.Type == TokenType.ELSE)
            {
                Advance();
                Expect(TokenType.COLON);
                SkipNewlines();
                elseBody = ParseBlock();
            }

            if (currentToken.Type == TokenType.FINALLY)
            {
                Advance();
                Expect(TokenType.COLON);
                SkipNewlines();
                finallyBody = ParseBlock();
            }

            return new TryNode(tryBody, exceptClauses, elseBody, finallyBody, line, column);
        }

        private ASTNode ParseImport()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.IMPORT);
            string moduleName = currentToken.Value;
            Expect(TokenType.IDENTIFIER);

            string alias = null;
            if (currentToken.Type == TokenType.AS)
            {
                Advance();
                alias = currentToken.Value;
                Expect(TokenType.IDENTIFIER);
            }

            return new ImportNode(moduleName, alias, line, column);
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

        private ASTNode ParseExpressionStatement()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            // Parse the first expression
            var expr = ParseExpression();

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

        private ASTNode ParseExpression() => ParseConditionalExpression();

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

        private ASTNode ParsePrimary()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            switch (currentToken.Type)
            {
                case TokenType.NUMBER:
                    double value = double.Parse(currentToken.Value);
                    Advance();
                    return new NumberNode(value, line, column);

                case TokenType.STRING:
                    string strValue = currentToken.Value;
                    Advance();
                    return new StringNode(strValue, line, column);

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

        private ASTNode ParseList()
        {
            int line = currentToken.Line;
            int column = currentToken.Column;

            Expect(TokenType.LBRACKET);
            var elements = new List<ASTNode>();

            // Skip any newlines after opening bracket
            SkipNewlinesAndIndents();

            while (currentToken.Type != TokenType.RBRACKET)
            {
                elements.Add(ParseExpression());

                // Skip newlines and indents after each element
                SkipNewlinesAndIndents();

                if (currentToken.Type == TokenType.COMMA)
                {
                    Advance(); // Skip comma
                    SkipNewlinesAndIndents(); // Skip newlines and indents after comma

                    // Allow trailing comma
                    if (currentToken.Type == TokenType.RBRACKET)
                        break;
                }
                else if (currentToken.Type != TokenType.RBRACKET)
                {
                    throw new PythonException("SyntaxError", $"Expected ',' or ']' in list", currentToken.Line, currentToken.Column);
                }
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