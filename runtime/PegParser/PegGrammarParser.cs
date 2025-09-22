using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpPy.PegParser
{
    /// <summary>
    /// Parser for PEG grammar files
    /// </summary>
    public class PegGrammarParser
    {
        private readonly string _input;
        private int _position;
        private int _line;
        private int _column;

        public PegGrammarParser(string input)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _position = 0;
            _line = 1;
            _column = 1;
        }

        public static PegGrammar ParseFile(string filename)
        {
            var content = File.ReadAllText(filename);
            return ParseString(content);
        }

        public static PegGrammar ParseString(string input)
        {
            var parser = new PegGrammarParser(input);
            return parser.ParseGrammar();
        }

        public PegGrammar ParseGrammar()
        {
            var rules = new List<PegRule>();

            SkipWhitespaceAndComments();

            while (!IsAtEnd())
            {
                var rule = ParseRule();
                if (rule != null)
                {
                    rules.Add(rule);
                }
                SkipWhitespaceAndComments();
            }

            return new PegGrammar(rules);
        }

        private PegRule ParseRule()
        {
            // Parse rule name
            var name = ParseIdentifier();
            if (string.IsNullOrEmpty(name))
            {
                throw new PegParseException($"Expected rule name at line {_line}, column {_column}");
            }

            // Parse optional return type [ReturnType]
            string returnType = "object";
            SkipWhitespace();
            if (Peek() == '[')
            {
                Advance(); // consume '['
                SkipWhitespace();
                returnType = ParseReturnType();
                SkipWhitespace();
                if (!Match(']'))
                {
                    throw new PegParseException($"Expected ']' after return type at line {_line}, column {_column}");
                }
            }

            SkipWhitespace();
            if (!Match(':'))
            {
                throw new PegParseException($"Expected ':' after rule name at line {_line}, column {_column}");
            }

            SkipWhitespace();

            // Check for (memo) annotation
            bool isMemoized = false;
            if (Match("(memo)"))
            {
                isMemoized = true;
                SkipWhitespace();
                if (!Match(':'))
                {
                    throw new PegParseException($"Expected ':' after (memo) at line {_line}, column {_column}");
                }
                SkipWhitespace();
            }

            // Parse expression
            var expression = ParseExpression();

            return new PegRule(name, returnType, expression, isMemoized);
        }

        private string ParseReturnType()
        {
            var sb = new StringBuilder();
            var depth = 0;

            while (!IsAtEnd())
            {
                var ch = Peek();
                if (ch == '[') depth++;
                else if (ch == ']')
                {
                    if (depth == 0) break;
                    depth--;
                }
                else if (ch == '<') depth++;
                else if (ch == '>') depth--;

                sb.Append(Advance());
            }

            return sb.ToString().Trim();
        }

        private PegExpression ParseExpression()
        {
            return ParseChoice();
        }

        private PegExpression ParseChoice()
        {
            var alternatives = new List<PegExpression>();

            // Handle leading '|' for multiline alternatives
            SkipWhitespace();
            if (Peek() == '|')
            {
                Advance();
                SkipWhitespace();
            }

            alternatives.Add(ParseSequence());

            while (true)
            {
                SkipWhitespace();
                if (!Match('|')) break;
                SkipWhitespace();
                alternatives.Add(ParseSequence());
            }

            return alternatives.Count == 1 ? alternatives[0] : new ChoiceExpression(alternatives);
        }

        private PegExpression ParseSequence()
        {
            var elements = new List<PegExpression>();

            SkipWhitespace();
            while (!IsAtEnd() && !IsSequenceTerminator())
            {
                var element = ParsePrimary();
                if (element == null) break;
                elements.Add(element);
                SkipWhitespace();
            }

            if (elements.Count == 0)
            {
                throw new PegParseException($"Empty sequence at line {_line}, column {_column}");
            }

            return elements.Count == 1 ? elements[0] : new SequenceExpression(elements);
        }

        private bool IsSequenceTerminator()
        {
            var ch = Peek();
            return ch == '|' || ch == '\n' || ch == '\r' || ch == '{' || IsAtEnd();
        }

        private PegExpression ParsePrimary()
        {
            SkipWhitespace();

            var ch = Peek();
            switch (ch)
            {
                case '&':
                    Advance();
                    return new PositiveLookaheadExpression(ParsePrimary());

                case '!':
                    Advance();
                    return new NegativeLookaheadExpression(ParsePrimary());

                case '~':
                    Advance();
                    return new CutExpression();

                case '(':
                    return ParseGroup();

                case '[':
                    return ParseOptional();

                case '\'':
                    return ParseStringLiteral();

                case '"':
                    return ParseSoftKeyword();

                case '{':
                    return ParseAction();

                default:
                    if (char.IsLetter(ch) || ch == '_')
                    {
                        return ParseNamedExpressionOrRule();
                    }
                    break;
            }

            return null;
        }

        private PegExpression ParseGroup()
        {
            if (!Match('('))
            {
                throw new PegParseException($"Expected '(' at line {_line}, column {_column}");
            }

            SkipWhitespace();
            var expression = ParseExpression();
            SkipWhitespace();

            if (!Match(')'))
            {
                throw new PegParseException($"Expected ')' at line {_line}, column {_column}");
            }

            return ParseSuffix(new GroupExpression(expression));
        }

        private PegExpression ParseOptional()
        {
            if (!Match('['))
            {
                throw new PegParseException($"Expected '[' at line {_line}, column {_column}");
            }

            SkipWhitespace();
            var expression = ParseExpression();
            SkipWhitespace();

            if (!Match(']'))
            {
                throw new PegParseException($"Expected ']' at line {_line}, column {_column}");
            }

            return new OptionalExpression(expression);
        }

        private PegExpression ParseStringLiteral()
        {
            if (!Match('\''))
            {
                throw new PegParseException($"Expected '\'' at line {_line}, column {_column}");
            }

            var sb = new StringBuilder();
            while (!IsAtEnd() && Peek() != '\'')
            {
                var ch = Advance();
                if (ch == '\\' && !IsAtEnd())
                {
                    ch = Advance(); // Handle escape sequences
                }
                sb.Append(ch);
            }

            if (!Match('\''))
            {
                throw new PegParseException($"Unterminated string literal at line {_line}, column {_column}");
            }

            return new TerminalExpression(sb.ToString(), true);
        }

        private PegExpression ParseSoftKeyword()
        {
            if (!Match('"'))
            {
                throw new PegParseException($"Expected '\"' at line {_line}, column {_column}");
            }

            var sb = new StringBuilder();
            while (!IsAtEnd() && Peek() != '"')
            {
                var ch = Advance();
                if (ch == '\\' && !IsAtEnd())
                {
                    ch = Advance(); // Handle escape sequences
                }
                sb.Append(ch);
            }

            if (!Match('"'))
            {
                throw new PegParseException($"Unterminated soft keyword at line {_line}, column {_column}");
            }

            return new TerminalExpression(sb.ToString(), true);
        }

        private PegExpression ParseAction()
        {
            if (!Match('{'))
            {
                throw new PegParseException($"Expected '{{' at line {_line}, column {_column}");
            }

            var sb = new StringBuilder();
            var depth = 1;

            while (!IsAtEnd() && depth > 0)
            {
                var ch = Advance();
                if (ch == '{') depth++;
                else if (ch == '}') depth--;

                if (depth > 0)
                {
                    sb.Append(ch);
                }
            }

            if (depth > 0)
            {
                throw new PegParseException($"Unterminated action at line {_line}, column {_column}");
            }

            return new ActionExpression(sb.ToString().Trim());
        }

        private PegExpression ParseNamedExpressionOrRule()
        {
            var identifier = ParseIdentifier();
            if (string.IsNullOrEmpty(identifier))
            {
                return null;
            }

            SkipWhitespace();

            // Check for variable binding (a=expression)
            if (Match('='))
            {
                SkipWhitespace();
                var expression = ParsePrimary();
                return new NamedExpression(identifier, expression);
            }

            // Check for separated list (s.e+)
            if (Match('.'))
            {
                SkipWhitespace();
                var element = ParsePrimary();
                if (element != null && Match('+'))
                {
                    return new SeparatedListExpression(new RuleExpression(identifier), element);
                }
                throw new PegParseException($"Invalid separated list syntax at line {_line}, column {_column}");
            }

            // It's a rule reference
            var rule = new RuleExpression(identifier);
            return ParseSuffix(rule);
        }

        private PegExpression ParseSuffix(PegExpression expression)
        {
            while (true)
            {
                var ch = Peek();
                switch (ch)
                {
                    case '*':
                        Advance();
                        expression = new ZeroOrMoreExpression(expression);
                        break;

                    case '+':
                        Advance();
                        expression = new OneOrMoreExpression(expression);
                        break;

                    case '?':
                        Advance();
                        expression = new OptionalExpression(expression);
                        break;

                    default:
                        return expression;
                }
            }
        }

        private string ParseIdentifier()
        {
            if (!char.IsLetter(Peek()) && Peek() != '_')
            {
                return null;
            }

            var sb = new StringBuilder();
            while (!IsAtEnd() && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
            {
                sb.Append(Advance());
            }

            return sb.ToString();
        }

        private void SkipWhitespaceAndComments()
        {
            while (!IsAtEnd())
            {
                var ch = Peek();
                if (char.IsWhiteSpace(ch))
                {
                    Advance();
                }
                else if (ch == '#')
                {
                    // Skip comment until end of line
                    while (!IsAtEnd() && Peek() != '\n' && Peek() != '\r')
                    {
                        Advance();
                    }
                }
                else
                {
                    break;
                }
            }
        }

        private void SkipWhitespace()
        {
            while (!IsAtEnd() && char.IsWhiteSpace(Peek()) && Peek() != '\n' && Peek() != '\r')
            {
                Advance();
            }
        }

        private char Peek()
        {
            return IsAtEnd() ? '\0' : _input[_position];
        }

        private char Advance()
        {
            if (IsAtEnd()) return '\0';

            var ch = _input[_position++];
            if (ch == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }

            return ch;
        }

        private bool Match(char expected)
        {
            if (Peek() != expected) return false;
            Advance();
            return true;
        }

        private bool Match(string expected)
        {
            if (_position + expected.Length > _input.Length)
                return false;

            for (int i = 0; i < expected.Length; i++)
            {
                if (_input[_position + i] != expected[i])
                    return false;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                Advance();
            }

            return true;
        }

        private bool IsAtEnd()
        {
            return _position >= _input.Length;
        }
    }

    public class PegParseException : Exception
    {
        public PegParseException(string message) : base(message) { }
        public PegParseException(string message, Exception innerException) : base(message, innerException) { }
    }
}