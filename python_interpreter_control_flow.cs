namespace PurePythonInterpreter
{
    // Function and Class Definition Nodes
    public class FunctionDefNode : ASTNode
    {
        public string Name { get; }
        public List<Parameter> Parameters { get; }
        public List<ASTNode> Body { get; }
        public TypeHint ReturnTypeHint { get; }

        public FunctionDefNode(string name, List<Parameter> parameters, List<ASTNode> body, TypeHint returnTypeHint = null)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
            ReturnTypeHint = returnTypeHint;
        }

        public override object Evaluate(Environment env)
        {
            var function = new UserFunction(Name, Parameters, Body, env, ReturnTypeHint);
            env.SetVariable(Name, function);
            return function;
        }
    }

    public class FunctionCallNode : ASTNode
    {
        public ASTNode Function { get; }
        public List<ASTNode> Arguments { get; }

        public FunctionCallNode(ASTNode function, List<ASTNode> arguments)
        {
            Function = function;
            Arguments = arguments;
        }

        public override object Evaluate(Environment env)
        {
            var function = Function.Evaluate(env);
            var args = Arguments.Select(arg => arg.Evaluate(env)).ToList();

            return function switch
            {
                UserFunction userFunc => userFunc.Call(args),
                BuiltinFunction builtinFunc => builtinFunc.Call(args),
                BoundMethod boundMethod => boundMethod.Call(args),
                PythonClass pythonClass => pythonClass.CreateInstance(args),
                _ => throw new PythonException("TypeError", $"'{function?.GetType()?.Name ?? "null"}' object is not callable")
            };
        }
    }

    public class ClassDefNode : ASTNode
    {
        public string Name { get; }
        public string BaseClass { get; }
        public List<ASTNode> Body { get; }

        public ClassDefNode(string name, List<ASTNode> body, string baseClass = null)
        {
            Name = name;
            BaseClass = baseClass;
            Body = body;
        }

        public override object Evaluate(Environment env)
        {
            PythonClass parentClass = null;
            if (!string.IsNullOrEmpty(BaseClass))
            {
                var baseObj = env.GetVariable(BaseClass);
                if (baseObj is PythonClass baseClass)
                    parentClass = baseClass;
                else
                    throw new PythonException("TypeError", $"'{BaseClass}' is not a class");
            }

            var classEnv = new Environment(env);
            
            // 부모 클래스의 메서드들을 상속
            if (parentClass != null)
            {
                foreach (var kvp in parentClass.ClassEnv.GetAllVariables())
                    classEnv.SetVariable(kvp.Key, kvp.Value);
            }

            foreach (var stmt in Body)
                stmt.Evaluate(classEnv);
                
            var pythonClass = new PythonClass(Name, classEnv, parentClass);
            env.SetVariable(Name, pythonClass);
            return pythonClass;
        }
    }

    // Control Flow Nodes
    public class IfNode : ASTNode
    {
        public ASTNode Condition { get; }
        public List<ASTNode> ThenBody { get; }
        public List<ASTNode> ElseBody { get; }

        public IfNode(ASTNode condition, List<ASTNode> thenBody, List<ASTNode> elseBody = null)
        {
            Condition = condition;
            ThenBody = thenBody;
            ElseBody = elseBody ?? new List<ASTNode>();
        }

        public override object Evaluate(Environment env)
        {
            var condition = Condition.Evaluate(env);
            var body = IsTrue(condition) ? ThenBody : ElseBody;
            
            object result = null;
            foreach (var stmt in body)
                result = stmt.Evaluate(env);
            return result;
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

    public class ForNode : ASTNode
    {
        public string Variable { get; }
        public ASTNode Iterable { get; }
        public List<ASTNode> Body { get; }

        public ForNode(string variable, ASTNode iterable, List<ASTNode> body)
        {
            Variable = variable;
            Iterable = iterable;
            Body = body;
        }

        public override object Evaluate(Environment env)
        {
            var iterableObj = Iterable.Evaluate(env);
            var items = GetIterableItems(iterableObj);

            object result = null;
            var forEnv = new Environment(env);

            try
            {
                foreach (var item in items)
                {
                    forEnv.SetVariable(Variable, item);
                    try
                    {
                        foreach (var stmt in Body)
                            result = stmt.Evaluate(forEnv);
                    }
                    catch (ContinueException)
                    {
                        continue; // Skip to next iteration
                    }
                }
            }
            catch (BreakException) 
            { 
                // Break out of loop
            }

            return result;
        }

        private List<object> GetIterableItems(object obj)
        {
            if (obj is PythonList list) return list.Items;
            if (obj is PythonTuple tuple) return tuple.Items;
            if (obj is string str) return str.Select(c => c.ToString()).Cast<object>().ToList();
            if (obj is PythonDict dict) return dict.Items.Keys.ToList();
            throw new PythonException("TypeError", $"'{obj?.GetType()}' object is not iterable");
        }
    }

    public class MultiForNode : ASTNode
    {
        public List<string> Variables { get; }
        public ASTNode Iterable { get; }
        public List<ASTNode> Body { get; }

        public MultiForNode(List<string> variables, ASTNode iterable, List<ASTNode> body)
        {
            Variables = variables;
            Iterable = iterable;
            Body = body;
        }

        public override object Evaluate(Environment env)
        {
            var iterableObj = Iterable.Evaluate(env);
            var items = GetIterableItems(iterableObj);

            object result = null;
            var forEnv = new Environment(env);

            try
            {
                foreach (var item in items)
                {
                    // Unpack the item into multiple variables
                    List<object> values;
                    if (item is PythonTuple tuple)
                        values = tuple.Items;
                    else if (item is PythonList list)
                        values = list.Items;
                    else
                        throw new PythonException("ValueError", $"Cannot unpack non-sequence {item?.GetType()}");

                    if (values.Count != Variables.Count)
                        throw new PythonException("ValueError", $"Cannot unpack {values.Count} values into {Variables.Count} variables");

                    for (int i = 0; i < Variables.Count; i++)
                    {
                        forEnv.SetVariable(Variables[i], values[i]);
                    }

                    try
                    {
                        foreach (var stmt in Body)
                            result = stmt.Evaluate(forEnv);
                    }
                    catch (ContinueException)
                    {
                        continue; // Skip to next iteration
                    }
                }
            }
            catch (BreakException) 
            { 
                // Break out of loop
            }

            return result;
        }

        private List<object> GetIterableItems(object obj)
        {
            if (obj is PythonList list) return list.Items;
            if (obj is PythonTuple tuple) return tuple.Items;
            if (obj is string str) return str.Select(c => c.ToString()).Cast<object>().ToList();
            if (obj is PythonDict dict) return dict.Items.Keys.ToList();
            throw new PythonException("TypeError", $"'{obj?.GetType()}' object is not iterable");
        }
    }

    public class WhileNode : ASTNode
    {
        public ASTNode Condition { get; }
        public List<ASTNode> Body { get; }

        public WhileNode(ASTNode condition, List<ASTNode> body)
        {
            Condition = condition;
            Body = body;
        }

        public override object Evaluate(Environment env)
        {
            object result = null;

            try
            {
                while (IsTrue(Condition.Evaluate(env)))
                {
                    try
                    {
                        foreach (var stmt in Body)
                            result = stmt.Evaluate(env);
                    }
                    catch (ContinueException)
                    {
                        continue; // Continue to next iteration
                    }
                }
            }
            catch (BreakException) 
            { 
                // Break out of loop
            }

            return result;
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

    public class BreakNode : ASTNode
    {
        public override object Evaluate(Environment env) => throw new BreakException();
    }

    public class ContinueNode : ASTNode
    {
        public override object Evaluate(Environment env) => throw new ContinueException();
    }

    public class ReturnNode : ASTNode
    {
        public ASTNode Value { get; }
        public ReturnNode(ASTNode value = null) => Value = value;

        public override object Evaluate(Environment env)
        {
            var returnValue = Value?.Evaluate(env);
            throw new ReturnException(returnValue);
        }
    }

    // Exception Handling Nodes
    public class TryNode : ASTNode
    {
        public List<ASTNode> TryBody { get; }
        public List<(string ExceptionType, string Variable, List<ASTNode> Body)> ExceptClauses { get; }
        public List<ASTNode> ElseBody { get; }
        public List<ASTNode> FinallyBody { get; }

        public TryNode(List<ASTNode> tryBody, 
                      List<(string, string, List<ASTNode>)> exceptClauses = null,
                      List<ASTNode> elseBody = null,
                      List<ASTNode> finallyBody = null)
        {
            TryBody = tryBody;
            ExceptClauses = exceptClauses ?? new List<(string, string, List<ASTNode>)>();
            ElseBody = elseBody ?? new List<ASTNode>();
            FinallyBody = finallyBody ?? new List<ASTNode>();
        }

        public override object Evaluate(Environment env)
        {
            object result = null;
            bool exceptionCaught = false;

            try
            {
                foreach (var stmt in TryBody)
                    result = stmt.Evaluate(env);

                if (!exceptionCaught)
                {
                    foreach (var stmt in ElseBody)
                        result = stmt.Evaluate(env);
                }
            }
            catch (PythonException ex)
            {
                exceptionCaught = true;
                bool handled = false;

                foreach (var (exceptionType, variable, body) in ExceptClauses)
                {
                    if (string.IsNullOrEmpty(exceptionType) || ex.Type == exceptionType)
                    {
                        var exceptEnv = new Environment(env);
                        if (!string.IsNullOrEmpty(variable))
                            exceptEnv.SetVariable(variable, ex.Message);

                        foreach (var stmt in body)
                            result = stmt.Evaluate(exceptEnv);
                        handled = true;
                        break;
                    }
                }

                if (!handled) throw;
            }
            catch (Exception ex) when (!(ex is ReturnException || ex is BreakException || ex is ContinueException))
            {
                exceptionCaught = true;
                var pythonEx = new PythonException("Exception", ex.Message);
                
                bool handled = false;
                foreach (var (exceptionType, variable, body) in ExceptClauses)
                {
                    if (string.IsNullOrEmpty(exceptionType) || exceptionType == "Exception")
                    {
                        var exceptEnv = new Environment(env);
                        if (!string.IsNullOrEmpty(variable))
                            exceptEnv.SetVariable(variable, ex.Message);

                        foreach (var stmt in body)
                            result = stmt.Evaluate(exceptEnv);
                        handled = true;
                        break;
                    }
                }

                if (!handled) throw pythonEx;
            }
            finally
            {
                foreach (var stmt in FinallyBody)
                    stmt.Evaluate(env);
            }

            return result;
        }
    }

    public class RaiseNode : ASTNode
    {
        public ASTNode Exception { get; }
        public RaiseNode(ASTNode exception) => Exception = exception;

        public override object Evaluate(Environment env)
        {
            var ex = Exception.Evaluate(env);
            if (ex is string message)
                throw new PythonException("Exception", message);
            throw new PythonException("Exception", ex?.ToString() ?? "");
        }
    }

    // Import and Block Nodes
    public class ImportNode : ASTNode
    {
        public string ModuleName { get; }
        public string Alias { get; }

        public ImportNode(string moduleName, string alias = null)
        {
            ModuleName = moduleName;
            Alias = alias;
        }

        public override object Evaluate(Environment env)
        {
            var module = ModuleSystem.ImportModule(ModuleName);
            string name = Alias ?? ModuleName;
            env.SetVariable(name, module);
            return module;
        }
    }

    public class BlockNode : ASTNode
    {
        public List<ASTNode> Statements { get; }
        public BlockNode(List<ASTNode> statements) => Statements = statements;

        public override object Evaluate(Environment env)
        {
            object result = null;
            foreach (var stmt in Statements)
                result = stmt.Evaluate(env);
            return result;
        }
    }
}