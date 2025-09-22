using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SharpPy.PegParser;

namespace SharpPy
{
    /// <summary>
    /// PEG-based Python parser for SharpPy
    /// Compatible with CPython 3.12 grammar
    /// </summary>
    public class PyPegParser : PackratParser
    {
        private readonly List<PyToken> _tokens;
        private readonly string _filename;
        private readonly string _sourceCode;

        // Context tracking for async/await and comprehensions
        private bool _inAsyncFunction = false;
        private bool _inComprehension = false;
        private bool _inMatchPattern = false;

        // Cut tracking for PEG commit behavior
        private bool _cutCalled = false;

        public PyPegParser(List<PyToken> tokens, string filename = "<unknown>", string sourceCode = "")
            : base()
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _filename = filename;
            _sourceCode = sourceCode;
        }

        /// <summary>
        /// Parse the entire file
        /// </summary>
        public static List<Statement> ParseSource(string source, string filename = "<string>")
        {
            try
            {
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: ParseSource called for file: {filename}");
#endif
                var lexer = new PyLexer(source);
                var tokens = lexer.Tokenize();
                var parser = new PyPegParser(tokens, filename, source);
                return parser.ParseFile();
            }
            catch (Exception ex)
            {
                throw new PySyntaxErrorException($"Syntax error in {filename}: {ex.Message}");
            }
        }

        /// <summary>
        /// Main parsing entry point
        /// </summary>
        public List<Statement> ParseFile()
        {
            Reset();
            return ParseWithMemo("file", () =>
            {
                var statements = ParseStatements();

                // Skip trailing NEWLINE and other whitespace tokens before ENDMARKER
                while (_position < _tokens.Count &&
                       (_tokens[_position].Type == TokenType.NEWLINE ||
                        _tokens[_position].Type == TokenType.COMMENT ||
                        _tokens[_position].Type == TokenType.NL))
                {
                    _position++;
                }

                if (!MatchToken(TokenType.ENDMARKER))
                {
                    var currentToken = _position < _tokens.Count ? _tokens[_position] : null;
                    var tokenInfo = currentToken != null ? $"{currentToken.Type} '{currentToken.Lexeme}'" : "EOF";
                    throw new PySyntaxErrorException($"Expected end of file at position {_position}, found {tokenInfo}");
                }
                return statements ?? new List<Statement>();
            });
        }

        #region Token Access and Matching

        private PyToken CurrentToken
        {
            get
            {
                SkipCommentsAndNL();
                return _position < _tokens.Count ? _tokens[_position] : null;
            }
        }

        private bool IsAtEnd
        {
            get
            {
                SkipCommentsAndNL();
                return _position >= _tokens.Count;
            }
        }

        /// <summary>
        /// Skip COMMENT and NL tokens like the original parser
        /// </summary>
        private void SkipCommentsAndNL()
        {
            while (_position < _tokens.Count &&
                   (_tokens[_position].Type == TokenType.COMMENT ||
                    _tokens[_position].Type == TokenType.NL))
            {
                _position++;
            }
        }

        private bool MatchToken(TokenType type)
        {
            SkipCommentsAndNL();
            if (IsAtEnd || CurrentToken.Type != type) return false;
            _position++;
            return true;
        }

        private bool MatchKeyword(string keyword)
        {
            SkipCommentsAndNL();
            if (IsAtEnd || CurrentToken.Type != TokenType.NAME || CurrentToken.Lexeme != keyword) return false;
            _position++;
            return true;
        }

        private bool MatchLiteral(string literal)
        {
            SkipCommentsAndNL();
            if (IsAtEnd || CurrentToken.Lexeme != literal) return false;
            _position++;
            return true;
        }

        private PyToken ConsumeToken(TokenType type)
        {
            SkipCommentsAndNL();
            if (IsAtEnd || CurrentToken.Type != type)
            {
                throw new PySyntaxErrorException($"Expected {type} at position {_position}");
            }
            var token = CurrentToken;
            _position++;
            return token;
        }

        private PyToken ConsumeKeyword(string keyword)
        {
            SkipCommentsAndNL();
            if (IsAtEnd || CurrentToken.Type != TokenType.NAME || CurrentToken.Lexeme != keyword)
            {
                throw new PySyntaxErrorException($"Expected '{keyword}' at position {_position}");
            }
            var token = CurrentToken;
            _position++;
            return token;
        }

        private PyToken ConsumeLiteral(string literal)
        {
            SkipCommentsAndNL();
            if (IsAtEnd || CurrentToken.Lexeme != literal)
            {
                throw new PySyntaxErrorException($"Expected '{literal}' at position {_position}");
            }
            var token = CurrentToken;
            _position++;
            return token;
        }

        #endregion

        #region Cut Support

        private void Cut()
        {
            _cutCalled = true;
        }

        #endregion

        #region Helper Methods

        private List<Expression> GetExpressionList(Expression expr)
        {
            if (expr == null) return new List<Expression>();
            if (expr is TupleExpression tuple) return tuple.Elements;
            return new List<Expression> { expr };
        }

        private object ParseNumber(string text)
        {
            if (int.TryParse(text, out var intVal)) return intVal;
            if (double.TryParse(text, out var doubleVal)) return doubleVal;
            return text;
        }

        private Expression CreateBoolOp(string op, List<Expression> values)
        {
            return new BoolOpExpression(op, values);
        }

        private Expression CreateBinOp(string op, List<Expression> values)
        {
            Expression result = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                result = new BinaryOpExpression(result, op, values[i]);
            }
            return result;
        }

        private Expression CreateBinOpChain(Expression left, List<(string op, Expression right)> rights)
        {
            Expression result = left;
            foreach (var (op, right) in rights)
            {
                result = new BinaryOpExpression(result, ConvertOpToken(op), right);
            }
            return result;
        }

        private Expression CreateCompare(Expression left, List<(string op, Expression right)> comparisons)
        {
            // For now, only handle single comparison
            if (comparisons.Count == 1)
            {
                var (op, right) = comparisons[0];
                return new CompareExpression(left, op, right);
            }

            // For multiple comparisons, chain them with AND
            Expression result = new CompareExpression(left, comparisons[0].op, comparisons[0].right);
            for (int i = 1; i < comparisons.Count; i++)
            {
                var nextComp = new CompareExpression(comparisons[i-1].right, comparisons[i].op, comparisons[i].right);
                result = new BoolOpExpression("And", new List<Expression> { result, nextComp });
            }
            return result;
        }

        private string ConvertOpToken(string token)
        {
            return token switch
            {
                "+" => "Add",
                "-" => "Sub",
                "*" => "Mult",
                "/" => "Div",
                "//" => "FloorDiv",
                "%" => "Mod",
                "**" => "Pow",
                "<<" => "LShift",
                ">>" => "RShift",
                "|" => "BitOr",
                "^" => "BitXor",
                "&" => "BitAnd",
                "@" => "MatMult",
                _ => token
            };
        }

        private Expression CreateAttributeExpression(string baseName, List<string> attributes)
        {
            Expression result = new NameExpression(baseName);
            foreach (var attr in attributes)
            {
                result = new AttributeExpression(result, attr);
            }
            return result;
        }

        #endregion

        #region Main Grammar Rules

        /// <summary>
        /// statements: statement+
        /// Grammar: statements[List<Statement>]: a=statement+ { a.SelectMany(s => s).ToList() }
        /// </summary>
        public List<Statement> ParseStatements()
        {
            return ParseWithMemo<List<Statement>>("statements", () =>
            {
                var statements = new List<List<Statement>>();

                // PEG: statement+ means one or more statements
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: ParseStatements starting, position: {_position}, current token: {CurrentToken?.Type} '{CurrentToken?.Lexeme}'");
#endif
                var firstStmt = ParseStatement();
                if (firstStmt == null)
                {
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] PEG: ParseStatements failed to parse first statement");
#endif
                    return null; // No statements found
                }

#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: ParseStatements parsed first statement: {firstStmt.Count} statements");
#endif
                statements.Add(firstStmt);

                // Continue parsing additional statements
                while (!IsAtEnd && CurrentToken.Type != TokenType.ENDMARKER)
                {
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] PEG: ParseStatements continuing, position: {_position}, current token: {CurrentToken?.Type} '{CurrentToken?.Lexeme}'");
#endif
                    var stmt = ParseStatement();
                    if (stmt != null)
                    {
#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] PEG: ParseStatements parsed additional statement: {stmt.Count} statements");
#endif
                        statements.Add(stmt);
                    }
                    else
                    {
#if DEBUG_LOG
                        Console.WriteLine($"[DEBUG] PEG: ParseStatements failed to parse additional statement, breaking");
#endif
                        break;
                    }
                }

                // Flatten the list of statement lists (SelectMany behavior)
                return statements.SelectMany(s => s).ToList();
            });
        }

        /// <summary>
        /// statement: compound_stmt | simple_stmts
        /// Grammar: statement[List<Statement>]: | a=compound_stmt { new List<Statement> { a } } | a=simple_stmts { a }
        /// </summary>
        public List<Statement> ParseStatement()
        {
            return ParseWithMemo<List<Statement>>("statement", () =>
            {
                // PEG ordered choice: try compound_stmt first
                var startPos = _position;

#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: ParseStatement trying compound_stmt at position: {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Lexeme}'");
#endif
                var compound = ParseCompoundStmt();
                if (compound != null)
                {
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] PEG: ParseStatement parsed compound_stmt successfully");
#endif
                    return new List<Statement> { compound };
                }

#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: ParseStatement compound_stmt failed");
#endif

                // Reset position for backtracking (PEG behavior)
                _position = startPos;

                // Try simple_stmts
                var simple = ParseSimpleStmts();
                if (simple != null)
                {
                    return simple;
                }

                return null;
            });
        }

        /// <summary>
        /// simple_stmts: simple_stmt !';' NEWLINE | ';'.simple_stmt+ [';'] NEWLINE
        /// </summary>
        public List<Statement> ParseSimpleStmts()
        {
            return ParseWithMemo("simple_stmts", () =>
            {
                var statements = new List<Statement>();

                // First simple statement
                var firstStmt = ParseSimpleStmt();
                if (firstStmt == null) return null;

                statements.Add(firstStmt);

                // Check for semicolon-separated statements
                while (MatchLiteral(";"))
                {
                    var stmt = ParseSimpleStmt();
                    if (stmt == null) break;
                    statements.Add(stmt);
                }

                // Optional trailing semicolon
                MatchLiteral(";");

                // Must end with newline
                if (!MatchToken(TokenType.NEWLINE))
                {
                    return null;
                }

                return statements;
            });
        }

        /// <summary>
        /// simple_stmt: assignment | type_alias | star_expressions | return_stmt | import_stmt | raise_stmt | 'pass' | del_stmt | yield_stmt | assert_stmt | 'break' | 'continue' | global_stmt | nonlocal_stmt
        /// PEG: Ordered choice - assignment MUST be tried first per grammar
        /// </summary>
        public Statement ParseSimpleStmt()
        {
            return ParseWithMemo("simple_stmt", () =>
            {
                var startPos = _position;

                // PEG ordered choice: Try assignment first (must precede expression per grammar)
                var assignment = ParseAssignment();
                if (assignment != null) return assignment;

                // Reset position for next choice
                _position = startPos;

                // Try type alias
                if (CurrentToken?.Lexeme == "type")
                {
                    var typeAlias = ParseTypeAlias();
                    if (typeAlias != null) return typeAlias;
                    _position = startPos;
                }

                // Try specific keywords
                if (CurrentToken?.Lexeme == "return")
                {
                    return ParseReturnStmt();
                }

                if (CurrentToken?.Lexeme == "import" || CurrentToken?.Lexeme == "from")
                {
                    return ParseImportStmt();
                }

                if (CurrentToken?.Lexeme == "raise")
                {
                    return ParseRaiseStmt();
                }

                if (CurrentToken?.Lexeme == "pass")
                {
                    ConsumeKeyword("pass");
                    return new PassStatement();
                }

                if (CurrentToken?.Lexeme == "del")
                {
                    return ParseDelStmt();
                }

                if (CurrentToken?.Lexeme == "yield")
                {
                    var yieldExpr = ParseYieldStmt();
                    return new ExpressionStatement(yieldExpr);
                }

                if (CurrentToken?.Lexeme == "assert")
                {
                    return ParseAssertStmt();
                }

                if (CurrentToken?.Lexeme == "break")
                {
                    ConsumeKeyword("break");
                    return new BreakStatement();
                }

                if (CurrentToken?.Lexeme == "continue")
                {
                    ConsumeKeyword("continue");
                    return new ContinueStatement();
                }

                if (CurrentToken?.Lexeme == "global")
                {
                    return ParseGlobalStmt();
                }

                if (CurrentToken?.Lexeme == "nonlocal")
                {
                    return ParseNonlocalStmt();
                }

                // Default: star_expressions (expression statement)
                var expr = ParseStarExpressions();
                if (expr != null)
                {
                    return new ExpressionStatement(expr);
                }

                return null;
            });
        }

        /// <summary>
        /// compound_stmt: function_def | if_stmt | class_def | with_stmt | for_stmt | try_stmt | while_stmt | match_stmt
        /// </summary>
        public Statement ParseCompoundStmt()
        {
            return ParseWithMemo("compound_stmt", () =>
            {
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: ParseCompoundStmt called at position: {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Lexeme}'");
#endif
                // Check for decorators first
                if (CurrentToken?.Lexeme == "@")
                {
                    return ParseDecoratedStmt();
                }

                // Function definition
                if (CurrentToken?.Lexeme == "def" || CurrentToken?.Lexeme == "async")
                {
                    return ParseFunctionDef();
                }

                // If statement
                if (CurrentToken?.Lexeme == "if")
                {
                    return ParseIfStmt();
                }

                // Class definition
                if (CurrentToken?.Lexeme == "class")
                {
                    return ParseClassDef();
                }

                // With statement
                if (CurrentToken?.Lexeme == "with")
                {
                    return ParseWithStmt();
                }

                // For statement
                if (CurrentToken?.Lexeme == "for")
                {
                    return ParseForStmt();
                }

                // Try statement
                if (CurrentToken?.Lexeme == "try")
                {
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] PEG: ParseCompoundStmt found 'try', calling ParseTryStmt()");
#endif
                    return ParseTryStmt();
                }

                // While statement
                if (CurrentToken?.Lexeme == "while")
                {
                    return ParseWhileStmt();
                }

                // Match statement
                if (CurrentToken?.Lexeme == "match")
                {
                    return ParseMatchStmt();
                }

                return null;
            });
        }

        #endregion

        #region Assignment Statements

        /// <summary>
        /// assignment: NAME ':' expression ['=' annotated_rhs] | single_target ':' expression ['=' annotated_rhs] | star_targets_list '=' annotated_rhs | single_target augassign annotated_rhs
        /// PEG: Ordered choice exactly as defined in grammar
        /// </summary>
        public Statement ParseAssignment()
        {
            return ParseWithMemo<Statement>("assignment", () =>
            {
                var startPos = _position;

                // Choice 1: a=NAME ':' b=expression c=['=' d=annotated_rhs { d }]
                if (CurrentToken?.Type == TokenType.NAME)
                {
                    var nameToken = CurrentToken;
                    _position++;

                    if (MatchLiteral(":"))
                    {
                        var annotation = ParseExpression();
                        if (annotation != null)
                        {
                            Expression value = null;
                            if (MatchLiteral("="))
                            {
                                value = ParseAnnotatedRhs();
                                if (value == null)
                                {
                                    _position = startPos;
                                    return null;
                                }
                            }
                            return new AnnAssignStatement(nameToken.Lexeme, annotation, value);
                        }
                    }
                }

                // Reset for next choice
                _position = startPos;

                // Choice 2: a=single_target ':' b=expression c=['=' d=annotated_rhs { d }]
                var singleTarget = ParseSingleTarget();
                if (singleTarget != null && MatchLiteral(":"))
                {
                    var annotation = ParseExpression();
                    if (annotation != null)
                    {
                        Expression value = null;
                        if (MatchLiteral("="))
                        {
                            value = ParseAnnotatedRhs();
                            if (value == null)
                            {
                                _position = startPos;
                                return null;
                            }
                        }
                        var varName = singleTarget is NameExpression nameExpr ? nameExpr.Name : "unknown";
                        return new AnnAssignStatement(varName, annotation, value);
                    }
                }

                // Reset for next choice
                _position = startPos;

                // Choice 3: a=star_targets_list '=' b=annotated_rhs
                var targets = ParseStarTargetsList();
                if (targets != null && MatchLiteral("="))
                {
                    var value = ParseAnnotatedRhs();
                    if (value != null)
                    {
                        // Handle attribute assignment (obj.attr = value)
                        if (targets.Count > 0 && targets[0] is AttributeExpression attrExpr)
                        {
                            return new AssignTargetStatement(attrExpr, value);
                        }
                        // Handle regular variable assignment (name = value)
                        else if (targets.Count > 0 && targets[0] is NameExpression nameExpr)
                        {
                            return new AssignStatement(nameExpr.Name, value);
                        }
                        else
                        {
                            var varName = "unknown";
                            return new AssignStatement(varName, value);
                        }
                    }
                }

                // Reset for next choice
                _position = startPos;

                // Choice 4: a=single_target b=augassign c=annotated_rhs
                singleTarget = ParseSingleTarget();
                if (singleTarget != null)
                {
                    var augOp = ParseAugAssign();
                    if (augOp != null)
                    {
                        var value = ParseAnnotatedRhs();
                        if (value != null)
                        {
                            var varName = singleTarget is NameExpression nameExpr ? nameExpr.Name : singleTarget.ToString();
                            return new AugAssignStatement(varName, augOp, value);
                        }
                    }
                }

                _position = startPos;
                return null;
            });
        }

        /// <summary>
        /// For now, simplified to handle single target (most common case)
        /// TODO: Implement proper multiple assignment later
        /// </summary>
        public List<Expression> ParseStarTargetsList()
        {
            return ParseWithMemo<List<Expression>>("star_targets_list", () =>
            {
                var target = ParseStarTargets();
                if (target == null) return null;

                return new List<Expression> { target };
            });
        }

        public Expression ParseAnnotatedRhs()
        {
            // yield_expr | star_expressions
            var yieldExpr = ParseYieldExpr();
            if (yieldExpr != null) return yieldExpr;

            return ParseStarExpressions();
        }

        public string ParseAugAssign()
        {
            if (MatchLiteral("+=")) return "Add";
            if (MatchLiteral("-=")) return "Sub";
            if (MatchLiteral("*=")) return "Mult";
            if (MatchLiteral("@=")) return "MatMult";
            if (MatchLiteral("/=")) return "Div";
            if (MatchLiteral("%=")) return "Mod";
            if (MatchLiteral("&=")) return "BitAnd";
            if (MatchLiteral("|=")) return "BitOr";
            if (MatchLiteral("^=")) return "BitXor";
            if (MatchLiteral("<<=")) return "LShift";
            if (MatchLiteral(">>=")) return "RShift";
            if (MatchLiteral("**=")) return "Pow";
            if (MatchLiteral("//=")) return "FloorDiv";
            return null;
        }

        #endregion

        #region Expression Parsing (Simplified)

        /// <summary>
        /// expressions: expression (',' expression)+ [','] | expression
        /// </summary>
        public Expression ParseExpressions()
        {
            return ParseWithMemo("expressions", () =>
            {
                var first = ParseExpression();
                if (first == null) return null;

                var expressions = new List<Expression> { first };

                while (MatchLiteral(","))
                {
                    var expr = ParseExpression();
                    if (expr == null) break;
                    expressions.Add(expr);
                }

                return expressions.Count == 1 ? first : new TupleExpression(expressions);
            });
        }

        /// <summary>
        /// expression: disjunction 'if' disjunction 'else' expression | disjunction | lambdef
        /// </summary>
        public Expression ParseExpression()
        {
            return ParseWithMemo("expression", () =>
            {
                // Try lambda first
                if (CurrentToken?.Lexeme == "lambda")
                {
                    return ParseLambdef();
                }

                // Try conditional expression
                var disjunction = ParseDisjunction();
                if (disjunction != null)
                {
                    if (MatchKeyword("if"))
                    {
                        var condition = ParseDisjunction();
                        if (condition != null && MatchKeyword("else"))
                        {
                            var elseExpr = ParseExpression();
                            if (elseExpr != null)
                            {
                                return new ConditionalExpression(condition, disjunction, elseExpr);
                            }
                        }
                    }
                    return disjunction;
                }

                return null;
            });
        }

        /// <summary>
        /// disjunction: conjunction ('or' conjunction)+  | conjunction
        /// </summary>
        public Expression ParseDisjunction()
        {
            return ParseWithMemo("disjunction", () =>
            {
                var first = ParseConjunction();
                if (first == null) return null;

                var values = new List<Expression> { first };

                while (MatchKeyword("or"))
                {
                    var conjunction = ParseConjunction();
                    if (conjunction == null) break;
                    values.Add(conjunction);
                }

                return values.Count == 1 ? first : CreateBoolOp("Or", values);
            });
        }

        /// <summary>
        /// conjunction: inversion ('and' inversion)+ | inversion
        /// </summary>
        public Expression ParseConjunction()
        {
            return ParseWithMemo("conjunction", () =>
            {
                var first = ParseInversion();
                if (first == null) return null;

                var values = new List<Expression> { first };

                while (MatchKeyword("and"))
                {
                    var inversion = ParseInversion();
                    if (inversion == null) break;
                    values.Add(inversion);
                }

                return values.Count == 1 ? first : CreateBoolOp("And", values);
            });
        }

        /// <summary>
        /// inversion: 'not' inversion | comparison
        /// </summary>
        public Expression ParseInversion()
        {
            return ParseWithMemo("inversion", () =>
            {
                if (MatchKeyword("not"))
                {
                    var inversion = ParseInversion();
                    if (inversion != null)
                    {
                        return new UnaryOpExpression("Not", inversion);
                    }
                    return null;
                }

                return ParseComparison();
            });
        }

        /// <summary>
        /// Simplified comparison parsing
        /// </summary>
        public Expression ParseComparison()
        {
            return ParseWithMemo("comparison", () =>
            {
                var left = ParseBitwiseOr();
                if (left == null) return null;

                var comparisons = new List<(string op, Expression right)>();

                while (true)
                {
                    string op = null;
                    if (MatchLiteral("==")) op = "Eq";
                    else if (MatchLiteral("!=")) op = "NotEq";
                    else if (MatchLiteral("<=")) op = "LtE";
                    else if (MatchLiteral("<")) op = "Lt";
                    else if (MatchLiteral(">=")) op = "GtE";
                    else if (MatchLiteral(">")) op = "Gt";
                    else if (MatchKeyword("not") && MatchKeyword("in")) op = "NotIn";
                    else if (MatchKeyword("in")) op = "In";
                    else if (MatchKeyword("is") && MatchKeyword("not")) op = "IsNot";
                    else if (MatchKeyword("is")) op = "Is";
                    else break;

                    var right = ParseBitwiseOr();
                    if (right == null) break;

                    comparisons.Add((op, right));
                }

                return comparisons.Count == 0 ? left : CreateCompare(left, comparisons);
            });
        }

        /// <summary>
        /// Simplified arithmetic expression parsing
        /// </summary>
        public Expression ParseBitwiseOr()
        {
            return ParseWithMemo("bitwise_or", () =>
            {
                var first = ParseBitwiseXor();
                if (first == null) return null;

                var values = new List<Expression> { first };

                while (MatchLiteral("|"))
                {
                    var expr = ParseBitwiseXor();
                    if (expr == null) break;
                    values.Add(expr);
                }

                return values.Count == 1 ? first : CreateBinOp("BitOr", values);
            });
        }

        public Expression ParseBitwiseXor()
        {
            return ParseWithMemo("bitwise_xor", () =>
            {
                var first = ParseBitwiseAnd();
                if (first == null) return null;

                var values = new List<Expression> { first };

                while (MatchLiteral("^"))
                {
                    var expr = ParseBitwiseAnd();
                    if (expr == null) break;
                    values.Add(expr);
                }

                return values.Count == 1 ? first : CreateBinOp("BitXor", values);
            });
        }

        public Expression ParseBitwiseAnd()
        {
            return ParseWithMemo("bitwise_and", () =>
            {
                var first = ParseShiftExpr();
                if (first == null) return null;

                var values = new List<Expression> { first };

                while (MatchLiteral("&"))
                {
                    var expr = ParseShiftExpr();
                    if (expr == null) break;
                    values.Add(expr);
                }

                return values.Count == 1 ? first : CreateBinOp("BitAnd", values);
            });
        }

        public Expression ParseShiftExpr()
        {
            return ParseWithMemo("shift_expr", () =>
            {
                var left = ParseSum();
                if (left == null) return null;

                var rights = new List<(string op, Expression right)>();

                while (true)
                {
                    string op = null;
                    if (MatchLiteral("<<")) op = "LShift";
                    else if (MatchLiteral(">>")) op = "RShift";
                    else break;

                    var right = ParseSum();
                    if (right == null) break;

                    rights.Add((op, right));
                }

                return rights.Count == 0 ? left : CreateBinOpChain(left, rights);
            });
        }

        public Expression ParseSum()
        {
            return ParseWithMemo("sum", () =>
            {
                var left = ParseTerm();
                if (left == null) return null;

                var rights = new List<(string op, Expression right)>();

                while (true)
                {
                    string op = null;
                    if (MatchLiteral("+")) op = "Add";
                    else if (MatchLiteral("-")) op = "Sub";
                    else break;

                    var right = ParseTerm();
                    if (right == null) break;

                    rights.Add((op, right));
                }

                return rights.Count == 0 ? left : CreateBinOpChain(left, rights);
            });
        }

        public Expression ParseTerm()
        {
            return ParseWithMemo("term", () =>
            {
                var left = ParseFactor();
                if (left == null) return null;

                var rights = new List<(string op, Expression right)>();

                while (true)
                {
                    string op = null;
                    if (MatchLiteral("*")) op = "Mult";
                    else if (MatchLiteral("@")) op = "MatMult";
                    else if (MatchLiteral("/")) op = "Div";
                    else if (MatchLiteral("%")) op = "Mod";
                    else if (MatchLiteral("//")) op = "FloorDiv";
                    else break;

                    var right = ParseFactor();
                    if (right == null) break;

                    rights.Add((op, right));
                }

                return rights.Count == 0 ? left : CreateBinOpChain(left, rights);
            });
        }

        public Expression ParseFactor()
        {
            return ParseWithMemo("factor", () =>
            {
                if (MatchLiteral("+"))
                {
                    var factor = ParseFactor();
                    return factor != null ? new UnaryOpExpression("UAdd", factor) : null;
                }

                if (MatchLiteral("-"))
                {
                    var factor = ParseFactor();
                    return factor != null ? new UnaryOpExpression("USub", factor) : null;
                }

                if (MatchLiteral("~"))
                {
                    var factor = ParseFactor();
                    return factor != null ? new UnaryOpExpression("Invert", factor) : null;
                }

                return ParsePower();
            });
        }

        public Expression ParsePower()
        {
            return ParseWithMemo("power", () =>
            {
                var left = ParseAwaitPrimary();
                if (left == null) return null;

                if (MatchLiteral("**"))
                {
                    var right = ParseFactor();
                    if (right != null)
                    {
                        return new BinaryOpExpression(left, "Pow", right);
                    }
                }

                return left;
            });
        }

        public Expression ParseAwaitPrimary()
        {
            return ParseWithMemo("await_primary", () =>
            {
                if (MatchKeyword("await"))
                {
                    var primary = ParsePrimary();
                    return primary != null ? new AwaitExpression(primary) : null;
                }

                return ParsePrimary();
            });
        }

        /// <summary>
        /// Simplified primary expression parsing
        /// </summary>
        public Expression ParsePrimary()
        {
            return ParseWithMemo("primary", () =>
            {
                var atom = ParseAtom();
                if (atom == null) return null;

                // Handle trailers (attribute access, calls, subscripts)
                while (true)
                {
                    if (MatchLiteral("."))
                    {
                        var nameToken = ConsumeToken(TokenType.NAME);
                        atom = new AttributeExpression(atom, nameToken.Lexeme);
                    }
                    else if (MatchLiteral("("))
                    {
                        var args = ParseArguments();
                        if (!MatchLiteral(")"))
                        {
                            throw new PySyntaxErrorException("Expected ')' after function arguments");
                        }
                        atom = new CallExpression(atom, args?.Arguments ?? new List<Expression>(), args?.Keywords?.Cast<KeywordExpression>()?.ToList() ?? new List<KeywordExpression>());
                    }
                    else if (MatchLiteral("["))
                    {
                        var slice = ParseSlices();
                        if (!MatchLiteral("]"))
                        {
                            throw new PySyntaxErrorException("Expected ']' after subscript");
                        }
                        atom = new SubscriptExpression(atom, slice);
                    }
                    else
                    {
                        break;
                    }
                }

                return atom;
            });
        }

        /// <summary>
        /// atom: NAME | 'True' | 'False' | 'None' | NUMBER | strings | tuple | group | list | dict | set
        /// </summary>
        public Expression ParseAtom()
        {
            return ParseWithMemo("atom", () =>
            {
                // Identifier
                if (CurrentToken?.Type == TokenType.NAME)
                {
                    if (CurrentToken.Lexeme == "True")
                    {
                        _position++;
                        return new ConstantExpression(PyBool.True);
                    }
                    if (CurrentToken.Lexeme == "False")
                    {
                        _position++;
                        return new ConstantExpression(PyBool.False);
                    }
                    if (CurrentToken.Lexeme == "None")
                    {
                        _position++;
                        return new ConstantExpression(PyNone.Instance);
                    }

                    var nameToken = CurrentToken;
                    _position++;
                    return new NameExpression(nameToken.Lexeme);
                }

                // Number
                if (CurrentToken?.Type == TokenType.NUMBER)
                {
                    var numberToken = CurrentToken;
                    _position++;
                    var numberValue = ParseNumber(numberToken.Lexeme);
                    return new ConstantExpression(numberValue is int i ? new PyInt(i) : new PyFloat((double)numberValue));
                }

                // String
                if (CurrentToken?.Type == TokenType.STRING)
                {
                    var stringToken = CurrentToken;
                    _position++;
                    // Remove quotes to match original parser format
                    var stringValue = stringToken.Lexeme;
                    if (stringValue.StartsWith("\"") && stringValue.EndsWith("\""))
                    {
                        stringValue = stringValue.Substring(1, stringValue.Length - 2);
                    }
                    else if (stringValue.StartsWith("'") && stringValue.EndsWith("'"))
                    {
                        stringValue = stringValue.Substring(1, stringValue.Length - 2);
                    }
                    return new ConstantExpression(new PyString(stringValue));
                }

                // F-String support (CPython 3.12 compatible)
                if (CurrentToken?.Type == TokenType.FSTRING_START)
                {
                    return ParseFString();
                }

                // Collections
                if (MatchLiteral("("))
                {
                    return ParseTupleOrGroup();
                }

                if (MatchLiteral("["))
                {
                    return ParseList();
                }

                if (MatchLiteral("{"))
                {
                    return ParseDictOrSet();
                }

                return null;
            });
        }

        /// <summary>
        /// Parse f-string with CPython 3.12 compatible FORMAT_VALUE and BUILD_STRING generation
        /// </summary>
        public Expression ParseFString()
        {
            return ParseWithMemo("fstring", () =>
            {
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: ParseFString called, current token: {CurrentToken?.Type} '{CurrentToken?.Lexeme}'");
#endif
                if (CurrentToken?.Type != TokenType.FSTRING_START)
                    return null;

                _position++; // Consume FSTRING_START

                var values = new List<Expression>();

                while (_position < _tokens.Count &&
                       CurrentToken?.Type != TokenType.FSTRING_END)
                {
                    if (CurrentToken?.Type == TokenType.FSTRING_MIDDLE)
                    {
                        // String literal part
                        var stringValue = CurrentToken.Lexeme;
                        values.Add(new ConstantExpression(new PyString(stringValue)));
                        _position++;
                    }
                    else if (CurrentToken?.Type == TokenType.OP && CurrentToken.Lexeme == "{")
                    {
                        _position++; // Skip '{'

                        // Parse expression inside {}
                        var expr = ParseExpression();
                        if (expr != null)
                        {
                            // Create FORMAT_VALUE expression (CPython 3.12 style)
                            var formattedValue = new FStringFormattedValue(expr, null, null); // conversion=None, format_spec=None
#if DEBUG_LOG
                            Console.WriteLine($"[DEBUG] PEG: Created FStringFormattedValue for expression: {expr.GetType().Name}");
#endif
                            values.Add(formattedValue);
                        }

                        // Skip '}'
                        if (CurrentToken?.Type == TokenType.OP && CurrentToken.Lexeme == "}")
                        {
                            _position++;
                        }
                    }
                    else
                    {
                        _position++; // Skip unknown tokens
                    }
                }

                if (CurrentToken?.Type == TokenType.FSTRING_END)
                {
                    _position++; // Consume FSTRING_END
                }

                // Create CPython 3.12 compatible f-string expression
                return new FStringExpression(values);
            });
        }

        #endregion

        #region Placeholder methods for complex constructs

        // These methods need full implementation but are placeholders for now
        public Statement ParseDecoratedStmt() => null;

        /// <summary>
        /// function_def: decorators function_def_raw | function_def_raw
        /// PEG: Ordered choice - try decorators first
        /// </summary>
        public Statement ParseFunctionDef()
        {
            return ParseWithMemo<Statement>("function_def", () =>
            {
                var startPos = _position;

                // Choice 1: decorators function_def_raw
                var decorators = ParseDecorators();
                if (decorators != null)
                {
                    var funcDef = ParseFunctionDefRaw();
                    if (funcDef != null)
                    {
                        // Add decorators to function definition
                        return funcDef;
                    }
                }

                // Reset for next choice
                _position = startPos;

                // Choice 2: function_def_raw
                return ParseFunctionDefRaw();
            });
        }

        /// <summary>
        /// Simplified function_def_raw: 'def' NAME '(' params? ')' ':' block
        /// </summary>
        public Statement ParseFunctionDefRaw()
        {
            return ParseWithMemo<Statement>("function_def_raw", () =>
            {
                var startPos = _position;

                // Must start with 'def'
                if (!MatchKeyword("def")) return null;

                // Function name
                if (CurrentToken?.Type != TokenType.NAME)
                {
                    _position = startPos;
                    return null;
                }
                var nameToken = CurrentToken;
                _position++;

                // Parameters: '(' params? ')'
                if (!MatchLiteral("("))
                {
                    _position = startPos;
                    return null;
                }

                var parameters = ParseParams() ?? new List<string>();

                if (!MatchLiteral(")"))
                {
                    _position = startPos;
                    return null;
                }

                // Colon
                if (!MatchLiteral(":"))
                {
                    _position = startPos;
                    return null;
                }

                // Function body
                var body = ParseBlock();
                if (body == null)
                {
                    _position = startPos;
                    return null;
                }

                return new FunctionDefStatement(nameToken.Lexeme, parameters, body);
            });
        }

        /// <summary>
        /// if_stmt: 'if' named_expression ':' block elif_stmt* [else_block]
        /// Simplified: 'if' expression ':' block
        /// </summary>
        public Statement ParseIfStmt()
        {
            return ParseWithMemo<Statement>("if_stmt", () =>
            {
                var startPos = _position;

                if (!MatchKeyword("if")) return null;

                var condition = ParseExpression();
                if (condition == null)
                {
                    _position = startPos;
                    return null;
                }

                if (!MatchLiteral(":"))
                {
                    _position = startPos;
                    return null;
                }

                var body = ParseBlock();
                if (body == null)
                {
                    _position = startPos;
                    return null;
                }

                // For now, simplified without elif/else
                return new IfStatement(condition, body, new List<Statement>());
            });
        }

        /// <summary>
        /// class_def: 'class' NAME ['(' arguments? ')'] ':' block
        /// Simplified: 'class' NAME ':' block
        /// </summary>
        public Statement ParseClassDef()
        {
            return ParseWithMemo<Statement>("class_def", () =>
            {
                var startPos = _position;

                if (!MatchKeyword("class")) return null;

                if (CurrentToken?.Type != TokenType.NAME)
                {
                    _position = startPos;
                    return null;
                }
                var nameToken = CurrentToken;
                _position++;

                // Skip optional inheritance for now
                if (MatchLiteral("("))
                {
                    // Skip until closing parenthesis
                    int parenCount = 1;
                    while (parenCount > 0 && !IsAtEnd)
                    {
                        if (CurrentToken?.Lexeme == "(") parenCount++;
                        else if (CurrentToken?.Lexeme == ")") parenCount--;
                        _position++;
                    }
                }

                if (!MatchLiteral(":"))
                {
                    _position = startPos;
                    return null;
                }

                var body = ParseBlock();
                if (body == null)
                {
                    _position = startPos;
                    return null;
                }

                return new ClassDefStatement(nameToken.Lexeme, new List<Expression>(), body);
            });
        }
        public Statement ParseWithStmt() => null;

        /// <summary>
        /// for_stmt: 'for' star_targets 'in' star_expressions ':' block
        /// Simplified: 'for' NAME 'in' expression ':' block
        /// </summary>
        public Statement ParseForStmt()
        {
            return ParseWithMemo<Statement>("for_stmt", () =>
            {
                var startPos = _position;

                if (!MatchKeyword("for")) return null;

                // Target variable
                if (CurrentToken?.Type != TokenType.NAME)
                {
                    _position = startPos;
                    return null;
                }
                var targetName = CurrentToken.Lexeme;
                _position++;

                if (!MatchKeyword("in"))
                {
                    _position = startPos;
                    return null;
                }

                var iterable = ParseExpression();
                if (iterable == null)
                {
                    _position = startPos;
                    return null;
                }

                if (!MatchLiteral(":"))
                {
                    _position = startPos;
                    return null;
                }

                var body = ParseBlock();
                if (body == null)
                {
                    _position = startPos;
                    return null;
                }

                return new ForStatement(targetName, iterable, body, new List<Statement>());
            });
        }

        /// <summary>
        /// try_stmt: 'try' ':' block except_block+ [else_block] [finally_block]
        /// Simplified: 'try' ':' block except_block
        /// </summary>
        public Statement ParseTryStmt()
        {
            return ParseWithMemo<Statement>("try_stmt", () =>
            {
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: ParseTryStmt called at position: {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Lexeme}'");
#endif
                var startPos = _position;

                if (!MatchKeyword("try"))
                {
#if DEBUG_LOG
                    Console.WriteLine($"[DEBUG] PEG: ParseTryStmt failed to match 'try' keyword");
#endif
                    return null;
                }

#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: ParseTryStmt matched 'try', now at position: {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Lexeme}'");
#endif

                if (!MatchLiteral(":"))
                {
                    _position = startPos;
                    return null;
                }

                var tryBody = ParseBlock();
                if (tryBody == null)
                {
                    _position = startPos;
                    return null;
                }

                // Parse except clause
                if (!MatchKeyword("except"))
                {
                    _position = startPos;
                    return null;
                }

                // Optional exception type
                Expression exceptionType = null;
                if (CurrentToken?.Type == TokenType.NAME && CurrentToken.Lexeme != ":")
                {
                    exceptionType = ParseExpression();
                }

                if (!MatchLiteral(":"))
                {
                    _position = startPos;
                    return null;
                }

                var exceptBody = ParseBlock();
                if (exceptBody == null)
                {
                    _position = startPos;
                    return null;
                }

                var exceptHandler = new ExceptHandler(exceptionType, null, exceptBody);
                return new TryStatement(tryBody, new List<ExceptHandler> { exceptHandler }, new List<Statement>(), new List<Statement>());
            });
        }
        public Statement ParseWhileStmt() => null;
        public Statement ParseMatchStmt() => null;
        public Statement ParseTypeAlias() => null;

        /// <summary>
        /// return_stmt: 'return' [star_expressions]
        /// </summary>
        public Statement ParseReturnStmt()
        {
            return ParseWithMemo<Statement>("return_stmt", () =>
            {
                if (!MatchKeyword("return")) return null;

                var value = ParseStarExpressions();
                return new ReturnStatement(value);
            });
        }
        public Statement ParseImportStmt() => null;
        public Statement ParseRaiseStmt() => null;
        public Statement ParseDelStmt() => null;
        public Expression ParseYieldStmt() => null;
        public Statement ParseAssertStmt() => null;
        public Statement ParseGlobalStmt() => null;
        public Statement ParseNonlocalStmt() => null;
        public Expression ParseStarExpressions() => ParseExpression();
        public Expression ParseStarTargets() => ParseExpression();
        public Expression ParseSingleTarget() => ParseExpression();
        public Expression ParseYieldExpr() => null;
        public Expression ParseLambdef() => null;
        /// <summary>
        /// arguments: args (',' args)* [',']
        /// Simplified: expression (',' expression)*
        /// </summary>
        public ArgumentList ParseArguments()
        {
            var argList = new ArgumentList();

            // Empty arguments case
            if (CurrentToken?.Lexeme == ")") return argList;

            // First argument
            var firstArg = ParseExpression();
            if (firstArg != null)
            {
                argList.Arguments.Add(firstArg);

                // Additional arguments
                while (MatchLiteral(","))
                {
                    // Handle trailing comma
                    if (CurrentToken?.Lexeme == ")") break;

                    var arg = ParseExpression();
                    if (arg != null)
                    {
                        argList.Arguments.Add(arg);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return argList;
        }
        public Expression ParseSlices() => ParseExpression();
        public Expression ParseTupleOrGroup() => ParseExpression();
        /// <summary>
        /// Parse list with CPython 3.12 compatible BUILD_LIST generation
        /// list: '[' [star_named_expressions] ']'
        /// </summary>
        public Expression ParseList()
        {
            return ParseWithMemo("list", () =>
            {
                var elements = new List<Expression>();

                // Parse list elements
                if (!IsAtEnd && CurrentToken?.Type != TokenType.OP || CurrentToken?.Lexeme != "]")
                {
                    var firstExpr = ParseExpression();
                    if (firstExpr != null)
                    {
                        elements.Add(firstExpr);

                        // Parse remaining elements
                        while (MatchLiteral(","))
                        {
                            // Allow trailing comma
                            if (CurrentToken?.Type == TokenType.OP && CurrentToken?.Lexeme == "]")
                            {
                                break;
                            }

                            var expr = ParseExpression();
                            if (expr != null)
                            {
                                elements.Add(expr);
                            }
                        }
                    }
                }

                // Consume closing ']'
                if (!MatchLiteral("]"))
                {
                    return null; // Missing closing bracket
                }

                // Create CPython 3.12 compatible list expression
                return new ListExpression(elements);
            });
        }
        public Expression ParseDictOrSet() => ParseExpression();

        // Function definition helpers
        public List<object> ParseDecorators() => null; // For now, no decorators

        public List<string> ParseParams()
        {
            var parameters = new List<string>();

            // Simple parameter parsing: NAME (',' NAME)*
            if (CurrentToken?.Type == TokenType.NAME)
            {
                parameters.Add(CurrentToken.Lexeme);
                _position++;

                while (MatchLiteral(","))
                {
                    if (CurrentToken?.Type == TokenType.NAME)
                    {
                        parameters.Add(CurrentToken.Lexeme);
                        _position++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return parameters;
        }

        public List<Statement> ParseBlock()
        {
#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] PEG: ParseBlock called at position: {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Lexeme}'");
#endif
            // Simplified block parsing - expect NEWLINE INDENT statements DEDENT
            if (!MatchToken(TokenType.NEWLINE))
            {
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: ParseBlock failed to match NEWLINE");
#endif
                return null;
            }
            if (!MatchToken(TokenType.INDENT))
            {
#if DEBUG_LOG
                Console.WriteLine($"[DEBUG] PEG: ParseBlock failed to match INDENT");
#endif
                return null;
            }

#if DEBUG_LOG
            Console.WriteLine($"[DEBUG] PEG: ParseBlock matched NEWLINE+INDENT, now at position: {_position}, token: {CurrentToken?.Type} '{CurrentToken?.Lexeme}'");
#endif

            var statements = new List<Statement>();

            while (!IsAtEnd && CurrentToken.Type != TokenType.DEDENT)
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    statements.AddRange(stmt);
                }
                else
                {
                    break;
                }
            }

            if (!MatchToken(TokenType.DEDENT)) return null;

            return statements;
        }

        #endregion
    }

    // Support classes for argument parsing
    public class ArgumentList
    {
        public List<Expression> Arguments { get; set; } = new List<Expression>();
        public List<PegKeywordExpression> Keywords { get; set; } = new List<PegKeywordExpression>();
    }

    public class PegKeywordExpression
    {
        public string Keyword { get; set; }
        public Expression Value { get; set; }

        public PegKeywordExpression(string keyword, Expression value)
        {
            Keyword = keyword;
            Value = value;
        }
    }
}