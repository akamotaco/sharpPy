// enhanced_ast_nodes_1.cs
using System;
using System.Text;
using System.Collections.Generic;

namespace SharpPy
{
    // Enhanced AST Node Base Class with Line/Column Information
    public abstract class ASTNode
    {
        public int Line { get; set; }
        public int Column { get; set; }

        protected ASTNode(int line = 0, int column = 0)
        {
            Line = line;
            Column = column;
        }

        public abstract PythonTypeObject Evaluate(Environment env);

        // Helper method to create PythonException with current node's location
        protected PythonException CreateException(string type, string message, string fileName = "<string>")
        {
            return new PythonException(type, message, Line, Column, fileName);
        }
    }

    // Expression Nodes with Line/Column Tracking
    public class NumberNode : ASTNode
    {
        public object Value { get; } // Can be int or double
        
        public NumberNode(object value, int line = 0, int column = 0) : base(line, column) 
            => Value = value;
            
        public override PythonTypeObject Evaluate(Environment env)
        {
            if (Value is int i)
                return new PythonInt(i);
            else if (Value is double d)
                return new PythonFloat(d);
            else
                throw CreateException("TypeError", $"Invalid number type: {Value?.GetType()}");
        }
    }

    public class StringNode : ASTNode
    {
        public string Value { get; }
        
        public StringNode(string value, int line = 0, int column = 0) : base(line, column) 
            => Value = value;
            
        public override PythonTypeObject Evaluate(Environment env) => new PythonString(Value);
    }

    public class BooleanNode : ASTNode
    {
        public bool Value { get; }
        
        public BooleanNode(bool value, int line = 0, int column = 0) : base(line, column) 
            => Value = value;
            
        public override PythonTypeObject Evaluate(Environment env) => new PythonBool(Value);
    }

    public class NoneNode : ASTNode
    {
        public NoneNode(int line = 0, int column = 0) : base(line, column) { }
        
        public override PythonTypeObject Evaluate(Environment env) => PythonNone.Instance;
    }

    public class VariableNode : ASTNode
    {
        public string Name { get; }
        
        public VariableNode(string name, int line = 0, int column = 0) : base(line, column) 
            => Name = name;

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                return env.GetVariable(Name);
            }
            catch (PythonException ex)
            {
                // Re-throw with current location if not already set
                if (ex.Line == 0)
                    throw CreateException(ex.Type, ex.Message);
                throw;
            }
        }
    }

    public class ListNode : ASTNode
    {
        public List<ASTNode> Elements { get; }
        
        public ListNode(List<ASTNode> elements, int line = 0, int column = 0) : base(line, column) 
            => Elements = elements;

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var list = new PythonList();
                foreach (var element in Elements)
                    list.Items.Add(element.Evaluate(env));
                return list;
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error creating list: {ex.Message}");
            }
        }
    }

    public class TupleNode : ASTNode
    {
        public List<ASTNode> Elements { get; }
        
        public TupleNode(List<ASTNode> elements, int line = 0, int column = 0) : base(line, column) 
            => Elements = elements;

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var tuple = new PythonTuple();
                foreach (var element in Elements)
                    tuple.Items.Add(element.Evaluate(env));
                return tuple;
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error creating tuple: {ex.Message}");
            }
        }
    }

    public class DictNode : ASTNode
    {
        public List<(ASTNode Key, ASTNode Value)> Pairs { get; }
        
        public DictNode(List<(ASTNode, ASTNode)> pairs, int line = 0, int column = 0) : base(line, column) 
            => Pairs = pairs;

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
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
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error creating dict: {ex.Message}");
            }
        }
    }

    public class IndexNode : ASTNode
    {
        public ASTNode Object { get; }
        public ASTNode Index { get; }

        public IndexNode(ASTNode obj, ASTNode index, int line = 0, int column = 0) : base(line, column)
        {
            Object = obj;
            Index = index;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var obj = Object.Evaluate(env);
                var index = Index.Evaluate(env);

                if (obj is PythonList list && NumberHelper.IsNumber(index))
                {
                    int i = NumberHelper.ToInt(index);
                    return list.GetItem(i);
                }
                else if (obj is PythonTuple tuple && NumberHelper.IsNumber(index))
                {
                    int i = NumberHelper.ToInt(index);
                    return tuple.GetItem(i);
                }
                else if (obj is PythonDict dict)
                {
                    return dict.GetItem(index);
                }
                else if (obj is PythonString str && NumberHelper.IsNumber(index))
                {
                    int i = NumberHelper.ToInt(index);
                    return str.GetItem(i);
                }

                throw CreateException("TypeError", $"'{obj?.Type}' object is not subscriptable");
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error during indexing: {ex.Message}");
            }
        }
    }

    public class SliceNode : ASTNode
    {
        public ASTNode Object { get; }
        public ASTNode Start { get; }
        public ASTNode Stop { get; }
        public ASTNode Step { get; }

        public SliceNode(ASTNode obj, ASTNode start = null, ASTNode stop = null, ASTNode step = null, int line = 0, int column = 0) : base(line, column)
        {
            Object = obj;
            Start = start;
            Stop = stop;
            Step = step;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var obj = Object.Evaluate(env);

                if (obj is PythonList list)
                    return SliceList(list, env);
                else if (obj is PythonTuple tuple)
                    return SliceTuple(tuple, env);
                else if (obj is PythonString str)
                    return SliceString(str, env);

                throw CreateException("TypeError", $"'{obj?.Type}' object is not subscriptable");
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error during slicing: {ex.Message}");
            }
        }

        private PythonList SliceList(PythonList list, Environment env)
        {
            int count = list.Items.Count;
            int? start = GetSliceIndex(Start?.Evaluate(env), 0, count);
            int? stop = GetSliceIndex(Stop?.Evaluate(env), count, count);
            int step = GetSliceStep(Step?.Evaluate(env));

            var result = new PythonList();

            if (step > 0)
            {
                int actualStart = start ?? 0;
                int actualStop = stop ?? count;
                for (int i = actualStart; i < actualStop; i += step)
                    if (i >= 0 && i < count)
                        result.Items.Add(list.Items[i]);
            }
            else if (step < 0)
            {
                int actualStart = start ?? count - 1;
                int actualStop = stop ?? -1;
                for (int i = actualStart; i > actualStop; i += step)
                    if (i >= 0 && i < count)
                        result.Items.Add(list.Items[i]);
            }

            return result;
        }

        private PythonTuple SliceTuple(PythonTuple tuple, Environment env)
        {
            int count = tuple.Items.Count;
            int? start = GetSliceIndex(Start?.Evaluate(env), 0, count);
            int? stop = GetSliceIndex(Stop?.Evaluate(env), count, count);
            int step = GetSliceStep(Step?.Evaluate(env));

            var result = new PythonTuple();

            if (step > 0)
            {
                int actualStart = start ?? 0;
                int actualStop = stop ?? count;
                for (int i = actualStart; i < actualStop; i += step)
                    if (i >= 0 && i < count)
                        result.Items.Add(tuple.Items[i]);
            }
            else if (step < 0)
            {
                int actualStart = start ?? count - 1;
                int actualStop = stop ?? -1;
                for (int i = actualStart; i > actualStop; i += step)
                    if (i >= 0 && i < count)
                        result.Items.Add(tuple.Items[i]);
            }

            return result;
        }

        private PythonString SliceString(PythonString str, Environment env)
        {
            return str.Slice(
                GetSliceIndex(Start?.Evaluate(env), null, str.Length),
                GetSliceIndex(Stop?.Evaluate(env), null, str.Length),
                GetSliceStep(Step?.Evaluate(env))
            );
        }

        private int? GetSliceIndex(PythonTypeObject indexObj, int? defaultValue, int count)
        {
            if (indexObj == null || indexObj is PythonNone) return defaultValue;
            if (NumberHelper.IsNumber(indexObj))
            {
                int index = NumberHelper.ToInt(indexObj);
                if (index < 0) index += count;
                return Math.Max(0, Math.Min(index, count));
            }
            return defaultValue;
        }

        private int GetSliceStep(PythonTypeObject stepObj)
        {
            if (stepObj == null || stepObj is PythonNone) return 1;
            if (NumberHelper.IsNumber(stepObj))
            {
                int step = NumberHelper.ToInt(stepObj);
                if (step == 0) throw CreateException("ValueError", "slice step cannot be zero");
                return step;
            }
            return 1;
        }
    }

    public class AttributeNode : ASTNode
    {
        public ASTNode Object { get; }
        public string Attribute { get; }

        public AttributeNode(ASTNode obj, string attribute, int line = 0, int column = 0) : base(line, column)
        {
            Object = obj;
            Attribute = attribute;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
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
                else if (obj is PythonString str)
                    return str.GetMethod(Attribute);
                else if (obj is FileObject file)
                    return file.GetMethod(Attribute);

                throw CreateException("AttributeError", $"'{obj?.Type}' object has no attribute '{Attribute}'");
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error accessing attribute: {ex.Message}");
            }
        }
    }

    public class ConditionalExpressionNode : ASTNode
    {
        public ASTNode Condition { get; }
        public ASTNode TrueValue { get; }
        public ASTNode FalseValue { get; }

        public ConditionalExpressionNode(ASTNode trueValue, ASTNode condition, ASTNode falseValue, int line = 0, int column = 0) : base(line, column)
        {
            TrueValue = trueValue;
            Condition = condition;
            FalseValue = falseValue;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var conditionResult = Condition.Evaluate(env);

                if (conditionResult.IsTrue())
                    return TrueValue.Evaluate(env);
                else
                    return FalseValue.Evaluate(env);
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in conditional expression: {ex.Message}");
            }
        }
    }

    public class LambdaNode : ASTNode
    {
        public List<Parameter> Parameters { get; }
        public ASTNode Body { get; }

        public LambdaNode(List<Parameter> parameters, ASTNode body, int line = 0, int column = 0) : base(line, column)
        {
            Parameters = parameters ?? new List<Parameter>();
            Body = body;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                return new LambdaFunction(Parameters, Body, env);
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error creating lambda: {ex.Message}");
            }
        }
    }
}