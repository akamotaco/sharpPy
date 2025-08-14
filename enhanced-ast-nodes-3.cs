// enhanced_ast_nodes_3.cs - NEW FILE - Add new AST nodes for f-string, list comprehension, compound assignment, with statement, and del
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;

namespace SharpPy
{
    // F-String Node
    public class FStringNode : ASTNode
    {
        public string Template { get; }
        public List<(string Text, ASTNode Expression)> Parts { get; }

        public FStringNode(string template, int line = 0, int column = 0) : base(line, column)
        {
            Template = template;
            Parts = ParseFString(template);
        }

        private List<(string Text, ASTNode Expression)> ParseFString(string template)
        {
            var parts = new List<(string Text, ASTNode Expression)>();
            var regex = new Regex(@"\{([^}]+)\}");
            int lastIndex = 0;

            foreach (Match match in regex.Matches(template))
            {
                // Add text before the expression
                if (match.Index > lastIndex)
                {
                    parts.Add((template.Substring(lastIndex, match.Index - lastIndex), null));
                }

                // Parse the expression inside {}
                string exprStr = match.Groups[1].Value;
                
                try
                {
                    // Create a mini parser for the expression
                    var lexer = new Lexer(exprStr);
                    var tokens = lexer.Tokenize();
                    var parser = new Parser(tokens);
                    var expr = parser.ParseExpression();
                    
                    parts.Add(("", expr));
                }
                catch
                {
                    // If parsing fails, treat it as literal text
                    parts.Add(("{" + exprStr + "}", null));
                }
                
                lastIndex = match.Index + match.Length;
            }

            // Add remaining text
            if (lastIndex < template.Length)
            {
                parts.Add((template.Substring(lastIndex), null));
            }

            return parts;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var result = new StringBuilder();
                
                foreach (var (text, expr) in Parts)
                {
                    if (expr != null)
                    {
                        var value = expr.Evaluate(env);
                        result.Append(FormatValue(value));
                    }
                    else
                    {
                        result.Append(text);
                    }
                }
                
                return result.ToString();
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Error in f-string evaluation: {ex.Message}");
            }
        }

        private string FormatValue(object value)
        {
            if (value == null) return "None";
            if (value is bool b) return b ? "True" : "False";
            if (value is string s) return s;
            return value.ToString();
        }
    }

    // List Comprehension Node
    public class ListComprehensionNode : ASTNode
    {
        public ASTNode Expression { get; }
        public string Variable { get; }
        public ASTNode Iterable { get; }
        public ASTNode Condition { get; } // Optional if clause

        public ListComprehensionNode(ASTNode expression, string variable, ASTNode iterable, 
                                     ASTNode condition = null, int line = 0, int column = 0) 
            : base(line, column)
        {
            Expression = expression;
            Variable = variable;
            Iterable = iterable;
            Condition = condition;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var result = new PythonList();
                var iterableObj = Iterable.Evaluate(env);
                var items = GetIterableItems(iterableObj);

                // Create a new scope for the comprehension
                var comprehensionEnv = new Environment(env);

                foreach (var item in items)
                {
                    comprehensionEnv.SetVariable(Variable, item);
                    
                    // Check condition if exists
                    if (Condition != null)
                    {
                        var conditionResult = Condition.Evaluate(comprehensionEnv);
                        if (!IsTrue(conditionResult))
                            continue;
                    }
                    
                    // Evaluate expression and add to result
                    var value = Expression.Evaluate(comprehensionEnv);
                    result.Items.Add(value);
                }

                return result;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Error in list comprehension: {ex.Message}");
            }
        }

        private List<object> GetIterableItems(object obj)
        {
            if (obj is PythonList list) return list.Items;
            if (obj is PythonTuple tuple) return tuple.Items;
            if (obj is string str) return str.Select(c => c.ToString()).Cast<object>().ToList();
            if (obj is PythonDict dict) return dict.Items.Keys.ToList();
            throw CreateException("TypeError", $"'{obj?.GetType()}' object is not iterable");
        }

        private bool IsTrue(object obj)
        {
            if (obj == null) return false;
            if (obj is bool b) return b;
            if (obj is int i) return i != 0;
            if (obj is double d) return d != 0;
            if (obj is string s) return !string.IsNullOrEmpty(s);
            if (obj is PythonList l) return l.Items.Count > 0;
            if (obj is PythonTuple t) return t.Items.Count > 0;
            if (obj is PythonDict dict) return dict.Items.Count > 0;
            return true;
        }
    }

    // Compound Assignment Node (+=, -=, *=, /=, etc.)
    public class CompoundAssignmentNode : ASTNode
    {
        public string VariableName { get; }
        public string Operator { get; }
        public ASTNode Value { get; }

        public CompoundAssignmentNode(string name, string op, ASTNode value, int line = 0, int column = 0) 
            : base(line, column)
        {
            VariableName = name;
            Operator = op;
            Value = value;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var currentValue = env.GetVariable(VariableName);
                var newValue = Value.Evaluate(env);
                
                object result = Operator switch
                {
                    "+=" => ApplyAdd(currentValue, newValue),
                    "-=" => ApplySubtract(currentValue, newValue),
                    "*=" => ApplyMultiply(currentValue, newValue),
                    "/=" => ApplyDivide(currentValue, newValue),
                    "%=" => ApplyModulo(currentValue, newValue),
                    "**=" => ApplyPower(currentValue, newValue),
                    _ => throw CreateException("SyntaxError", $"Invalid compound assignment operator: {Operator}")
                };
                
                env.SetVariable(VariableName, result);
                return result;
            }
            catch (PythonException ex) when (ex.Type == "NameError")
            {
                throw CreateException("NameError", $"Name '{VariableName}' is not defined");
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Error in compound assignment: {ex.Message}");
            }
        }

        private object ApplyAdd(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Add(left, right);
            if (left is string || right is string)
                return left?.ToString() + right?.ToString();
            if (left is PythonList ll && right is PythonList rl)
            {
                ll.Items.AddRange(rl.Items);
                return ll;
            }
            throw CreateException("TypeError", $"unsupported operand type(s) for +=");
        }

        private object ApplySubtract(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Subtract(left, right);
            throw CreateException("TypeError", $"unsupported operand type(s) for -=");
        }

        private object ApplyMultiply(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Multiply(left, right);
            if (left is string s && NumberHelper.IsNumber(right))
                return string.Concat(Enumerable.Repeat(s, NumberHelper.ToInt(right)));
            if (left is PythonList list && NumberHelper.IsNumber(right))
            {
                var originalItems = new List<object>(list.Items);
                list.Items.Clear();
                for (int i = 0; i < NumberHelper.ToInt(right); i++)
                    list.Items.AddRange(originalItems);
                return list;
            }
            throw CreateException("TypeError", $"unsupported operand type(s) for *=");
        }

        private object ApplyDivide(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Divide(left, right);
            throw CreateException("TypeError", $"unsupported operand type(s) for /=");
        }

        private object ApplyModulo(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Modulo(left, right);
            throw CreateException("TypeError", $"unsupported operand type(s) for %=");
        }

        private object ApplyPower(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Power(left, right);
            throw CreateException("TypeError", $"unsupported operand type(s) for **=");
        }
    }

    // With Statement Node
    public class WithNode : ASTNode
    {
        public ASTNode ContextExpression { get; }
        public string Variable { get; } // Optional variable name for 'as' clause
        public List<ASTNode> Body { get; }

        public WithNode(ASTNode contextExpr, string variable, List<ASTNode> body, int line = 0, int column = 0) 
            : base(line, column)
        {
            ContextExpression = contextExpr;
            Variable = variable;
            Body = body;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var contextManager = ContextExpression.Evaluate(env);
                
                // Call __enter__ method
                object enterResult = null;
                if (contextManager is PythonInstance instance)
                {
                    try
                    {
                        var enterMethod = instance.GetAttribute("__enter__");
                        if (enterMethod is Function enterFunc)
                        {
                            enterResult = enterFunc.Call(new List<object>());
                        }
                        else
                        {
                            throw CreateException("AttributeError", "Context manager missing __enter__ method");
                        }
                    }
                    catch (PythonException ex) when (ex.Type == "AttributeError")
                    {
                        throw CreateException("AttributeError", "Context manager missing __enter__ method");
                    }
                }
                else if (contextManager is FileObject fileObj)
                {
                    // Built-in file object support
                    enterResult = fileObj;
                }
                else
                {
                    throw CreateException("TypeError", "Object does not support context management protocol");
                }
                
                // Assign to variable if 'as' clause is present
                if (!string.IsNullOrEmpty(Variable))
                {
                    env.SetVariable(Variable, enterResult);
                }
                
                object result = null;
                Exception caughtException = null;
                
                try
                {
                    // Execute body
                    foreach (var stmt in Body)
                    {
                        result = stmt.Evaluate(env);
                    }
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
                
                // Call __exit__ method
                if (contextManager is PythonInstance inst)
                {
                    try
                    {
                        var exitMethod = inst.GetAttribute("__exit__");
                        if (exitMethod is Function exitFunc)
                        {
                            var args = new List<object>();
                            if (caughtException != null)
                            {
                                args.Add(caughtException.GetType().Name);
                                args.Add(caughtException.Message);
                                args.Add(null); // traceback
                            }
                            else
                            {
                                args.Add(null);
                                args.Add(null);
                                args.Add(null);
                            }
                            
                            var suppressException = exitFunc.Call(args);
                            
                            // If __exit__ returns True, suppress the exception
                            if (caughtException != null && !IsTrue(suppressException))
                            {
                                throw caughtException;
                            }
                        }
                    }
                    catch (PythonException ex) when (ex.Type == "AttributeError")
                    {
                        // __exit__ not found
                        if (caughtException != null) throw caughtException;
                    }
                }
                else if (contextManager is FileObject fileObject)
                {
                    // Close file
                    fileObject.Close();
                    if (caughtException != null) throw caughtException;
                }
                
                return result;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex) when (!(ex is ReturnException || ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Error in with statement: {ex.Message}");
            }
        }

        private bool IsTrue(object obj)
        {
            if (obj == null) return false;
            if (obj is bool b) return b;
            if (obj is int i) return i != 0;
            if (obj is double d) return d != 0;
            if (obj is string s) return !string.IsNullOrEmpty(s);
            return true;
        }
    }

    // Del Node - NEW
    public class DelNode : ASTNode
    {
        public string VariableName { get; }

        public DelNode(string variableName, int line = 0, int column = 0) : base(line, column)
        {
            VariableName = variableName;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                env.DeleteVariable(VariableName);
                return null;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Error in del statement: {ex.Message}");
            }
        }
    }

    // File Object for with statement support - FIXED
    public class FileObject
    {
        private StreamReader reader;
        private StreamWriter writer;
        private string mode;
        private string path;
        private bool closed;

        public FileObject(string path, string mode = "r")
        {
            this.path = path;
            this.mode = mode;
            this.closed = false;

            if (mode.Contains("r"))
            {
                reader = new StreamReader(path);
            }
            else if (mode.Contains("w"))
            {
                writer = new StreamWriter(path, false);
            }
            else if (mode.Contains("a"))
            {
                writer = new StreamWriter(path, true);
            }
        }

        public string Read()
        {
            if (closed) throw new PythonException("ValueError", "I/O operation on closed file");
            if (reader == null) throw new PythonException("IOError", "File not open for reading");
            return reader.ReadToEnd();
        }

        public string ReadLine()
        {
            if (closed) throw new PythonException("ValueError", "I/O operation on closed file");
            if (reader == null) throw new PythonException("IOError", "File not open for reading");
            return reader.ReadLine();
        }

        public PythonList ReadLines()
        {
            if (closed) throw new PythonException("ValueError", "I/O operation on closed file");
            if (reader == null) throw new PythonException("IOError", "File not open for reading");
            
            var lines = new PythonList();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lines.Items.Add(line + "\n");
            }
            return lines;
        }

        public void Write(string text)
        {
            if (closed) throw new PythonException("ValueError", "I/O operation on closed file");
            if (writer == null) throw new PythonException("IOError", "File not open for writing");
            writer.Write(text);
            writer.Flush(); // Ensure data is written immediately
        }

        public void WriteLine(string text)
        {
            if (closed) throw new PythonException("ValueError", "I/O operation on closed file");
            if (writer == null) throw new PythonException("IOError", "File not open for writing");
            writer.WriteLine(text);
            writer.Flush(); // Ensure data is written immediately
        }

        public void Close()
        {
            if (!closed)
            {
                reader?.Close();
                writer?.Close();
                closed = true;
            }
        }

        // Get method for attribute access
        public object GetMethod(string name)
        {
            return name switch
            {
                "read" => new BuiltinFunction("read", args => Read()),
                "readline" => new BuiltinFunction("readline", args => ReadLine()),
                "readlines" => new BuiltinFunction("readlines", args => ReadLines()),
                "write" => new BuiltinFunction("write", args => {
                    if (args.Count != 1) throw new PythonException("TypeError", "write() takes exactly 1 argument");
                    string text = args[0]?.ToString() ?? "";
                    Write(text);
                    return text.Length;
                }),
                "close" => new BuiltinFunction("close", args => {
                    Close();
                    return null;
                }),
                "__enter__" => new BuiltinFunction("__enter__", args => this),
                "__exit__" => new BuiltinFunction("__exit__", args => {
                    Close();
                    return false;
                }),
                _ => throw new PythonException("AttributeError", $"'FileObject' has no attribute '{name}'")
            };
        }

        public override string ToString() => $"<file '{path}' mode '{mode}'>";
    }
}