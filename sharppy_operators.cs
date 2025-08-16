// enhanced_operators.cs
using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    // Optimized Binary Operator Node
    public sealed class BinaryOpNode : ASTNode
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
                    "==" => PythonBool.Create(IsEqual(leftVal, rightVal)),
                    "!=" => PythonBool.Create(!IsEqual(leftVal, rightVal)),
                    "<" => PythonBool.Create(IsLess(leftVal, rightVal)),
                    ">" => PythonBool.Create(IsGreater(leftVal, rightVal)),
                    "<=" => PythonBool.Create(IsLessOrEqual(leftVal, rightVal)),
                    ">=" => PythonBool.Create(IsGreaterOrEqual(leftVal, rightVal)),
                    "and" => PythonBool.Create(leftVal.IsTrue() && rightVal.IsTrue()),
                    "or" => PythonBool.Create(leftVal.IsTrue() || rightVal.IsTrue()),
                    "in" => PythonBool.Create(IsIn(leftVal, rightVal)),
                    "is" => PythonBool.Create(IsIdentical(leftVal, rightVal)),
                    "is not" => PythonBool.Create(!IsIdentical(leftVal, rightVal)),
                    _ => throw CreateException("TypeError", $"Unknown operator: {Operator}")
                };
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in binary operation '{Operator}': {ex.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PythonTypeObject Add(PythonTypeObject left, PythonTypeObject right) => left switch
        {
            PythonInt li => li.Add(right),
            PythonFloat lf => lf.Add(right),
            PythonString ls => ls.Add(right),
            PythonList ll when right is PythonList rl => ConcatLists(ll, rl),
            PythonTuple lt when right is PythonTuple rt => ConcatTuples(lt, rt),
            PythonDict ld when right is PythonDict rd => ConcatDicts(ld, rd),
            _ => throw CreateException("TypeError", $"Cannot add {left.Type} and {right.Type}")
        };

        private static PythonList ConcatLists(PythonList left, PythonList right)
        {
            var newList = new PythonList();
            newList.Items.Capacity = left.Items.Count + right.Items.Count;
            newList.Items.AddRange(left.Items);
            newList.Items.AddRange(right.Items);
            return newList;
        }

        private static PythonTuple ConcatTuples(PythonTuple left, PythonTuple right)
        {
            var newTuple = new PythonTuple();
            newTuple.Items.Capacity = left.Items.Count + right.Items.Count;
            newTuple.Items.AddRange(left.Items);
            newTuple.Items.AddRange(right.Items);
            return newTuple;
        }

        private static PythonDict ConcatDicts(PythonDict left, PythonDict right)
        {
            var newDict = new PythonDict();
            newDict.Items.EnsureCapacity(left.Items.Count + right.Items.Count);
            foreach (var kvp in left.Items) newDict.Items[kvp.Key] = kvp.Value;
            foreach (var kvp in right.Items) newDict.Items[kvp.Key] = kvp.Value;
            return newDict;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PythonTypeObject Subtract(PythonTypeObject left, PythonTypeObject right) => left switch
        {
            PythonInt li => li.Subtract(right),
            PythonFloat lf => lf.Subtract(right),
            _ => throw CreateException("TypeError", $"Cannot subtract {right.Type} from {left.Type}")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PythonTypeObject Multiply(PythonTypeObject left, PythonTypeObject right) => left switch
        {
            PythonInt li => li.Multiply(right),
            PythonFloat lf => lf.Multiply(right),
            PythonString ls when NumberHelper.IsNumber(right) => ls.Repeat(NumberHelper.ToInt(right)),
            PythonList ll when NumberHelper.IsNumber(right) => ll.Repeat(NumberHelper.ToInt(right)),
            PythonTuple lt when NumberHelper.IsNumber(right) => lt.Repeat(NumberHelper.ToInt(right)),
            _ => throw CreateException("TypeError", $"Cannot multiply {left.Type} and {right.Type}")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PythonTypeObject Divide(PythonTypeObject left, PythonTypeObject right) => left switch
        {
            PythonInt li => li.Divide(right),
            PythonFloat lf => lf.Divide(right),
            _ => throw CreateException("TypeError", $"Cannot divide {left.Type} by {right.Type}")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PythonTypeObject Modulo(PythonTypeObject left, PythonTypeObject right) => left switch
        {
            PythonInt li => li.Modulo(right),
            PythonFloat lf => lf.Modulo(right),
            _ => throw CreateException("TypeError", $"Cannot modulo {left.Type} by {right.Type}")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PythonTypeObject Power(PythonTypeObject left, PythonTypeObject right) => left switch
        {
            PythonInt li => li.Power(right),
            PythonFloat lf => lf.Power(right),
            _ => throw CreateException("TypeError", $"Cannot raise {left.Type} to power of {right.Type}")
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEqual(PythonTypeObject left, PythonTypeObject right) => left.Equals(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsLess(PythonTypeObject left, PythonTypeObject right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.ToDouble(left) < NumberHelper.ToDouble(right);
            if (left is PythonString ls && right is PythonString rs) 
                return string.Compare(ls.Value, rs.Value) < 0;
            
            throw CreateException("TypeError", $"'<' not supported between instances of '{left.Type}' and '{right.Type}'");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsGreater(PythonTypeObject left, PythonTypeObject right)
        {
            if (NumberHelper.IsNumber(left) && NumberHelper.IsNumber(right))
                return NumberHelper.ToDouble(left) > NumberHelper.ToDouble(right);
            if (left is PythonString ls && right is PythonString rs) 
                return string.Compare(ls.Value, rs.Value) > 0;
            
            throw CreateException("TypeError", $"'>' not supported between instances of '{left.Type}' and '{right.Type}'");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsLessOrEqual(PythonTypeObject left, PythonTypeObject right)
        {
            try { return IsEqual(left, right) || IsLess(left, right); }
            catch { throw CreateException("TypeError", $"'<=' not supported between instances of '{left.Type}' and '{right.Type}'"); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsGreaterOrEqual(PythonTypeObject left, PythonTypeObject right)
        {
            try { return IsEqual(left, right) || IsGreater(left, right); }
            catch { throw CreateException("TypeError", $"'>=' not supported between instances of '{left.Type}' and '{right.Type}'"); }
        }

        private bool IsIn(PythonTypeObject item, PythonTypeObject container)
        {
            switch (container)
            {
                case PythonList list:
                    return list.Items.Any(i => i.Equals(item));
                case PythonTuple tuple:
                    return tuple.Items.Any(i => i.Equals(item));
                case PythonString str when item is PythonString s:
                    return str.Contains(s);
                case PythonDict dict:
                    foreach (var key in dict.Items.Keys)
                    {
                        if (key.Equals(item))
                            return true;
                    }
                    return false;
                default:
                    throw CreateException("TypeError", $"argument of type '{container.Type}' is not iterable");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIdentical(PythonTypeObject left, PythonTypeObject right)
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

    // Optimized Unary Operator Node
    public sealed class UnaryOpNode : ASTNode
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
                    "not" => PythonBool.Create(!value.IsTrue()),
                    _ => throw CreateException("TypeError", $"Unknown unary operator: {Operator}")
                };
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in unary operation '{Operator}': {ex.Message}");
            }
        }
    }

    // Optimized Assignment Nodes
    public sealed class AssignmentNode : ASTNode
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
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in assignment to '{VariableName}': {ex.Message}");
            }
        }
    }

    public sealed class IndexAssignmentNode : ASTNode
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

                switch (obj)
                {
                    case PythonList list when NumberHelper.IsNumber(index):
                        list.SetItem(NumberHelper.ToInt(index), value);
                        return value;
                    case PythonDict dict:
                        dict.SetItem(index, value);
                        return value;
                    default:
                        throw CreateException("TypeError", $"'{obj?.Type}' object does not support item assignment");
                }
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in index assignment: {ex.Message}");
            }
        }
    }
}

// enhanced_operators.cs