// enhanced_operators.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    // Enhanced Binary Operator Node with Line/Column Tracking
    public class BinaryOpNode : ASTNode
    {
        public ASTNode Left { get; }
        public string Operator { get; }
        public ASTNode Right { get; }

        public BinaryOpNode(ASTNode left, string op, ASTNode right, int line = 0, int column = 0) : base(line, column)
        {
            Left = left;
            Operator = op;
            Right = right;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var leftVal = Left.Evaluate(env);
                var rightVal = Right.Evaluate(env);

                return Operator switch
                {
                    "+" => Add(leftVal, rightVal),
                    "-" => Subtract(leftVal, rightVal),
                    "*" => Multiply(leftVal, rightVal),
                    "/" => Divide(leftVal, rightVal),
                    "%" => Modulo(leftVal, rightVal),
                    "**" => Power(leftVal, rightVal),
                    "==" => IsEqual(leftVal, rightVal),
                    "!=" => !IsEqual(leftVal, rightVal),
                    "<" => IsLess(leftVal, rightVal),
                    ">" => IsGreater(leftVal, rightVal),
                    "<=" => IsLessOrEqual(leftVal, rightVal),
                    ">=" => IsGreaterOrEqual(leftVal, rightVal),
                    "and" => IsTrue(leftVal) && IsTrue(rightVal),
                    "or" => IsTrue(leftVal) || IsTrue(rightVal),
                    "in" => IsIn(leftVal, rightVal),
                    "is" => IsIdentical(leftVal, rightVal),
                    "is not" => !IsIdentical(leftVal, rightVal),
                    _ => throw CreateException("TypeError", $"Unknown operator: {Operator}")
                };
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in binary operation '{Operator}': {ex.Message}");
            }
        }

        private object Add(object left, object right)
        {
            // Numeric addition
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Add(left, right);
            
            // String concatenation
            if (left is string || right is string) 
                return left?.ToString() + right?.ToString();
            
            // List concatenation
            if (left is PythonList ll && right is PythonList rl)
            {
                var newList = new PythonList();
                newList.Items.AddRange(ll.Items);
                newList.Items.AddRange(rl.Items);
                return newList;
            }
            
            // Tuple concatenation
            if (left is PythonTuple lt && right is PythonTuple rt)
            {
                var newTuple = new PythonTuple();
                newTuple.Items.AddRange(lt.Items);
                newTuple.Items.AddRange(rt.Items);
                return newTuple;
            }
            
            // Dict concatenation
            if (left is PythonDict ld && right is PythonDict rd)
            {
                var newDict = new PythonDict();
                foreach (var kvp in ld.Items) newDict.Items[kvp.Key] = kvp.Value;
                foreach (var kvp in rd.Items) newDict.Items[kvp.Key] = kvp.Value;
                return newDict;
            }
            
            throw CreateException("TypeError", $"Cannot add {GetTypeName(left)} and {GetTypeName(right)}");
        }

        private object Subtract(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Subtract(left, right);
            
            throw CreateException("TypeError", $"Cannot subtract {GetTypeName(right)} from {GetTypeName(left)}");
        }

        private object Multiply(object left, object right)
        {
            // Numeric multiplication
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Multiply(left, right);
            
            // String repetition
            if (left is string s && NumberHelper.IsNumber(right))
                return string.Concat(Enumerable.Repeat(s, NumberHelper.ToInt(right)));
            if (NumberHelper.IsNumber(left) && right is string s2)
                return string.Concat(Enumerable.Repeat(s2, NumberHelper.ToInt(left)));
            
            // List repetition
            if (left is PythonList list && NumberHelper.IsNumber(right))
            {
                var newList = new PythonList();
                for (int i = 0; i < NumberHelper.ToInt(right); i++)
                    newList.Items.AddRange(list.Items);
                return newList;
            }
            if (NumberHelper.IsNumber(left) && right is PythonList list2)
            {
                var newList = new PythonList();
                for (int i = 0; i < NumberHelper.ToInt(left); i++)
                    newList.Items.AddRange(list2.Items);
                return newList;
            }
            
            // Tuple repetition
            if (left is PythonTuple tuple && NumberHelper.IsNumber(right))
            {
                var newTuple = new PythonTuple();
                for (int i = 0; i < NumberHelper.ToInt(right); i++)
                    newTuple.Items.AddRange(tuple.Items);
                return newTuple;
            }
            if (NumberHelper.IsNumber(left) && right is PythonTuple tuple2)
            {
                var newTuple = new PythonTuple();
                for (int i = 0; i < NumberHelper.ToInt(left); i++)
                    newTuple.Items.AddRange(tuple2.Items);
                return newTuple;
            }
            
            throw CreateException("TypeError", $"Cannot multiply {GetTypeName(left)} and {GetTypeName(right)}");
        }

        private object Divide(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Divide(left, right);
            
            throw CreateException("TypeError", $"Cannot divide {GetTypeName(left)} by {GetTypeName(right)}");
        }

        private object Modulo(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Modulo(left, right);
            
            throw CreateException("TypeError", $"Cannot modulo {GetTypeName(left)} by {GetTypeName(right)}");
        }

        private object Power(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.Power(left, right);
            
            throw CreateException("TypeError", $"Cannot raise {GetTypeName(left)} to power of {GetTypeName(right)}");
        }

        private bool IsEqual(object left, object right) => Equals(left, right);

        private bool IsLess(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.ToDouble(left) < NumberHelper.ToDouble(right);
            if (left is string ls && right is string rs) 
                return string.Compare(ls, rs) < 0;
            
            throw CreateException("TypeError", $"'<' not supported between instances of '{GetTypeName(left)}' and '{GetTypeName(right)}'");
        }

        private bool IsGreater(object left, object right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.ToDouble(left) > NumberHelper.ToDouble(right);
            if (left is string ls && right is string rs) 
                return string.Compare(ls, rs) > 0;
            
            throw CreateException("TypeError", $"'>' not supported between instances of '{GetTypeName(left)}' and '{GetTypeName(right)}'");
        }

        private bool IsLessOrEqual(object left, object right)
        {
            try { return IsEqual(left, right) || IsLess(left, right); }
            catch { throw CreateException("TypeError", $"'<=' not supported between instances of '{GetTypeName(left)}' and '{GetTypeName(right)}'"); }
        }

        private bool IsGreaterOrEqual(object left, object right)
        {
            try { return IsEqual(left, right) || IsGreater(left, right); }
            catch { throw CreateException("TypeError", $"'>=' not supported between instances of '{GetTypeName(left)}' and '{GetTypeName(right)}'"); }
        }

        private bool IsIn(object item, object container)
        {
            if (container is PythonList list) return list.Items.Contains(item);
            if (container is PythonTuple tuple) return tuple.Items.Contains(item);
            if (container is string str && item is string s) return str.Contains(s);
            if (container is PythonDict dict) return dict.Items.ContainsKey(item);
            throw CreateException("TypeError", $"argument of type '{GetTypeName(container)}' is not iterable");
        }

        private bool IsIdentical(object left, object right)
        {
            // None comparison - most common "is" usage in Python
            if (left == null && right == null) return true;
            if (left == null || right == null) return false;

            // Same reference check
            if (ReferenceEquals(left, right)) return true;

            // Python-like interning simulation for small integers and some strings
            if (left is int li && right is int ri)
            {
                // Small integers (-5 to 256) are interned in Python
                if (li == ri && li >= -5 && li <= 256)
                    return true;
            }

            // Empty tuples are singletons
            if (left is PythonTuple lt && right is PythonTuple rt)
            {
                if (lt.Items.Count == 0 && rt.Items.Count == 0)
                    return true;
            }

            // Boolean values are singletons
            if (left is bool lb && right is bool rb)
                return lb == rb;

            // Small strings are often interned (simplified to length 1)
            if (left is string ls && right is string rs)
            {
                if (ls.Length <= 1 && rs.Length <= 1)
                    return ls == rs;
            }

            return false;
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

        private string GetTypeName(object obj)
        {
            return obj switch
            {
                null => "NoneType",
                bool => "bool",
                int => "int",
                double => "float",
                string => "str",
                PythonList => "list",
                PythonTuple => "tuple",
                PythonDict => "dict",
                _ => obj.GetType().Name
            };
        }
    }

    // Enhanced Unary Operator Node with Line/Column Tracking
    public class UnaryOpNode : ASTNode
    {
        public string Operator { get; }
        public ASTNode Operand { get; }

        public UnaryOpNode(string op, ASTNode operand, int line = 0, int column = 0) : base(line, column)
        {
            Operator = op;
            Operand = operand;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var value = Operand.Evaluate(env);
                return Operator switch
                {
                    "-" => NumberHelper.IsNumber(value) ? NumberHelper.Negate(value) : 
                           throw CreateException("TypeError", "Cannot negate non-number"),
                    "not" => !IsTrue(value),
                    _ => throw CreateException("TypeError", $"Unknown unary operator: {Operator}")
                };
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in unary operation '{Operator}': {ex.Message}");
            }
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

    // Enhanced Assignment Nodes with Line/Column Tracking
    public class AssignmentNode : ASTNode
    {
        public string VariableName { get; }
        public ASTNode Value { get; }

        public AssignmentNode(string name, ASTNode value, int line = 0, int column = 0) : base(line, column)
        {
            VariableName = name;
            Value = value;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var value = Value.Evaluate(env);
                env.SetVariable(VariableName, value);
                return value;
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in assignment to '{VariableName}': {ex.Message}");
            }
        }
    }

    public class MultipleAssignmentNode : ASTNode
    {
        public List<string> VariableNames { get; }
        public ASTNode Value { get; }

        public MultipleAssignmentNode(List<string> names, ASTNode value, int line = 0, int column = 0) : base(line, column)
        {
            VariableNames = names;
            Value = value;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var value = Value.Evaluate(env);

                // Convert value to iterable
                List<object> items;
                if (value is PythonList list)
                    items = list.Items;
                else if (value is PythonTuple tuple)
                    items = tuple.Items;
                else if (value is string str)
                    items = str.Select(c => c.ToString()).Cast<object>().ToList();
                else
                    throw CreateException("TypeError", "Cannot unpack non-iterable object");

                // Handle underscore (_) - variables to ignore
                var validNames = new List<string>();
                var validIndices = new List<int>();

                for (int i = 0; i < VariableNames.Count; i++)
                {
                    if (VariableNames[i] != "_")
                    {
                        validNames.Add(VariableNames[i]);
                        validIndices.Add(i);
                    }
                }

                // Length validation (excluding underscores)
                if (items.Count != VariableNames.Count)
                    throw CreateException("ValueError", $"Cannot unpack {items.Count} values into {VariableNames.Count} variables");

                // Assign values to variables (skip underscores)
                for (int i = 0; i < validNames.Count; i++)
                {
                    int actualIndex = validIndices[i];
                    env.SetVariable(validNames[i], items[actualIndex]);
                }

                return value;
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in multiple assignment: {ex.Message}");
            }
        }
    }

    public class IndexAssignmentNode : ASTNode
    {
        public ASTNode Object { get; }
        public ASTNode Index { get; }
        public ASTNode Value { get; }

        public IndexAssignmentNode(ASTNode obj, ASTNode index, ASTNode value, int line = 0, int column = 0) : base(line, column)
        {
            Object = obj;
            Index = index;
            Value = value;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var obj = Object.Evaluate(env);
                var index = Index.Evaluate(env);
                var value = Value.Evaluate(env);

                if (obj is PythonList list && NumberHelper.IsNumber(index))
                {
                    int i = NumberHelper.ToInt(index);
                    if (i < 0) i += list.Items.Count;
                    if (i >= 0 && i < list.Items.Count)
                    {
                        list.Items[i] = value;
                        return value;
                    }
                    throw CreateException("IndexError", "list assignment index out of range");
                }
                else if (obj is PythonDict dict)
                {
                    dict.Items[index] = value;
                    return value;
                }

                throw CreateException("TypeError", $"'{obj?.GetType()}' object does not support item assignment");
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in index assignment: {ex.Message}");
            }
        }
    }

    public class AttributeAssignmentNode : ASTNode
    {
        public ASTNode Object { get; }
        public string Attribute { get; }
        public ASTNode Value { get; }

        public AttributeAssignmentNode(ASTNode obj, string attribute, ASTNode value, int line = 0, int column = 0) : base(line, column)
        {
            Object = obj;
            Attribute = attribute;
            Value = value;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var obj = Object.Evaluate(env);
                var value = Value.Evaluate(env);

                if (obj is PythonInstance instance)
                {
                    instance.SetAttribute(Attribute, value);
                    return value;
                }

                throw CreateException("AttributeError", $"'{obj?.GetType()}' object has no attribute '{Attribute}'");
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in attribute assignment: {ex.Message}");
            }
        }
    }
}