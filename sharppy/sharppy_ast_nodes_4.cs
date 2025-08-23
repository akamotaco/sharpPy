// Missing AST Node classes with Evaluate implementation

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    // ============= Yield Nodes =============
    public sealed class YieldNode : ASTNode
    {
        public ASTNode Value { get; }

        public YieldNode(ASTNode value, int line = 0, int column = 0) : base(line, column)
        {
            Value = value;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                // Yield can only be used inside a generator function
                var value = Value?.Evaluate(env) ?? PythonNone.Instance;
                throw new YieldException(value);
            }
            catch (YieldException)
            {
                throw;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in yield: {ex.Message}");
            }
        }
    }

    public sealed class YieldFromNode : ASTNode
    {
        public ASTNode Value { get; }

        public YieldFromNode(ASTNode value, int line = 0, int column = 0) : base(line, column)
        {
            Value = value;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                if (Value == null)
                    throw CreateException("SyntaxError", "yield from requires an iterable");

                var iterable = Value.Evaluate(env);
                
                // Get iterable items
                List<PythonTypeObject> items = iterable switch
                {
                    PythonList list => list.Items,
                    PythonTuple tuple => tuple.Items,
                    PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
                    PythonDict dict => dict.Items.Keys.ToList(),
                    GeneratorObject gen => gen.GetAllValues(),
                    _ => throw CreateException("TypeError", $"'{iterable?.Type}' object is not iterable")
                };

                // Yield each item
                foreach (var item in items)
                {
                    throw new YieldException(item);
                }

                return PythonNone.Instance;
            }
            catch (YieldException)
            {
                throw;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in yield from: {ex.Message}");
            }
        }
    }

    // ============= Assert Node =============
    public sealed class AssertNode : ASTNode
    {
        public ASTNode Condition { get; }
        public ASTNode Message { get; }

        public AssertNode(ASTNode condition, ASTNode message = null, int line = 0, int column = 0) : base(line, column)
        {
            Condition = condition;
            Message = message;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var conditionResult = Condition.Evaluate(env);
                
                if (!conditionResult.IsTrue())
                {
                    string errorMessage = "assertion failed";
                    
                    if (Message != null)
                    {
                        var msgValue = Message.Evaluate(env);
                        errorMessage = msgValue.ToPythonString();
                    }
                    
                    throw CreateException("AssertionError", errorMessage);
                }
                
                return PythonNone.Instance;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in assert: {ex.Message}");
            }
        }
    }

    // ============= Match-Case Nodes =============
    public sealed class MatchNode : ASTNode
    {
        public ASTNode Subject { get; }
        public List<(ASTNode pattern, ASTNode guard, List<ASTNode> body)> Cases { get; }

        public MatchNode(ASTNode subject, List<(ASTNode, ASTNode, List<ASTNode>)> cases, int line = 0, int column = 0) : base(line, column)
        {
            Subject = subject;
            Cases = cases;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var subjectValue = Subject.Evaluate(env);
                
                foreach (var (pattern, guard, body) in Cases)
                {
                    // Create a new scope for pattern variables
                    var matchEnv = new Environment(env);
                    
                    if (MatchesPattern(subjectValue, pattern, matchEnv))
                    {
                        // Check guard if present
                        if (guard != null)
                        {
                            var guardResult = guard.Evaluate(matchEnv);
                            if (!guardResult.IsTrue())
                                continue;
                        }
                        
                        // Execute body
                        PythonTypeObject result = PythonNone.Instance;
                        foreach (var stmt in body)
                        {
                            result = stmt.Evaluate(matchEnv);
                        }
                        
                        // Copy captured variables back to parent environment
                        foreach (var kvp in matchEnv.GetAllVariables())
                        {
                            if (!env.HasVariable(kvp.Key))
                                env.SetVariable(kvp.Key, kvp.Value);
                        }
                        
                        return result;
                    }
                }
                
                return PythonNone.Instance;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex) when (!(ex is ReturnException || ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Internal error in match statement: {ex.Message}");
            }
        }

        private bool MatchesPattern(PythonTypeObject value, ASTNode pattern, Environment env)
        {
            switch (pattern)
            {
                case WildcardPatternNode _:
                    return true;
                    
                case VariableNode varNode:
                    env.SetVariable(varNode.Name, value);
                    return true;
                    
                case NumberNode numNode:
                    var numValue = numNode.Evaluate(env);
                    return value.Equals(numValue);
                    
                case StringNode strNode:
                    var strValue = strNode.Evaluate(env);
                    return value.Equals(strValue);
                    
                case BooleanNode boolNode:
                    var boolValue = boolNode.Evaluate(env);
                    return value.Equals(boolValue);
                    
                case NoneNode _:
                    return value is PythonNone;
                    
                case ListNode listNode:
                    if (!(value is PythonList listValue))
                        return false;
                    
                    if (listNode.Elements.Count != listValue.Items.Count)
                        return false;
                    
                    for (int i = 0; i < listNode.Elements.Count; i++)
                    {
                        if (!MatchesPattern(listValue.Items[i], listNode.Elements[i], env))
                            return false;
                    }
                    return true;
                    
                case TupleNode tupleNode:
                    if (!(value is PythonTuple tupleValue))
                        return false;
                    
                    if (tupleNode.Elements.Count != tupleValue.Items.Count)
                        return false;
                    
                    for (int i = 0; i < tupleNode.Elements.Count; i++)
                    {
                        if (!MatchesPattern(tupleValue.Items[i], tupleNode.Elements[i], env))
                            return false;
                    }
                    return true;
                    
                default:
                    // For other patterns, evaluate and compare
                    var patternValue = pattern.Evaluate(env);
                    return value.Equals(patternValue);
            }
        }
    }

    public sealed class WildcardPatternNode : ASTNode
    {
        public WildcardPatternNode(int line = 0, int column = 0) : base(line, column)
        {
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            // Wildcard pattern doesn't evaluate to a value
            return PythonNone.Instance;
        }
    }

    // ============= Comprehension Nodes =============
    public sealed class DictComprehensionNode : ASTNode
    {
        public ASTNode Key { get; }
        public ASTNode Value { get; }
        public string Variable { get; }
        public ASTNode Iterable { get; }
        public ASTNode Condition { get; }

        public DictComprehensionNode(ASTNode key, ASTNode value, string variable, ASTNode iterable, ASTNode condition = null, int line = 0, int column = 0) 
            : base(line, column)
        {
            Key = key;
            Value = value;
            Variable = variable;
            Iterable = iterable;
            Condition = condition;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var result = new PythonDict();
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
                    
                    // Evaluate key and value, add to result
                    var keyValue = Key.Evaluate(comprehensionEnv);
                    var valueValue = Value.Evaluate(comprehensionEnv);
                    result.Items[keyValue] = valueValue;
                }

                return result;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Error in dict comprehension: {ex.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private List<PythonTypeObject> GetIterableItems(PythonTypeObject obj) => obj switch
        {
            PythonList list => list.Items,
            PythonTuple tuple => tuple.Items,
            PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
            PythonDict dict => dict.Items.Keys.ToList(),
            _ => throw CreateException("TypeError", $"'{obj?.Type}' object is not iterable")
        };
    }

    public sealed class SetComprehensionNode : ASTNode
    {
        public ASTNode Expression { get; }
        public string Variable { get; }
        public ASTNode Iterable { get; }
        public ASTNode Condition { get; }

        public SetComprehensionNode(ASTNode expression, string variable, ASTNode iterable, ASTNode condition = null, int line = 0, int column = 0) 
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
                var result = new PythonSet();
                var iterableObj = Iterable.Evaluate(env);
                var items = GetIterableItems(iterableObj);

                var comprehensionEnv = new Environment(env);

                foreach (var item in items)
                {
                    comprehensionEnv.SetVariable(Variable, item);
                    
                    if (Condition != null)
                    {
                        var conditionResult = Condition.Evaluate(comprehensionEnv);
                        if (!conditionResult.IsTrue())
                            continue;
                    }
                    
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
                throw CreateException("RuntimeError", $"Error in set comprehension: {ex.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private List<PythonTypeObject> GetIterableItems(PythonTypeObject obj) => obj switch
        {
            PythonList list => list.Items,
            PythonTuple tuple => tuple.Items,
            PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
            PythonDict dict => dict.Items.Keys.ToList(),
            _ => throw CreateException("TypeError", $"'{obj?.Type}' object is not iterable")
        };
    }

    public sealed class GeneratorExpressionNode : ASTNode
    {
        public ASTNode Expression { get; }
        public string Variable { get; }
        public ASTNode Iterable { get; }
        public ASTNode Condition { get; }

        public GeneratorExpressionNode(ASTNode expression, string variable, ASTNode iterable, ASTNode condition = null, int line = 0, int column = 0) 
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
                return new GeneratorObject(Expression, Variable, Iterable, Condition, env);
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Error creating generator expression: {ex.Message}");
            }
        }
    }

    public sealed class SetNode : ASTNode
    {
        public List<ASTNode> Elements { get; }

        public SetNode(List<ASTNode> elements, int line = 0, int column = 0) : base(line, column)
        {
            Elements = elements;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var set = new PythonSet();
                
                foreach (var element in Elements)
                {
                    var value = element.Evaluate(env);
                    set.Items.Add(value);
                }
                
                return set;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error creating set: {ex.Message}");
            }
        }
    }

    // ============= Async Nodes =============
    public sealed class AsyncFunctionDefNode : ASTNode
    {
        public string Name { get; }
        public List<Parameter> Parameters { get; }
        public List<ASTNode> Body { get; }
        public TypeHint ReturnTypeHint { get; }

        public AsyncFunctionDefNode(string name, List<Parameter> parameters, List<ASTNode> body, TypeHint returnTypeHint = null, int line = 0, int column = 0) 
            : base(line, column)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
            ReturnTypeHint = returnTypeHint;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var function = new AsyncFunction(Name, Parameters, Body, env, ReturnTypeHint);
                env.SetVariable(Name, function);
                return function;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error defining async function '{Name}': {ex.Message}");
            }
        }
    }

    public sealed class AsyncForNode : ASTNode
    {
        public string Variable { get; }
        public ASTNode Iterable { get; }
        public List<ASTNode> Body { get; }

        public AsyncForNode(string variable, ASTNode iterable, List<ASTNode> body, int line = 0, int column = 0) : base(line, column)
        {
            Variable = variable;
            Iterable = iterable;
            Body = body;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var iterableObj = Iterable.Evaluate(env);
                
                // Check if iterable supports async iteration
                if (!(iterableObj is AsyncIterableObject asyncIterable))
                {
                    throw CreateException("TypeError", $"'{iterableObj?.Type}' object is not an async iterable");
                }

                PythonTypeObject result = PythonNone.Instance;

                try
                {
                    var items = asyncIterable.GetAsyncItems();
                    
                    foreach (var item in items)
                    {
                        env.SetVariable(Variable, item);
                        try
                        {
                            foreach (var stmt in Body)
                                result = stmt.Evaluate(env);
                        }
                        catch (ContinueException)
                        {
                            continue;
                        }
                    }
                }
                catch (BreakException)
                {
                    // Break out of loop
                }

                return result;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (ReturnException)
            {
                throw;
            }
            catch (Exception ex) when (!(ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Internal error in async for loop: {ex.Message}");
            }
        }
    }

    public sealed class AsyncMultiForNode : ASTNode
    {
        public List<string> Variables { get; }
        public ASTNode Iterable { get; }
        public List<ASTNode> Body { get; }

        public AsyncMultiForNode(List<string> variables, ASTNode iterable, List<ASTNode> body, int line = 0, int column = 0) : base(line, column)
        {
            Variables = variables;
            Iterable = iterable;
            Body = body;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var iterableObj = Iterable.Evaluate(env);
                
                if (!(iterableObj is AsyncIterableObject asyncIterable))
                {
                    throw CreateException("TypeError", $"'{iterableObj?.Type}' object is not an async iterable");
                }

                PythonTypeObject result = PythonNone.Instance;

                try
                {
                    var items = asyncIterable.GetAsyncItems();
                    
                    foreach (var item in items)
                    {
                        // Unpack the item into multiple variables
                        List<PythonTypeObject> values = item switch
                        {
                            PythonTuple tuple => tuple.Items,
                            PythonList list => list.Items,
                            _ => throw CreateException("ValueError", $"Cannot unpack non-sequence {item?.Type}")
                        };

                        if (values.Count != Variables.Count)
                            throw CreateException("ValueError", $"Cannot unpack {values.Count} values into {Variables.Count} variables");

                        for (int i = 0; i < Variables.Count; i++)
                        {
                            env.SetVariable(Variables[i], values[i]);
                        }

                        try
                        {
                            foreach (var stmt in Body)
                                result = stmt.Evaluate(env);
                        }
                        catch (ContinueException)
                        {
                            continue;
                        }
                    }
                }
                catch (BreakException)
                {
                    // Break out of loop
                }

                return result;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (ReturnException)
            {
                throw;
            }
            catch (Exception ex) when (!(ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Internal error in async for loop with multiple variables: {ex.Message}");
            }
        }
    }

    public sealed class AsyncWithNode : ASTNode
    {
        public ASTNode ContextExpression { get; }
        public string Variable { get; }
        public List<ASTNode> Body { get; }

        public AsyncWithNode(ASTNode contextExpression, string variable, List<ASTNode> body, int line = 0, int column = 0) : base(line, column)
        {
            ContextExpression = contextExpression;
            Variable = variable;
            Body = body;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var contextManager = ContextExpression.Evaluate(env);
                
                // Call __aenter__ method
                PythonTypeObject enterResult = PythonNone.Instance;
                
                if (contextManager is PythonInstance instance)
                {
                    try
                    {
                        var aenterMethod = instance.GetAttribute("__aenter__");
                        if (aenterMethod is Function aenterFunc)
                        {
                            enterResult = aenterFunc.Call(new List<PythonTypeObject>());
                        }
                        else
                        {
                            throw CreateException("AttributeError", "Async context manager missing __aenter__ method");
                        }
                    }
                    catch (PythonException ex) when (ex.Type == "AttributeError")
                    {
                        throw CreateException("AttributeError", "Async context manager missing __aenter__ method");
                    }
                }
                else
                {
                    throw CreateException("TypeError", "Object does not support async context management protocol");
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
                
                // Call __aexit__ method
                if (contextManager is PythonInstance inst)
                {
                    try
                    {
                        var aexitMethod = inst.GetAttribute("__aexit__");
                        if (aexitMethod is Function aexitFunc)
                        {
                            var args = new List<PythonTypeObject>(3);
                            if (caughtException != null)
                            {
                                args.Add(new PythonString(caughtException.GetType().Name));
                                args.Add(new PythonString(caughtException.Message));
                                args.Add(PythonNone.Instance);
                            }
                            else
                            {
                                args.Add(PythonNone.Instance);
                                args.Add(PythonNone.Instance);
                                args.Add(PythonNone.Instance);
                            }
                            
                            var suppressException = aexitFunc.Call(args);
                            
                            // If __aexit__ returns True, suppress the exception
                            if (caughtException != null && !suppressException.IsTrue())
                            {
                                throw caughtException;
                            }
                        }
                    }
                    catch (PythonException ex) when (ex.Type == "AttributeError")
                    {
                        if (caughtException != null) throw caughtException;
                    }
                }
                
                return result;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex) when (!(ex is ReturnException || ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Error in async with statement: {ex.Message}");
            }
        }
    }

    public sealed class AwaitNode : ASTNode
    {
        public ASTNode Expression { get; }

        public AwaitNode(ASTNode expression, int line = 0, int column = 0) : base(line, column)
        {
            Expression = expression;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var awaitable = Expression.Evaluate(env);
                
                // Check if object is awaitable
                if (awaitable is AwaitableObject awaitableObj)
                {
                    return awaitableObj.GetResult();
                }
                
                // Check for __await__ method
                if (awaitable is PythonInstance instance)
                {
                    try
                    {
                        var awaitMethod = instance.GetAttribute("__await__");
                        if (awaitMethod is Function awaitFunc)
                        {
                            return awaitFunc.Call(new List<PythonTypeObject>());
                        }
                    }
                    catch (PythonException ex) when (ex.Type == "AttributeError")
                    {
                        // Fall through to error
                    }
                }
                
                throw CreateException("TypeError", $"object {awaitable?.Type} can't be used in 'await' expression");
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in await expression: {ex.Message}");
            }
        }
    }

    // ============= Decorator Nodes =============
    public sealed class DecoratedFunctionNode : ASTNode
    {
        public FunctionDefNode Function { get; }
        public List<ASTNode> Decorators { get; }

        public DecoratedFunctionNode(FunctionDefNode function, List<ASTNode> decorators, int line = 0, int column = 0) : base(line, column)
        {
            Function = function;
            Decorators = decorators;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                // First evaluate the function
                var func = Function.Evaluate(env);

                // Apply decorators in reverse order (bottom to top)
                for (int i = Decorators.Count - 1; i >= 0; i--)
                {
                    var decorator = Decorators[i].Evaluate(env);

                    // Special handling for built-in decorators
                    if (decorator is BuiltinFunction builtinFunc)
                    {
                        // staticmethod, classmethod 등의 내장 데코레이터
                        func = builtinFunc.Call(new List<PythonTypeObject> { func });
                    }
                    else if (decorator is Function decoratorFunc)
                    {
                        // 일반 함수 데코레이터
                        func = decoratorFunc.Call(new List<PythonTypeObject> { func });
                    }
                    else if (decorator is PythonClass decoratorClass)
                    {
                        // 클래스 데코레이터 (함수를 감싸는 클래스)
                        func = decoratorClass.CreateInstance(new List<PythonTypeObject> { func });
                    }
                    else
                    {
                        throw CreateException("TypeError", $"'{decorator?.Type}' object is not callable");
                    }
                }

                // Set the decorated function in the environment
                env.SetVariable(Function.Name, func);
                return func;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error applying decorators to function '{Function.Name}': {ex.Message}");
            }
        }
    }

    public sealed class DecoratedClassNode : ASTNode
    {
        public ClassDefNode Class { get; }
        public List<ASTNode> Decorators { get; }

        public DecoratedClassNode(ClassDefNode classNode, List<ASTNode> decorators, int line = 0, int column = 0) : base(line, column)
        {
            Class = classNode;
            Decorators = decorators;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                // First evaluate the class
                var cls = Class.Evaluate(env);
                
                // Apply decorators in reverse order (bottom to top)
                for (int i = Decorators.Count - 1; i >= 0; i--)
                {
                    var decorator = Decorators[i].Evaluate(env);
                    
                    // Call decorator with class as argument
                    if (decorator is Function decoratorFunc)
                    {
                        cls = decoratorFunc.Call(new List<PythonTypeObject> { cls });
                    }
                    else
                    {
                        throw CreateException("TypeError", $"'{decorator?.Type}' object is not callable");
                    }
                }
                
                // Set the decorated class in the environment
                env.SetVariable(Class.Name, cls);
                return cls;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error applying decorators to class '{Class.Name}': {ex.Message}");
            }
        }
    }

    public sealed class DecoratedAsyncFunctionNode : ASTNode
    {
        public AsyncFunctionDefNode Function { get; }
        public List<ASTNode> Decorators { get; }

        public DecoratedAsyncFunctionNode(AsyncFunctionDefNode function, List<ASTNode> decorators, int line = 0, int column = 0) : base(line, column)
        {
            Function = function;
            Decorators = decorators;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                // First evaluate the async function
                var func = Function.Evaluate(env);
                
                // Apply decorators in reverse order (bottom to top)
                for (int i = Decorators.Count - 1; i >= 0; i--)
                {
                    var decorator = Decorators[i].Evaluate(env);
                    
                    // Call decorator with function as argument
                    if (decorator is Function decoratorFunc)
                    {
                        func = decoratorFunc.Call(new List<PythonTypeObject> { func });
                    }
                    else
                    {
                        throw CreateException("TypeError", $"'{decorator?.Type}' object is not callable");
                    }
                }
                
                // Set the decorated function in the environment
                env.SetVariable(Function.Name, func);
                return func;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error applying decorators to async function '{Function.Name}': {ex.Message}");
            }
        }
    }

    // ============= Walrus Operator Node =============
    public sealed class WalrusNode : ASTNode
    {
        public string Name { get; }
        public ASTNode Value { get; }

        public WalrusNode(string name, ASTNode value, int line = 0, int column = 0) : base(line, column)
        {
            Name = name;
            Value = value;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                // Evaluate the value
                var result = Value.Evaluate(env);
                
                // Assign to variable
                env.SetVariable(Name, result);
                
                // Return the value (walrus operator returns the assigned value)
                return result;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in walrus operator: {ex.Message}");
            }
        }
    }

    // ============= Supporting Classes =============
    
    // Exception for yield statements
    public class YieldException : Exception
    {
        public PythonTypeObject Value { get; }
        
        public YieldException(PythonTypeObject value) : base("Yield")
        {
            Value = value;
        }
    }

    // Generator object for generator expressions
    public class GeneratorObject : PythonTypeObject
    {
        private readonly ASTNode expression;
        private readonly string variable;
        private readonly ASTNode iterable;
        private readonly ASTNode condition;
        private readonly Environment env;
        private List<PythonTypeObject> items;
        private int currentIndex = 0;

        public GeneratorObject(ASTNode expression, string variable, ASTNode iterable, ASTNode condition, Environment env)
        {
            this.expression = expression;
            this.variable = variable;
            this.iterable = iterable;
            this.condition = condition;
            this.env = env;
        }

        public override PythonType Type => PythonType.Instance;
        public override bool IsTrue() => true;
        public override string ToPythonString() => "<generator object>";
        public override object GetRawValue() => this;
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);

        public PythonTypeObject GetNext()
        {
            if (items == null)
            {
                Initialize();
            }

            if (currentIndex >= items.Count)
            {
                throw new PythonException("StopIteration", "");
            }

            return items[currentIndex++];
        }

        public List<PythonTypeObject> GetAllValues()
        {
            if (items == null)
            {
                Initialize();
            }
            return new List<PythonTypeObject>(items);
        }

        private void Initialize()
        {
            items = new List<PythonTypeObject>();
            var iterableObj = iterable.Evaluate(env);
            var iterableItems = GetIterableItems(iterableObj);
            
            var genEnv = new Environment(env);
            
            foreach (var item in iterableItems)
            {
                genEnv.SetVariable(variable, item);
                
                if (condition != null)
                {
                    var condResult = condition.Evaluate(genEnv);
                    if (!condResult.IsTrue())
                        continue;
                }
                
                var value = expression.Evaluate(genEnv);
                items.Add(value);
            }
        }

        private List<PythonTypeObject> GetIterableItems(PythonTypeObject obj) => obj switch
        {
            PythonList list => list.Items,
            PythonTuple tuple => tuple.Items,
            PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
            PythonDict dict => dict.Items.Keys.ToList(),
            _ => throw new PythonException("TypeError", $"'{obj?.Type}' object is not iterable")
        };
    }

    // Python Set type
    // Python Set type - 수정된 버전
    public class PythonSet : PythonTypeObject
    {
        public HashSet<PythonTypeObject> Items { get; }

        public PythonSet()
        {
            Items = new HashSet<PythonTypeObject>();
        }

        public override PythonType Type => PythonType.Instance;

        public override bool IsTrue() => Items.Count > 0;

        public override string ToPythonString()
        {
            if (Items.Count == 0)
                return "set()";

            // 각 아이템의 repr 스타일 표현을 위해
            var itemStrings = Items.Select(item =>
            {
                // 문자열인 경우 따옴표를 추가해서 표시
                if (item is PythonString str)
                {
                    // 문자열을 따옴표로 감싸서 repr 스타일로 표시
                    return "'" + str.Value.Replace("'", "\\'") + "'";
                }
                // 다른 타입들은 그대로 ToPythonString() 사용
                return item.ToPythonString();
            });

            return "{" + string.Join(", ", itemStrings) + "}";
        }

        public override object GetRawValue() => Items;

        public override bool Equals(PythonTypeObject other)
        {
            if (other is PythonSet otherSet)
            {
                return Items.SetEquals(otherSet.Items);
            }
            return false;
        }

        // Set 관련 메서드들 추가
        public void Add(PythonTypeObject item)
        {
            Items.Add(item);
        }

        public void Remove(PythonTypeObject item)
        {
            Items.Remove(item);
        }

        public bool Contains(PythonTypeObject item)
        {
            return Items.Contains(item);
        }

        public void Clear()
        {
            Items.Clear();
        }

        public PythonInt GetLength()
        {
            return PythonInt.Create(Items.Count);
        }

        // Set operations
        public PythonSet Union(PythonSet other)
        {
            var result = new PythonSet();
            foreach (var item in Items)
                result.Items.Add(item);
            foreach (var item in other.Items)
                result.Items.Add(item);
            return result;
        }

        public PythonSet Intersection(PythonSet other)
        {
            var result = new PythonSet();
            foreach (var item in Items)
            {
                if (other.Items.Contains(item))
                    result.Items.Add(item);
            }
            return result;
        }

        public PythonSet Difference(PythonSet other)
        {
            var result = new PythonSet();
            foreach (var item in Items)
            {
                if (!other.Items.Contains(item))
                    result.Items.Add(item);
            }
            return result;
        }

        public override List<string> GetMethodNames()
        {
            return new List<string>
        {
            "add", "remove", "discard", "pop", "clear",
            "union", "intersection", "difference", "symmetric_difference",
            "update", "intersection_update", "difference_update", "symmetric_difference_update",
            "issubset", "issuperset", "isdisjoint",
            "copy"
        };
        }

        public override BuiltinFunction GetMethod(string name)
        {
            return name switch
            {
                "add" => new BuiltinFunction("add", args =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", $"add() takes exactly 1 argument ({args.Count} given)");
                    Add(args[0]);
                    return PythonNone.Instance;
                }),

                "remove" => new BuiltinFunction("remove", args =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", $"remove() takes exactly 1 argument ({args.Count} given)");
                    if (!Items.Remove(args[0]))
                        throw new PythonException("KeyError", args[0].ToPythonString());
                    return PythonNone.Instance;
                }),

                "discard" => new BuiltinFunction("discard", args =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", $"discard() takes exactly 1 argument ({args.Count} given)");
                    Items.Remove(args[0]);
                    return PythonNone.Instance;
                }),

                "clear" => new BuiltinFunction("clear", args =>
                {
                    Clear();
                    return PythonNone.Instance;
                }),

                "copy" => new BuiltinFunction("copy", args =>
                {
                    var newSet = new PythonSet();
                    foreach (var item in Items)
                        newSet.Items.Add(item);
                    return newSet;
                }),

                _ => null
            };
        }
    }

    // Async function type
    public class AsyncFunction : Function
    {
        public AsyncFunction(string name, List<Parameter> parameters, List<ASTNode> body, Environment closure, TypeHint returnTypeHint = null)
            : base(name)
        {
            // Store async function details
        }

        public override PythonTypeObject Call(List<PythonTypeObject> args)
        {
            // Return a coroutine object
            return new CoroutineObject(this, args);
        }
    }

    // Coroutine object for async functions
    public class CoroutineObject : PythonTypeObject
    {
        private readonly AsyncFunction function;
        private readonly List<PythonTypeObject> args;

        public CoroutineObject(AsyncFunction function, List<PythonTypeObject> args)
        {
            this.function = function;
            this.args = args;
        }

        public override PythonType Type => PythonType.Instance;
        public override bool IsTrue() => true;
        public override string ToPythonString() => $"<coroutine object {function.Name}>";
        public override object GetRawValue() => this;
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
    }

    // Awaitable object interface
    public abstract class AwaitableObject : PythonTypeObject
    {
        public abstract PythonTypeObject GetResult();
    }

    // Async iterable interface
    public abstract class AsyncIterableObject : PythonTypeObject
    {
        public abstract List<PythonTypeObject> GetAsyncItems();
    }
}