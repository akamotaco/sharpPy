// enhanced_ast_nodes_1.cs
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    // Enhanced AST Node Base Class with optimizations
    public abstract class ASTNode
    {
        public int Line { get; }
        public int Column { get; }

        protected ASTNode(int line = 0, int column = 0)
        {
            Line = line;
            Column = column;
        }

        public abstract PythonTypeObject Evaluate(Environment env);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected PythonException CreateException(string type, string message, string fileName = "<string>") =>
            new PythonException(type, message, Line, Column, fileName);
    }

    // Optimized Expression Nodes
    public sealed class NumberNode : ASTNode
    {
        public object Value { get; }
        
        public NumberNode(object value, int line = 0, int column = 0) : base(line, column) 
            => Value = value;
            
        public override PythonTypeObject Evaluate(Environment env) => Value switch
        {
            int i => PythonInt.Create(i),
            double d => new PythonFloat(d),
            _ => throw CreateException("TypeError", $"Invalid number type: {Value?.GetType()}")
        };
    }

    public sealed class StringNode : ASTNode
    {
        public string Value { get; }
        
        public StringNode(string value, int line = 0, int column = 0) : base(line, column) 
            => Value = value;
            
        public override PythonTypeObject Evaluate(Environment env) => new PythonString(Value);
    }

    public sealed class BooleanNode : ASTNode
    {
        public bool Value { get; }
        
        public BooleanNode(bool value, int line = 0, int column = 0) : base(line, column) 
            => Value = value;
            
        public override PythonTypeObject Evaluate(Environment env) => PythonBool.Create(Value);
    }

    public sealed class NoneNode : ASTNode
    {
        public NoneNode(int line = 0, int column = 0) : base(line, column) { }
        
        public override PythonTypeObject Evaluate(Environment env) => PythonNone.Instance;
    }

    public sealed class VariableNode : ASTNode
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
                if (ex.Line == 0)
                    throw CreateException(ex.Type, ex.Message);
                throw;
            }
        }
    }

    public sealed class ListNode : ASTNode
    {
        public List<ASTNode> Elements { get; }
        
        public ListNode(List<ASTNode> elements, int line = 0, int column = 0) : base(line, column) 
            => Elements = elements;

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var list = new PythonList();
                list.Items.Capacity = Elements.Count; // Pre-allocate capacity
                
                foreach (var element in Elements)
                    list.Items.Add(element.Evaluate(env));
                return list;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error creating list: {ex.Message}");
            }
        }
    }

    public sealed class TupleNode : ASTNode
    {
        public List<ASTNode> Elements { get; }
        
        public TupleNode(List<ASTNode> elements, int line = 0, int column = 0) : base(line, column) 
            => Elements = elements;

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var tuple = new PythonTuple();
                tuple.Items.Capacity = Elements.Count; // Pre-allocate capacity
                
                foreach (var element in Elements)
                    tuple.Items.Add(element.Evaluate(env));
                return tuple;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error creating tuple: {ex.Message}");
            }
        }
    }

    public sealed class DictNode : ASTNode
    {
        public List<(ASTNode Key, ASTNode Value)> Pairs { get; }
        
        public DictNode(List<(ASTNode, ASTNode)> pairs, int line = 0, int column = 0) : base(line, column) 
            => Pairs = pairs;

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var dict = new PythonDict();
                dict.Items.EnsureCapacity(Pairs.Count); // Pre-allocate capacity
                
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
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error creating dict: {ex.Message}");
            }
        }
    }

    public sealed class IndexNode : ASTNode
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

                return obj switch
                {
                    PythonList list when NumberHelper.IsNumber(index) => list.GetItem(NumberHelper.ToInt(index)),
                    PythonTuple tuple when NumberHelper.IsNumber(index) => tuple.GetItem(NumberHelper.ToInt(index)),
                    PythonDict dict => dict.GetItem(index),
                    PythonString str when NumberHelper.IsNumber(index) => str.GetItem(NumberHelper.ToInt(index)),
                    _ => throw CreateException("TypeError", $"'{obj?.Type}' object is not subscriptable")
                };
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error during indexing: {ex.Message}");
            }
        }
    }

    public sealed class SliceNode : ASTNode
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

                return obj switch
                {
                    PythonList list => SliceList(list, env),
                    PythonTuple tuple => SliceTuple(tuple, env),
                    PythonString str => SliceString(str, env),
                    _ => throw CreateException("TypeError", $"'{obj?.Type}' object is not subscriptable")
                };
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error during slicing: {ex.Message}");
            }
        }

        private PythonList SliceList(PythonList list, Environment env)
        {
            int count = list.Items.Count;
            int step = GetSliceStep(Step?.Evaluate(env));
            
            int? start, stop;
            if (step > 0)
            {
                start = GetSliceIndex(Start?.Evaluate(env), 0, count);
                stop = GetSliceIndex(Stop?.Evaluate(env), count, count);
            }
            else
            {
                start = GetSliceIndex(Start?.Evaluate(env), count - 1, count);
                stop = GetSliceIndex(Stop?.Evaluate(env), -count - 1, count);
            }

            var result = new PythonList();
            result.Items.Capacity = Math.Abs((stop ?? count) - (start ?? 0)) / Math.Abs(step) + 1;

            if (step > 0)
            {
                int actualStart = start ?? 0;
                int actualStop = stop ?? count;
                for (int i = actualStart; i < actualStop && i < count; i += step)
                    if (i >= 0)
                        result.Items.Add(list.Items[i]);
            }
            else if (step < 0)
            {
                int actualStart = start ?? count - 1;
                int actualStop = stop ?? -count - 1;
                
                for (int i = actualStart; i >= 0 && i < count; i += step)
                {
                    result.Items.Add(list.Items[i]);
                    if (i <= actualStop + count && actualStop < 0)
                        break;
                    if (actualStop >= 0 && i <= actualStop)
                        break;
                }
            }

            return result;
        }

        private PythonTuple SliceTuple(PythonTuple tuple, Environment env)
        {
            int count = tuple.Items.Count;
            int step = GetSliceStep(Step?.Evaluate(env));
            
            int? start, stop;
            if (step > 0)
            {
                start = GetSliceIndex(Start?.Evaluate(env), 0, count);
                stop = GetSliceIndex(Stop?.Evaluate(env), count, count);
            }
            else
            {
                start = GetSliceIndex(Start?.Evaluate(env), count - 1, count);
                stop = GetSliceIndex(Stop?.Evaluate(env), -count - 1, count);
            }

            var result = new PythonTuple();
            result.Items.Capacity = Math.Abs((stop ?? count) - (start ?? 0)) / Math.Abs(step) + 1;

            if (step > 0)
            {
                int actualStart = start ?? 0;
                int actualStop = stop ?? count;
                for (int i = actualStart; i < actualStop && i < count; i += step)
                    if (i >= 0)
                        result.Items.Add(tuple.Items[i]);
            }
            else if (step < 0)
            {
                int actualStart = start ?? count - 1;
                int actualStop = stop ?? -count - 1;
                
                for (int i = actualStart; i >= 0 && i < count; i += step)
                {
                    result.Items.Add(tuple.Items[i]);
                    if (i <= actualStop + count && actualStop < 0)
                        break;
                    if (actualStop >= 0 && i <= actualStop)
                        break;
                }
            }

            return result;
        }

        private PythonString SliceString(PythonString str, Environment env)
        {
            int count = str.Length;
            int step = GetSliceStep(Step?.Evaluate(env));
            
            int? start, stop;
            if (step > 0)
            {
                start = GetSliceIndex(Start?.Evaluate(env), 0, count);
                stop = GetSliceIndex(Stop?.Evaluate(env), count, count);
            }
            else
            {
                start = GetSliceIndex(Start?.Evaluate(env), count - 1, count);
                stop = GetSliceIndex(Stop?.Evaluate(env), -count - 1, count);
            }
            
            return str.Slice(start, stop, step);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int? GetSliceIndex(PythonTypeObject indexObj, int? defaultValue, int count)
        {
            if (indexObj == null || indexObj is PythonNone) return defaultValue;
            if (NumberHelper.IsNumber(indexObj))
            {
                int index = NumberHelper.ToInt(indexObj);
                if (index < 0) index += count;
                return index;
            }
            return defaultValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    public sealed class AttributeNode : ASTNode
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

                return obj switch
                {
                    PythonInstance instance => instance.GetAttribute(Attribute),
                    PythonList list => list.GetMethod(Attribute),
                    PythonTuple tuple => tuple.GetMethod(Attribute),
                    PythonDict dict => dict.GetMethod(Attribute),
                    PythonModule module => module.GetAttribute(Attribute),
                    PythonString str => str.GetMethod(Attribute),
                    FileObject file => file.GetMethod(Attribute),
                    _ => throw CreateException("AttributeError", $"'{obj?.Type}' object has no attribute '{Attribute}'")
                };
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error accessing attribute: {ex.Message}");
            }
        }
    }

    public sealed class ConditionalExpressionNode : ASTNode
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
                return conditionResult.IsTrue() ? TrueValue.Evaluate(env) : FalseValue.Evaluate(env);
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in conditional expression: {ex.Message}");
            }
        }
    }

    public sealed class LambdaNode : ASTNode
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
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error creating lambda: {ex.Message}");
            }
        }
    }
}

// enhanced_ast_nodes_1.cs