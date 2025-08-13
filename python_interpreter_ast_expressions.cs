using System.Text;

namespace PurePythonInterpreter
{
    // AST Node Base
    public abstract class ASTNode
    {
        public abstract object Evaluate(Environment env);
    }

    // Expression Nodes
    public class NumberNode : ASTNode
    {
        public double Value { get; }
        public NumberNode(double value) => Value = value;
        public override object Evaluate(Environment env) => Value;
    }

    public class StringNode : ASTNode
    {
        public string Value { get; }
        public StringNode(string value) => Value = value;
        public override object Evaluate(Environment env) => Value;
    }

    public class BooleanNode : ASTNode
    {
        public bool Value { get; }
        public BooleanNode(bool value) => Value = value;
        public override object Evaluate(Environment env) => Value;
    }

    public class NoneNode : ASTNode
    {
        public override object Evaluate(Environment env) => null;
    }

    public class VariableNode : ASTNode
    {
        public string Name { get; }
        public VariableNode(string name) => Name = name;
        public override object Evaluate(Environment env) => env.GetVariable(Name);
    }

    public class ListNode : ASTNode
    {
        public List<ASTNode> Elements { get; }
        public ListNode(List<ASTNode> elements) => Elements = elements;

        public override object Evaluate(Environment env)
        {
            var list = new PythonList();
            foreach (var element in Elements)
                list.Items.Add(element.Evaluate(env));
            return list;
        }
    }

    public class TupleNode : ASTNode
    {
        public List<ASTNode> Elements { get; }
        public TupleNode(List<ASTNode> elements) => Elements = elements;

        public override object Evaluate(Environment env)
        {
            var tuple = new PythonTuple();
            foreach (var element in Elements)
                tuple.Items.Add(element.Evaluate(env));
            return tuple;
        }
    }

    public class DictNode : ASTNode
    {
        public List<(ASTNode Key, ASTNode Value)> Pairs { get; }
        public DictNode(List<(ASTNode, ASTNode)> pairs) => Pairs = pairs;

        public override object Evaluate(Environment env)
        {
            var dict = new PythonDict();
            foreach (var (key, value) in Pairs)
            {
                var keyObj = key.Evaluate(env);
                var valueObj = value.Evaluate(env);
                dict.Items[keyObj] = valueObj;
            }
            return dict;
        }
    }

    public class IndexNode : ASTNode
    {
        public ASTNode Object { get; }
        public ASTNode Index { get; }

        public IndexNode(ASTNode obj, ASTNode index)
        {
            Object = obj;
            Index = index;
        }

        public override object Evaluate(Environment env)
        {
            var obj = Object.Evaluate(env);
            var index = Index.Evaluate(env);

            if (obj is PythonList list && index is double d)
            {
                int i = (int)d;
                if (i < 0) i += list.Items.Count;
                if (i >= 0 && i < list.Items.Count)
                    return list.Items[i];
                throw new PythonException("IndexError", "list index out of range");
            }
            else if (obj is PythonTuple tuple && index is double d3)
            {
                int i = (int)d3;
                if (i < 0) i += tuple.Items.Count;
                if (i >= 0 && i < tuple.Items.Count)
                    return tuple.Items[i];
                throw new PythonException("IndexError", "tuple index out of range");
            }
            else if (obj is PythonDict dict)
            {
                if (dict.Items.ContainsKey(index))
                    return dict.Items[index];
                throw new PythonException("KeyError", $"KeyError: {index}");
            }
            else if (obj is string str && index is double d2)
            {
                int i = (int)d2;
                if (i < 0) i += str.Length;
                if (i >= 0 && i < str.Length)
                    return str[i].ToString();
                throw new PythonException("IndexError", "string index out of range");
            }

            throw new PythonException("TypeError", $"'{obj?.GetType()}' object is not subscriptable");
        }
    }

    public class SliceNode : ASTNode
    {
        public ASTNode Object { get; }
        public ASTNode Start { get; }
        public ASTNode Stop { get; }
        public ASTNode Step { get; }

        public SliceNode(ASTNode obj, ASTNode start = null, ASTNode stop = null, ASTNode step = null)
        {
            Object = obj;
            Start = start;
            Stop = stop;
            Step = step;
        }

        public override object Evaluate(Environment env)
        {
            var obj = Object.Evaluate(env);

            if (obj is PythonList list)
                return SliceList(list, env);
            else if (obj is PythonTuple tuple)
                return SliceTuple(tuple, env);
            else if (obj is string str)
                return SliceString(str, env);

            throw new PythonException("TypeError", $"'{obj?.GetType()}' object is not subscriptable");
        }

        private PythonList SliceList(PythonList list, Environment env)
        {
            int count = list.Items.Count;
            int start = GetSliceIndex(Start?.Evaluate(env), 0, count);
            int stop = GetSliceIndex(Stop?.Evaluate(env), count, count);
            int step = GetSliceStep(Step?.Evaluate(env));

            var result = new PythonList();

            if (step > 0)
            {
                for (int i = start; i < stop; i += step)
                    if (i >= 0 && i < count)
                        result.Items.Add(list.Items[i]);
            }
            else if (step < 0)
            {
                if (Start == null) start = count - 1;
                if (Stop == null) stop = -1;
                for (int i = start; i > stop; i += step)
                    if (i >= 0 && i < count)
                        result.Items.Add(list.Items[i]);
            }

            return result;
        }

        private PythonTuple SliceTuple(PythonTuple tuple, Environment env)
        {
            int count = tuple.Items.Count;
            int start = GetSliceIndex(Start?.Evaluate(env), 0, count);
            int stop = GetSliceIndex(Stop?.Evaluate(env), count, count);
            int step = GetSliceStep(Step?.Evaluate(env));

            var result = new PythonTuple();

            if (step > 0)
            {
                for (int i = start; i < stop; i += step)
                    if (i >= 0 && i < count)
                        result.Items.Add(tuple.Items[i]);
            }
            else if (step < 0)
            {
                if (Start == null) start = count - 1;
                if (Stop == null) stop = -1;
                for (int i = start; i > stop; i += step)
                    if (i >= 0 && i < count)
                        result.Items.Add(tuple.Items[i]);
            }

            return result;
        }

        private string SliceString(string str, Environment env)
        {
            int count = str.Length;
            int start = GetSliceIndex(Start?.Evaluate(env), 0, count);
            int stop = GetSliceIndex(Stop?.Evaluate(env), count, count);
            int step = GetSliceStep(Step?.Evaluate(env));

            var result = new StringBuilder();

            if (step > 0)
            {
                for (int i = start; i < stop; i += step)
                    if (i >= 0 && i < count)
                        result.Append(str[i]);
            }
            else if (step < 0)
            {
                if (Start == null) start = count - 1;
                if (Stop == null) stop = -1;
                for (int i = start; i > stop; i += step)
                    if (i >= 0 && i < count)
                        result.Append(str[i]);
            }

            return result.ToString();
        }

        private int GetSliceIndex(object indexObj, int defaultValue, int count)
        {
            if (indexObj == null) return defaultValue;
            if (indexObj is double d)
            {
                int index = (int)d;
                if (index < 0) index += count;
                return Math.Max(0, Math.Min(index, count));
            }
            return defaultValue;
        }

        private int GetSliceStep(object stepObj)
        {
            if (stepObj == null) return 1;
            if (stepObj is double d)
            {
                int step = (int)d;
                if (step == 0) throw new PythonException("ValueError", "slice step cannot be zero");
                return step;
            }
            return 1;
        }
    }

    public class AttributeNode : ASTNode
    {
        public ASTNode Object { get; }
        public string Attribute { get; }

        public AttributeNode(ASTNode obj, string attribute)
        {
            Object = obj;
            Attribute = attribute;
        }

        public override object Evaluate(Environment env)
        {
            var obj = Object.Evaluate(env);

            if (obj is PythonInstance instance)
                return instance.GetAttribute(Attribute);
            else if (obj is PythonList list)
                return list.GetMethod(Attribute);
            else if (obj is PythonTuple tuple)
                return tuple.GetMethod(Attribute);
            else if (obj is PythonDict dict)
                return dict.GetMethod(Attribute);
            else if (obj is PythonModule module)
                return module.GetAttribute(Attribute);

            throw new PythonException("AttributeError", $"'{obj?.GetType()}' object has no attribute '{Attribute}'");
        }
    }

    public class ConditionalExpressionNode : ASTNode
    {
        public ASTNode Condition { get; }
        public ASTNode TrueValue { get; }
        public ASTNode FalseValue { get; }

        public ConditionalExpressionNode(ASTNode trueValue, ASTNode condition, ASTNode falseValue)
        {
            TrueValue = trueValue;
            Condition = condition;
            FalseValue = falseValue;
        }

        public override object Evaluate(Environment env)
        {
            var conditionResult = Condition.Evaluate(env);

            if (IsTrue(conditionResult))
                return TrueValue.Evaluate(env);
            else
                return FalseValue.Evaluate(env);
        }

        private bool IsTrue(object obj)
        {
            if (obj == null) return false;
            if (obj is bool b) return b;
            if (obj is double d) return d != 0;
            if (obj is string s) return !string.IsNullOrEmpty(s);
            if (obj is PythonList l) return l.Items.Count > 0;
            if (obj is PythonTuple t) return t.Items.Count > 0;
            if (obj is PythonDict dict) return dict.Items.Count > 0;
            return true;
        }
    }
}