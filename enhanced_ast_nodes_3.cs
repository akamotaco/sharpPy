// enhanced_ast_nodes_3.cs (일부) - PythonTypeObject 기반 추가 기능
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;

namespace SharpPy
{
    // F-String Node
    // enhanced_ast_nodes_3.cs의 FStringNode 클래스 수정
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
            int position = 0;
            
            while (position < template.Length)
            {
                int braceStart = template.IndexOf('{', position);
                
                if (braceStart == -1)
                {
                    // No more expressions, add remaining text
                    if (position < template.Length)
                    {
                        parts.Add((template.Substring(position), null));
                    }
                    break;
                }
                
                // Check for escaped braces {{
                if (braceStart + 1 < template.Length && template[braceStart + 1] == '{')
                {
                    // Add text including single {
                    parts.Add((template.Substring(position, braceStart - position) + "{", null));
                    position = braceStart + 2;
                    continue;
                }
                
                // Add text before the expression
                if (braceStart > position)
                {
                    parts.Add((template.Substring(position, braceStart - position), null));
                }
                
                // Find matching closing brace (handle nested braces in expressions)
                int braceEnd = braceStart + 1;
                int braceDepth = 1;
                
                while (braceEnd < template.Length && braceDepth > 0)
                {
                    if (template[braceEnd] == '{')
                        braceDepth++;
                    else if (template[braceEnd] == '}')
                    {
                        // Check for escaped closing brace }}
                        if (braceEnd + 1 < template.Length && template[braceEnd + 1] == '}')
                        {
                            braceEnd++; // Skip escaped brace
                        }
                        else
                        {
                            braceDepth--;
                        }
                    }
                    braceEnd++;
                }
                
                if (braceDepth != 0)
                {
                    // No matching brace found, treat as literal text
                    parts.Add((template.Substring(braceStart), null));
                    break;
                }
                
                // Extract and parse the expression
                string exprStr = template.Substring(braceStart + 1, braceEnd - braceStart - 2);
                
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
                
                position = braceEnd;
            }
            
            return parts;
        }
        
        private int FindMatchingBrace(string template, int start)
        {
            // Simple search for closing brace - handles basic cases
            // For nested braces in format specifiers, would need more complex parsing
            for (int i = start + 1; i < template.Length; i++)
            {
                if (template[i] == '}')
                {
                    // Check for escaped closing brace }}
                    if (i + 1 < template.Length && template[i + 1] == '}')
                    {
                        i++; // Skip escaped brace
                        continue;
                    }
                    return i;
                }
            }
            return -1;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var result = new StringBuilder();
                
                foreach (var (text, expr) in Parts)
                {
                    if (expr != null)
                    {
                        var value = expr.Evaluate(env);
                        result.Append(value.ToPythonString());
                    }
                    else
                    {
                        result.Append(text);
                    }
                }
                
                return new PythonString(result.ToString());
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

        public override PythonTypeObject Evaluate(Environment env)
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
                        if (!conditionResult.IsTrue())
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

        private List<PythonTypeObject> GetIterableItems(PythonTypeObject obj)
        {
            if (obj is PythonList list) return list.Items;
            if (obj is PythonTuple tuple) return tuple.Items;
            if (obj is PythonString str) 
                return str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList();
            if (obj is PythonDict dict) return dict.Items.Keys.ToList();
            throw CreateException("TypeError", $"'{obj?.Type}' object is not iterable");
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

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var currentValue = env.GetVariable(VariableName);
                var newValue = Value.Evaluate(env);
                
                PythonTypeObject result = Operator switch
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

        private PythonTypeObject ApplyAdd(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Add(right);
            if (left is PythonFloat lf) return lf.Add(right);
            if (left is PythonString ls) return ls.Add(right);
            if (left is PythonList ll && right is PythonList rl)
            {
                ll.Items.AddRange(rl.Items);
                return ll;
            }
            throw CreateException("TypeError", $"unsupported operand type(s) for +=");
        }

        private PythonTypeObject ApplySubtract(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Subtract(right);
            if (left is PythonFloat lf) return lf.Subtract(right);
            throw CreateException("TypeError", $"unsupported operand type(s) for -=");
        }

        private PythonTypeObject ApplyMultiply(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Multiply(right);
            if (left is PythonFloat lf) return lf.Multiply(right);
            if (left is PythonString ls && NumberHelper.IsNumber(right))
                return ls.Repeat(NumberHelper.ToInt(right));
            if (left is PythonList list && NumberHelper.IsNumber(right))
            {
                var originalItems = new List<PythonTypeObject>(list.Items);
                list.Items.Clear();
                for (int i = 0; i < NumberHelper.ToInt(right); i++)
                    list.Items.AddRange(originalItems);
                return list;
            }
            throw CreateException("TypeError", $"unsupported operand type(s) for *=");
        }

        private PythonTypeObject ApplyDivide(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Divide(right);
            if (left is PythonFloat lf) return lf.Divide(right);
            throw CreateException("TypeError", $"unsupported operand type(s) for /=");
        }

        private PythonTypeObject ApplyModulo(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Modulo(right);
            if (left is PythonFloat lf) return lf.Modulo(right);
            throw CreateException("TypeError", $"unsupported operand type(s) for %=");
        }

        private PythonTypeObject ApplyPower(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Power(right);
            if (left is PythonFloat lf) return lf.Power(right);
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

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var contextManager = ContextExpression.Evaluate(env);
                
                // Call __enter__ method
                PythonTypeObject enterResult = PythonNone.Instance;
                if (contextManager is PythonInstance instance)
                {
                    try
                    {
                        var enterMethod = instance.GetAttribute("__enter__");
                        if (enterMethod is Function enterFunc)
                        {
                            enterResult = enterFunc.Call(new List<PythonTypeObject>());
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
                
                PythonTypeObject result = PythonNone.Instance;
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
                            var args = new List<PythonTypeObject>();
                            if (caughtException != null)
                            {
                                args.Add(new PythonString(caughtException.GetType().Name));
                                args.Add(new PythonString(caughtException.Message));
                                args.Add(PythonNone.Instance); // traceback
                            }
                            else
                            {
                                args.Add(PythonNone.Instance);
                                args.Add(PythonNone.Instance);
                                args.Add(PythonNone.Instance);
                            }
                            
                            var suppressException = exitFunc.Call(args);
                            
                            // If __exit__ returns True, suppress the exception
                            if (caughtException != null && !suppressException.IsTrue())
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
    }

    // Del Node
    public class DelNode : ASTNode
    {
        public string VariableName { get; }

        public DelNode(string variableName, int line = 0, int column = 0) : base(line, column)
        {
            VariableName = variableName;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                env.DeleteVariable(VariableName);
                return PythonNone.Instance;
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

    // File Object for with statement support - Updated with PythonTypeObject
    public class FileObject : PythonTypeObject
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

        public override PythonType Type => PythonType.Instance;
        public override bool IsTrue() => !closed;
        public override string ToPythonString() => $"<file '{path}' mode '{mode}'>";
        public override object GetRawValue() => this;
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);

        public PythonString Read()
        {
            if (closed) throw new PythonException("ValueError", "I/O operation on closed file");
            if (reader == null) throw new PythonException("IOError", "File not open for reading");
            return new PythonString(reader.ReadToEnd());
        }

        public PythonString ReadLine()
        {
            if (closed) throw new PythonException("ValueError", "I/O operation on closed file");
            if (reader == null) throw new PythonException("IOError", "File not open for reading");
            var line = reader.ReadLine();
            return line != null ? new PythonString(line) : new PythonString("");
        }

        public PythonList ReadLines()
        {
            if (closed) throw new PythonException("ValueError", "I/O operation on closed file");
            if (reader == null) throw new PythonException("IOError", "File not open for reading");
            
            var lines = new PythonList();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lines.Items.Add(new PythonString(line + "\n"));
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
        public PythonTypeObject GetMethod(string name)
        {
            return name switch
            {
                "read" => new BuiltinFunction("read", args => Read()),
                "readline" => new BuiltinFunction("readline", args => ReadLine()),
                "readlines" => new BuiltinFunction("readlines", args => ReadLines()),
                "write" => new BuiltinFunction("write", args => {
                    if (args.Count != 1) throw new PythonException("TypeError", "write() takes exactly 1 argument");
                    string text = (args[0] as PythonString)?.Value ?? args[0].ToPythonString();
                    Write(text);
                    return new PythonInt(text.Length);
                }),
                "close" => new BuiltinFunction("close", args => {
                    Close();
                    return PythonNone.Instance;
                }),
                "__enter__" => new BuiltinFunction("__enter__", args => this),
                "__exit__" => new BuiltinFunction("__exit__", args => {
                    Close();
                    return new PythonBool(false);
                }),
                _ => throw new PythonException("AttributeError", $"'FileObject' has no attribute '{name}'")
            };
        }
    }
}