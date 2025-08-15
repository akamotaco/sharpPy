// enhanced_operators.cs (주요 부분)
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    // Enhanced Binary Operator Node with PythonTypeObject
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

        public override PythonTypeObject Evaluate(Environment env)
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
                    "==" => new PythonBool(IsEqual(leftVal, rightVal)),
                    "!=" => new PythonBool(!IsEqual(leftVal, rightVal)),
                    "<" => new PythonBool(IsLess(leftVal, rightVal)),
                    ">" => new PythonBool(IsGreater(leftVal, rightVal)),
                    "<=" => new PythonBool(IsLessOrEqual(leftVal, rightVal)),
                    ">=" => new PythonBool(IsGreaterOrEqual(leftVal, rightVal)),
                    "and" => new PythonBool(leftVal.IsTrue() && rightVal.IsTrue()),
                    "or" => new PythonBool(leftVal.IsTrue() || rightVal.IsTrue()),
                    "in" => new PythonBool(IsIn(leftVal, rightVal)),
                    "is" => new PythonBool(IsIdentical(leftVal, rightVal)),
                    "is not" => new PythonBool(!IsIdentical(leftVal, rightVal)),
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

        private PythonTypeObject Add(PythonTypeObject left, PythonTypeObject right)
        {
            // Numeric addition
            if (left is PythonInt li) return li.Add(right);
            if (left is PythonFloat lf) return lf.Add(right);
            
            // String concatenation
            if (left is PythonString ls) return ls.Add(right);
            
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
            
            throw CreateException("TypeError", $"Cannot add {left.Type} and {right.Type}");
        }

        private PythonTypeObject Subtract(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Subtract(right);
            if (left is PythonFloat lf) return lf.Subtract(right);
            
            throw CreateException("TypeError", $"Cannot subtract {right.Type} from {left.Type}");
        }

        private PythonTypeObject Multiply(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Multiply(right);
            if (left is PythonFloat lf) return lf.Multiply(right);
            if (left is PythonString ls && NumberHelper.IsNumber(right))
                return ls.Repeat(NumberHelper.ToInt(right));
            if (left is PythonList ll && NumberHelper.IsNumber(right))
                return ll.Repeat(NumberHelper.ToInt(right));
            if (left is PythonTuple lt && NumberHelper.IsNumber(right))
                return lt.Repeat(NumberHelper.ToInt(right));
            
            throw CreateException("TypeError", $"Cannot multiply {left.Type} and {right.Type}");
        }

        private PythonTypeObject Divide(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Divide(right);
            if (left is PythonFloat lf) return lf.Divide(right);
            
            throw CreateException("TypeError", $"Cannot divide {left.Type} by {right.Type}");
        }

        private PythonTypeObject Modulo(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Modulo(right);
            if (left is PythonFloat lf) return lf.Modulo(right);
            
            throw CreateException("TypeError", $"Cannot modulo {left.Type} by {right.Type}");
        }

        private PythonTypeObject Power(PythonTypeObject left, PythonTypeObject right)
        {
            if (left is PythonInt li) return li.Power(right);
            if (left is PythonFloat lf) return lf.Power(right);
            
            throw CreateException("TypeError", $"Cannot raise {left.Type} to power of {right.Type}");
        }

        private bool IsEqual(PythonTypeObject left, PythonTypeObject right) => left.Equals(right);

        private bool IsLess(PythonTypeObject left, PythonTypeObject right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.ToDouble(left) < NumberHelper.ToDouble(right);
            if (left is PythonString ls && right is PythonString rs) 
                return string.Compare(ls.Value, rs.Value) < 0;
            
            throw CreateException("TypeError", $"'<' not supported between instances of '{left.Type}' and '{right.Type}'");
        }

        private bool IsGreater(PythonTypeObject left, PythonTypeObject right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.ToDouble(left) > NumberHelper.ToDouble(right);
            if (left is PythonString ls && right is PythonString rs) 
                return string.Compare(ls.Value, rs.Value) > 0;
            
            throw CreateException("TypeError", $"'>' not supported between instances of '{left.Type}' and '{right.Type}'");
        }

        private bool IsLessOrEqual(PythonTypeObject left, PythonTypeObject right)
        {
            try { return IsEqual(left, right) || IsLess(left, right); }
            catch { throw CreateException("TypeError", $"'<=' not supported between instances of '{left.Type}' and '{right.Type}'"); }
        }

        private bool IsGreaterOrEqual(PythonTypeObject left, PythonTypeObject right)
        {
            try { return IsEqual(left, right) || IsGreater(left, right); }
            catch { throw CreateException("TypeError", $"'>=' not supported between instances of '{left.Type}' and '{right.Type}'"); }
        }

        private bool IsIn(PythonTypeObject item, PythonTypeObject container)
        {
            if (container is PythonList list) 
                return list.Items.Any(i => i.Equals(item));
            if (container is PythonTuple tuple) 
                return tuple.Items.Any(i => i.Equals(item));
            if (container is PythonString str && item is PythonString s) 
                return str.Contains(s);
            if (container is PythonDict dict)
            {
                // For dict, 'in' checks keys (Python standard behavior)
                // return dict.ContainsKey(item);
                foreach (var key in dict.Items.Keys)
                {
                    if (key.Equals(item))
                        return true;
                }
                return false;
            }
            throw CreateException("TypeError", $"argument of type '{container.Type}' is not iterable");
        }

        private bool IsIdentical(PythonTypeObject left, PythonTypeObject right)
        {
            // None comparison
            if (left is PythonNone && right is PythonNone) return true;
            
            // Same reference check
            if (ReferenceEquals(left, right)) return true;

            // Python-like interning simulation for small integers
            if (left is PythonInt li && right is PythonInt ri)
            {
                // Small integers (-5 to 256) are interned in Python
                if (li.Value == ri.Value && li.Value >= -5 && li.Value <= 256)
                    return true;
            }

            // Empty tuples are singletons
            if (left is PythonTuple lt && right is PythonTuple rt)
            {
                if (lt.Items.Count == 0 && rt.Items.Count == 0)
                    return true;
            }

            // Boolean values are singletons
            if (left is PythonBool lb && right is PythonBool rb)
                return lb.Value == rb.Value;

            return false;
        }
    }

    // Enhanced Unary Operator Node with PythonTypeObject
    public class UnaryOpNode : ASTNode
    {
        public string Operator { get; }
        public ASTNode Operand { get; }

        public UnaryOpNode(string op, ASTNode operand, int line = 0, int column = 0) : base(line, column)
        {
            Operator = op;
            Operand = operand;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var value = Operand.Evaluate(env);
                return Operator switch
                {
                    "-" => NumberHelper.IsNumber(value) ? NumberHelper.Negate(value) : 
                           throw CreateException("TypeError", "Cannot negate non-number"),
                    "not" => new PythonBool(!value.IsTrue()),
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
    }

    // Enhanced Assignment Nodes with PythonTypeObject
    public class AssignmentNode : ASTNode
    {
        public string VariableName { get; }
        public ASTNode Value { get; }

        public AssignmentNode(string name, ASTNode value, int line = 0, int column = 0) : base(line, column)
        {
            VariableName = name;
            Value = value;
        }

        public override PythonTypeObject Evaluate(Environment env)
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

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var obj = Object.Evaluate(env);
                var index = Index.Evaluate(env);
                var value = Value.Evaluate(env);

                if (obj is PythonList list && NumberHelper.IsNumber(index))
                {
                    int i = NumberHelper.ToInt(index);
                    list.SetItem(i, value);
                    return value;
                }
                else if (obj is PythonDict dict)
                {
                    dict.SetItem(index, value);
                    return value;
                }

                throw CreateException("TypeError", $"'{obj?.Type}' object does not support item assignment");
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
}