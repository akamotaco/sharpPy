using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpPy.PegParser
{
    /// <summary>
    /// Generates C# parser code from PEG grammar
    /// </summary>
    public class PegCodeGenerator
    {
        private readonly PegGrammar _grammar;
        private readonly StringBuilder _code;
        private int _indentLevel;

        public PegCodeGenerator(PegGrammar grammar)
        {
            _grammar = grammar ?? throw new ArgumentNullException(nameof(grammar));
            _code = new StringBuilder();
            _indentLevel = 0;
        }

        public string GenerateParser(string className = "PyPegParser", string namespaceName = "SharpPy")
        {
            _code.Clear();
            _indentLevel = 0;

            GenerateFileHeader(namespaceName);
            GenerateClassHeader(className);
            GenerateFields();
            GenerateConstructor(className);
            GenerateUtilityMethods();
            GenerateRuleMethods();
            GenerateClassFooter();
            GenerateNamespaceFooter();

            return _code.ToString();
        }

        private void GenerateFileHeader(string namespaceName)
        {
            WriteLine("using System;");
            WriteLine("using System.Collections.Generic;");
            WriteLine("using System.Linq;");
            WriteLine("using System.Text;");
            WriteLine();
            WriteLine($"namespace {namespaceName}");
            WriteLine("{");
            _indentLevel++;
        }

        private void GenerateClassHeader(string className)
        {
            WriteLine("/// <summary>");
            WriteLine("/// PEG parser generated from grammar");
            WriteLine("/// </summary>");
            WriteLine($"public class {className}");
            WriteLine("{");
            _indentLevel++;
        }

        private void GenerateFields()
        {
            WriteLine("private readonly List<PyToken> _tokens;");
            WriteLine("private int _position;");
            WriteLine("private readonly string _filename;");
            WriteLine("private readonly string _sourceCode;");
            WriteLine();
            WriteLine("// Memoization cache for left recursion support");
            WriteLine("private readonly Dictionary<(int position, string rule), (object result, int newPosition)> _memoCache");
            WriteLine("    = new Dictionary<(int, string), (object, int)>();");
            WriteLine();
            WriteLine("// Left recursion detection");
            WriteLine("private readonly Dictionary<(int position, string rule), bool> _recursionHead");
            WriteLine("    = new Dictionary<(int, string), bool>();");
            WriteLine();
            WriteLine("// Cut tracking for commit behavior");
            WriteLine("private bool _cutCalled = false;");
            WriteLine();
        }

        private void GenerateConstructor(string className)
        {
            WriteLine($"public {className}(List<PyToken> tokens, string filename = \"<unknown>\", string sourceCode = \"\")");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));");
            WriteLine("_position = 0;");
            WriteLine("_filename = filename;");
            WriteLine("_sourceCode = sourceCode;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        private void GenerateUtilityMethods()
        {
            // Token access methods
            WriteLine("private PyToken CurrentToken => _position < _tokens.Count ? _tokens[_position] : null;");
            WriteLine("private bool IsAtEnd => _position >= _tokens.Count;");
            WriteLine();

            // Match methods
            WriteLine("private bool MatchToken(PyTokenType type)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (IsAtEnd || CurrentToken.Type != type) return false;");
            WriteLine("_position++;");
            WriteLine("return true;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("private bool MatchKeyword(string keyword)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (IsAtEnd || CurrentToken.Type != PyTokenType.NAME || CurrentToken.Lexeme != keyword) return false;");
            WriteLine("_position++;");
            WriteLine("return true;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("private bool MatchLiteral(string literal)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (IsAtEnd || CurrentToken.Lexeme != literal) return false;");
            WriteLine("_position++;");
            WriteLine("return true;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Memoization methods
            WriteLine("private T WithMemoization<T>(string ruleName, Func<T> ruleFunc)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var key = (_position, ruleName);");
            WriteLine("if (_memoCache.TryGetValue(key, out var cached))");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position = cached.newPosition;");
            WriteLine("return (T)cached.result;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("// Left recursion detection");
            WriteLine("if (_recursionHead.ContainsKey(key))");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return default(T); // Fail on left recursion");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            WriteLine("_recursionHead[key] = true;");
            WriteLine("var startPos = _position;");
            WriteLine("var result = ruleFunc();");
            WriteLine("_recursionHead.Remove(key);");
            WriteLine();
            WriteLine("_memoCache[key] = (result, _position);");
            WriteLine("return result;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Cut support
            WriteLine("private void Cut()");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_cutCalled = true;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            // Helper methods for AST construction
            WriteLine("private List<Expression> GetExpressionList(Expression expr)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (expr == null) return new List<Expression>();");
            WriteLine("if (expr is TupleExpression tuple) return tuple.Elements;");
            WriteLine("return new List<Expression> { expr };");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("private object ParseNumber(string text)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("if (int.TryParse(text, out var intVal)) return intVal;");
            WriteLine("if (double.TryParse(text, out var doubleVal)) return doubleVal;");
            WriteLine("return text;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("private object ParseComplexNumber(string text)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("// Complex number parsing implementation");
            WriteLine("return text; // Simplified for now");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("private Expression CreateBoolOp(string op, List<Expression> values)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return new BoolOpExpression(op, values);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("private Expression CreateBinOp(string op, List<Expression> values)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("Expression result = values[0];");
            WriteLine("for (int i = 1; i < values.Count; i++)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("result = new BinaryOpExpression(result, op, values[i]);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return result;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("private Expression CreateBinOpChain(Expression left, List<Tuple<string, Expression>> rights)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("Expression result = left;");
            WriteLine("foreach (var (op, right) in rights)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("result = new BinaryOpExpression(result, ConvertOpToken(op), right);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return result;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("private Expression CreateCompare(Expression left, List<Tuple<string, Expression>> comparisons)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var ops = comparisons.Select(c => c.Item1).ToList();");
            WriteLine("var comparators = comparisons.Select(c => c.Item2).ToList();");
            WriteLine("return new CompareExpression(left, ops, comparators);");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("private string ConvertOpToken(string token)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("return token switch");
            WriteLine("{");
            _indentLevel++;
            WriteLine("\"+\" => \"Add\",");
            WriteLine("\"-\" => \"Sub\",");
            WriteLine("\"*\" => \"Mult\",");
            WriteLine("\"/\" => \"Div\",");
            WriteLine("\"//\" => \"FloorDiv\",");
            WriteLine("\"%\" => \"Mod\",");
            WriteLine("\"**\" => \"Pow\",");
            WriteLine("\"<<\" => \"LShift\",");
            WriteLine("\">>\" => \"RShift\",");
            WriteLine("\"|\" => \"BitOr\",");
            WriteLine("\"^\" => \"BitXor\",");
            WriteLine("\"&\" => \"BitAnd\",");
            WriteLine("\"@\" => \"MatMult\",");
            WriteLine("_ => token");
            _indentLevel--;
            WriteLine("};");
            _indentLevel--;
            WriteLine("}");
            WriteLine();

            WriteLine("private Expression CreateAttributeExpression(string baseName, List<string> attributes)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("Expression result = new NameExpression(baseName);");
            WriteLine("foreach (var attr in attributes)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("result = new AttributeExpression(result, attr);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("return result;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
        }

        private void GenerateRuleMethods()
        {
            foreach (var rule in _grammar.Rules)
            {
                GenerateRuleMethod(rule);
                WriteLine();
            }
        }

        private void GenerateRuleMethod(PegRule rule)
        {
            var returnType = rule.ReturnType;
            var methodName = $"Parse{CapitalizeFirst(rule.Name)}";

            WriteLine($"public {returnType} {methodName}()");
            WriteLine("{");
            _indentLevel++;

            if (rule.IsMemoized)
            {
                WriteLine($"return WithMemoization(\"{rule.Name}\", () =>");
                WriteLine("{");
                _indentLevel++;
            }

            WriteLine("var startPos = _position;");
            WriteLine("_cutCalled = false;");
            WriteLine();

            GenerateExpressionCode(rule.Expression, returnType);

            if (rule.IsMemoized)
            {
                _indentLevel--;
                WriteLine("});");
            }

            _indentLevel--;
            WriteLine("}");
        }

        private void GenerateExpressionCode(PegExpression expression, string expectedType)
        {
            switch (expression)
            {
                case ChoiceExpression choice:
                    GenerateChoiceCode(choice, expectedType);
                    break;

                case SequenceExpression sequence:
                    GenerateSequenceCode(sequence, expectedType);
                    break;

                case TerminalExpression terminal:
                    GenerateTerminalCode(terminal);
                    break;

                case RuleExpression rule:
                    GenerateRuleCallCode(rule, expectedType);
                    break;

                case OptionalExpression optional:
                    GenerateOptionalCode(optional, expectedType);
                    break;

                case ZeroOrMoreExpression zeroOrMore:
                    GenerateZeroOrMoreCode(zeroOrMore, expectedType);
                    break;

                case OneOrMoreExpression oneOrMore:
                    GenerateOneOrMoreCode(oneOrMore, expectedType);
                    break;

                case SeparatedListExpression separatedList:
                    GenerateSeparatedListCode(separatedList, expectedType);
                    break;

                case PositiveLookaheadExpression posLookahead:
                    GeneratePositiveLookaheadCode(posLookahead);
                    break;

                case NegativeLookaheadExpression negLookahead:
                    GenerateNegativeLookaheadCode(negLookahead);
                    break;

                case NamedExpression named:
                    GenerateNamedExpressionCode(named, expectedType);
                    break;

                case ActionExpression action:
                    GenerateActionCode(action);
                    break;

                case CutExpression cut:
                    WriteLine("Cut();");
                    break;

                case GroupExpression group:
                    GenerateExpressionCode(group.Expression, expectedType);
                    break;

                default:
                    WriteLine($"// TODO: Implement {expression.GetType().Name}");
                    WriteLine($"return default({expectedType});");
                    break;
            }
        }

        private void GenerateChoiceCode(ChoiceExpression choice, string expectedType)
        {
            for (int i = 0; i < choice.Alternatives.Count; i++)
            {
                var alternative = choice.Alternatives[i];

                WriteLine("// Alternative " + (i + 1));
                WriteLine("{");
                _indentLevel++;
                WriteLine("var choicePos = _position;");
                WriteLine("var cutState = _cutCalled;");

                GenerateExpressionCode(alternative, expectedType);

                if (i < choice.Alternatives.Count - 1)
                {
                    WriteLine("if (_cutCalled) return result;");
                    WriteLine("_position = choicePos;");
                    WriteLine("_cutCalled = cutState;");
                    _indentLevel--;
                    WriteLine("}");
                    WriteLine();
                }
                else
                {
                    _indentLevel--;
                    WriteLine("}");
                    WriteLine();
                    WriteLine($"return default({expectedType});");
                }
            }
        }

        private void GenerateSequenceCode(SequenceExpression sequence, string expectedType)
        {
            var variables = new List<string>();

            for (int i = 0; i < sequence.Elements.Count; i++)
            {
                var element = sequence.Elements[i];
                var varName = $"elem{i}";

                if (element is NamedExpression named)
                {
                    varName = named.Name;
                    GenerateExpressionCode(named.Expression, "object");
                }
                else if (element is ActionExpression action)
                {
                    GenerateActionCode(action);
                    continue;
                }
                else
                {
                    GenerateExpressionCode(element, "object");
                }

                variables.Add(varName);
                WriteLine($"var {varName} = result;");
                WriteLine($"if ({varName} == null) return default({expectedType});");
            }

            WriteLine($"return default({expectedType}); // TODO: Combine sequence results");
        }

        private void GenerateTerminalCode(TerminalExpression terminal)
        {
            if (terminal.IsLiteral)
            {
                WriteLine($"if (!MatchLiteral(\"{EscapeString(terminal.Value)}\"))");
                WriteLine("{");
                _indentLevel++;
                WriteLine("return null;");
                _indentLevel--;
                WriteLine("}");
                WriteLine("var result = CurrentToken?.Lexeme;");
            }
            else
            {
                // Token type
                var tokenType = GetTokenTypeFromName(terminal.Value);
                WriteLine($"if (!MatchToken({tokenType}))");
                WriteLine("{");
                _indentLevel++;
                WriteLine("return null;");
                _indentLevel--;
                WriteLine("}");
                WriteLine("var result = _tokens[_position - 1];");
            }
        }

        private void GenerateRuleCallCode(RuleExpression rule, string expectedType)
        {
            var methodName = $"Parse{CapitalizeFirst(rule.RuleName)}";
            WriteLine($"var result = {methodName}();");
            WriteLine("if (result == null) return null;");
        }

        private void GenerateOptionalCode(OptionalExpression optional, string expectedType)
        {
            WriteLine("var optionalPos = _position;");
            GenerateExpressionCode(optional.Expression, expectedType);
            WriteLine("if (result == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position = optionalPos;");
            WriteLine("result = null; // Optional failed, that's OK");
            _indentLevel--;
            WriteLine("}");
        }

        private void GenerateZeroOrMoreCode(ZeroOrMoreExpression zeroOrMore, string expectedType)
        {
            WriteLine("var results = new List<object>();");
            WriteLine("while (true)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var loopPos = _position;");
            GenerateExpressionCode(zeroOrMore.Expression, "object");
            WriteLine("if (result == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position = loopPos;");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("results.Add(result);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("var result = results;");
        }

        private void GenerateOneOrMoreCode(OneOrMoreExpression oneOrMore, string expectedType)
        {
            WriteLine("var results = new List<object>();");
            GenerateExpressionCode(oneOrMore.Expression, "object");
            WriteLine("if (result == null) return null;");
            WriteLine("results.Add(result);");
            WriteLine();
            WriteLine("while (true)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var loopPos = _position;");
            GenerateExpressionCode(oneOrMore.Expression, "object");
            WriteLine("if (result == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position = loopPos;");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("results.Add(result);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("var result = results;");
        }

        private void GenerateSeparatedListCode(SeparatedListExpression separatedList, string expectedType)
        {
            WriteLine("var results = new List<object>();");
            GenerateExpressionCode(separatedList.Element, "object");
            WriteLine("if (result == null) return null;");
            WriteLine("results.Add(result);");
            WriteLine();
            WriteLine("while (true)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("var sepPos = _position;");
            GenerateExpressionCode(separatedList.Separator, "object");
            WriteLine("if (result == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position = sepPos;");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("}");
            WriteLine();
            GenerateExpressionCode(separatedList.Element, "object");
            WriteLine("if (result == null)");
            WriteLine("{");
            _indentLevel++;
            WriteLine("_position = sepPos;");
            WriteLine("break;");
            _indentLevel--;
            WriteLine("}");
            WriteLine("results.Add(result);");
            _indentLevel--;
            WriteLine("}");
            WriteLine("var result = results;");
        }

        private void GeneratePositiveLookaheadCode(PositiveLookaheadExpression posLookahead)
        {
            WriteLine("var lookaheadPos = _position;");
            GenerateExpressionCode(posLookahead.Expression, "object");
            WriteLine("_position = lookaheadPos;");
            WriteLine("if (result == null) return null;");
            WriteLine("var result = true; // Positive lookahead succeeded");
        }

        private void GenerateNegativeLookaheadCode(NegativeLookaheadExpression negLookahead)
        {
            WriteLine("var lookaheadPos = _position;");
            GenerateExpressionCode(negLookahead.Expression, "object");
            WriteLine("_position = lookaheadPos;");
            WriteLine("if (result != null) return null;");
            WriteLine("var result = true; // Negative lookahead succeeded");
        }

        private void GenerateNamedExpressionCode(NamedExpression named, string expectedType)
        {
            GenerateExpressionCode(named.Expression, expectedType);
            WriteLine($"var {named.Name} = result;");
        }

        private void GenerateActionCode(ActionExpression action)
        {
            WriteLine($"var result = {action.Code};");
        }

        private void GenerateClassFooter()
        {
            _indentLevel--;
            WriteLine("}");
        }

        private void GenerateNamespaceFooter()
        {
            _indentLevel--;
            WriteLine("}");
        }

        private void WriteLine(string line = "")
        {
            if (!string.IsNullOrEmpty(line))
            {
                _code.Append(new string(' ', _indentLevel * 4));
                _code.AppendLine(line);
            }
            else
            {
                _code.AppendLine();
            }
        }

        private string CapitalizeFirst(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpper(text[0]) + text.Substring(1);
        }

        private string EscapeString(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private string GetTokenTypeFromName(string name)
        {
            return name switch
            {
                "NAME" => "PyTokenType.NAME",
                "NUMBER" => "PyTokenType.NUMBER",
                "STRING" => "PyTokenType.STRING",
                "NEWLINE" => "PyTokenType.NEWLINE",
                "INDENT" => "PyTokenType.INDENT",
                "DEDENT" => "PyTokenType.DEDENT",
                "ENDMARKER" => "PyTokenType.ENDMARKER",
                "OP" => "PyTokenType.OP",
                "ASYNC" => "PyTokenType.ASYNC",
                "AWAIT" => "PyTokenType.AWAIT",
                "TYPE_COMMENT" => "PyTokenType.TYPE_COMMENT",
                _ => $"PyTokenType.{name}"
            };
        }
    }
}