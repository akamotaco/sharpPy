// enhanced_ast_nodes_2.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    // Function and Class Definition Nodes with Line/Column Tracking
    public class FunctionDefNode : ASTNode
    {
        public string Name { get; }
        public List<Parameter> Parameters { get; }
        public List<ASTNode> Body { get; }
        public TypeHint ReturnTypeHint { get; }

        public FunctionDefNode(string name, List<Parameter> parameters, List<ASTNode> body, TypeHint returnTypeHint = null, int line = 0, int column = 0) : base(line, column)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
            ReturnTypeHint = returnTypeHint;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var function = new UserFunction(Name, Parameters, Body, env, ReturnTypeHint);
                env.SetVariable(Name, function);
                return function;
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error defining function '{Name}': {ex.Message}");
            }
        }
    }

    public class FunctionCallNode : ASTNode
    {
        public ASTNode Function { get; }
        public List<ASTNode> Arguments { get; }

        public FunctionCallNode(ASTNode function, List<ASTNode> arguments, int line = 0, int column = 0) : base(line, column)
        {
            Function = function;
            Arguments = arguments;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var function = Function.Evaluate(env);
                var args = Arguments.Select(arg => arg.Evaluate(env)).ToList();

                return function switch
                {
                    UserFunction userFunc => userFunc.Call(args),
                    LambdaFunction lambdaFunc => lambdaFunc.Call(args),
                    BuiltinFunction builtinFunc => builtinFunc.Call(args),
                    BoundMethod boundMethod => boundMethod.Call(args),
                    PythonClass pythonClass => pythonClass.CreateInstance(args),
                    _ => throw CreateException("TypeError", $"'{function?.GetType()?.Name ?? "null"}' object is not callable")
                };
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (ReturnException)
            {
                throw; // Re-throw ReturnExceptions as-is (though this shouldn't normally happen at this level)
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error calling function: {ex.Message}");
            }
        }
    }

    public class ClassDefNode : ASTNode
    {
        public string Name { get; }
        public string BaseClass { get; }
        public List<ASTNode> Body { get; }

        public ClassDefNode(string name, List<ASTNode> body, string baseClass = null, int line = 0, int column = 0) : base(line, column)
        {
            Name = name;
            BaseClass = baseClass;
            Body = body;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                PythonClass parentClass = null;
                if (!string.IsNullOrEmpty(BaseClass))
                {
                    var baseObj = env.GetVariable(BaseClass);
                    if (baseObj is PythonClass baseClass)
                        parentClass = baseClass;
                    else
                        throw CreateException("TypeError", $"'{BaseClass}' is not a class");
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
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex) when (!(ex is ReturnException || ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Internal error defining class '{Name}': {ex.Message}");
            }
        }
    }

    // Control Flow Nodes with Line/Column Tracking
    public class IfNode : ASTNode
    {
        public ASTNode Condition { get; }
        public List<ASTNode> ThenBody { get; }
        public List<ASTNode> ElseBody { get; }

        public IfNode(ASTNode condition, List<ASTNode> thenBody, List<ASTNode> elseBody = null, int line = 0, int column = 0) : base(line, column)
        {
            Condition = condition;
            ThenBody = thenBody;
            ElseBody = elseBody ?? new List<ASTNode>();
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var condition = Condition.Evaluate(env);
                var body = IsTrue(condition) ? ThenBody : ElseBody;

                object result = null;
                foreach (var stmt in body)
                    result = stmt.Evaluate(env);
                return result;
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex) when (!(ex is ReturnException || ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Internal error in if statement: {ex.Message}");
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

    public class ForNode : ASTNode
    {
        public string Variable { get; }
        public ASTNode Iterable { get; }
        public List<ASTNode> Body { get; }

        public ForNode(string variable, ASTNode iterable, List<ASTNode> body, int line = 0, int column = 0) : base(line, column)
        {
            Variable = variable;
            Iterable = iterable;
            Body = body;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var iterableObj = Iterable.Evaluate(env);
                var items = GetIterableItems(iterableObj);

                object result = null;

                try
                {
                    foreach (var item in items)
                    {
                        env.SetVariable(Variable, item); // 같은 scope에서 변수 설정
                        try
                        {
                            foreach (var stmt in Body)
                                result = stmt.Evaluate(env); // 같은 env 사용
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
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (ReturnException)
            {
                throw; // Re-throw ReturnExceptions as-is
            }
            catch (Exception ex) when (!(ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Internal error in for loop: {ex.Message}");
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
    }

    public class MultiForNode : ASTNode
    {
        public List<string> Variables { get; }
        public ASTNode Iterable { get; }
        public List<ASTNode> Body { get; }

        public MultiForNode(List<string> variables, ASTNode iterable, List<ASTNode> body, int line = 0, int column = 0) : base(line, column)
        {
            Variables = variables;
            Iterable = iterable;
            Body = body;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var iterableObj = Iterable.Evaluate(env);
                var items = GetIterableItems(iterableObj);

                object result = null;

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
                            throw CreateException("ValueError", $"Cannot unpack non-sequence {item?.GetType()}");

                        if (values.Count != Variables.Count)
                            throw CreateException("ValueError", $"Cannot unpack {values.Count} values into {Variables.Count} variables");

                        for (int i = 0; i < Variables.Count; i++)
                        {
                            env.SetVariable(Variables[i], values[i]); // 같은 scope에서 변수 설정
                        }

                        try
                        {
                            foreach (var stmt in Body)
                                result = stmt.Evaluate(env); // 같은 env 사용
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
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (ReturnException)
            {
                throw; // Re-throw ReturnExceptions as-is
            }
            catch (Exception ex) when (!(ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Internal error in for loop with multiple variables: {ex.Message}");
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
    }

    public class WhileNode : ASTNode
    {
        public ASTNode Condition { get; }
        public List<ASTNode> Body { get; }

        public WhileNode(ASTNode condition, List<ASTNode> body, int line = 0, int column = 0) : base(line, column)
        {
            Condition = condition;
            Body = body;
        }

        public override object Evaluate(Environment env)
        {
            try
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
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (ReturnException)
            {
                throw; // Re-throw ReturnExceptions as-is
            }
            catch (Exception ex) when (!(ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Internal error in while loop: {ex.Message}");
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

    public class BreakNode : ASTNode
    {
        public BreakNode(int line = 0, int column = 0) : base(line, column) { }
        public override object Evaluate(Environment env) => throw new BreakException();
    }

    public class ContinueNode : ASTNode
    {
        public ContinueNode(int line = 0, int column = 0) : base(line, column) { }
        public override object Evaluate(Environment env) => throw new ContinueException();
    }

    public class ReturnNode : ASTNode
    {
        public ASTNode Value { get; }
        
        public ReturnNode(ASTNode value = null, int line = 0, int column = 0) : base(line, column) 
            => Value = value;

        public override object Evaluate(Environment env)
        {
            try
            {
                var returnValue = Value?.Evaluate(env);
                throw new ReturnException(returnValue);
            }
            catch (ReturnException)
            {
                throw; // Re-throw return exceptions as-is
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in return statement: {ex.Message}");
            }
        }
    }

    // Exception Handling Nodes with Line/Column Tracking
    public class TryNode : ASTNode
    {
        public List<ASTNode> TryBody { get; }
        public List<(string ExceptionType, string Variable, List<ASTNode> Body)> ExceptClauses { get; }
        public List<ASTNode> ElseBody { get; }
        public List<ASTNode> FinallyBody { get; }

        public TryNode(List<ASTNode> tryBody,
                      List<(string, string, List<ASTNode>)> exceptClauses = null,
                      List<ASTNode> elseBody = null,
                      List<ASTNode> finallyBody = null,
                      int line = 0, int column = 0) : base(line, column)
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
                        // except 절에서 exception variable만 설정 (새로운 scope 만들지 않음)
                        if (!string.IsNullOrEmpty(variable))
                            env.SetVariable(variable, ex.Message);

                        foreach (var stmt in body)
                            result = stmt.Evaluate(env); // 같은 env 사용
                        handled = true;
                        break;
                    }
                }

                if (!handled) throw;
            }
            catch (Exception ex) when (!(ex is ReturnException || ex is BreakException || ex is ContinueException))
            {
                exceptionCaught = true;
                var pythonEx = new PythonException("Exception", ex.Message, Line, Column);

                bool handled = false;
                foreach (var (exceptionType, variable, body) in ExceptClauses)
                {
                    if (string.IsNullOrEmpty(exceptionType) || exceptionType == "Exception")
                    {
                        // except 절에서 exception variable만 설정 (새로운 scope 만들지 않음)
                        if (!string.IsNullOrEmpty(variable))
                            env.SetVariable(variable, ex.Message);

                        foreach (var stmt in body)
                            result = stmt.Evaluate(env); // 같은 env 사용
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
        
        public RaiseNode(ASTNode exception, int line = 0, int column = 0) : base(line, column) 
            => Exception = exception;

        public override object Evaluate(Environment env)
        {
            try
            {
                var ex = Exception.Evaluate(env);
                if (ex is string message)
                    throw CreateException("Exception", message);
                throw CreateException("Exception", ex?.ToString() ?? "");
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error raising exception: {ex.Message}");
            }
        }
    }

    // Import and Block Nodes with Line/Column Tracking
    public class ImportNode : ASTNode
    {
        public string ModuleName { get; }
        public string Alias { get; }

        public ImportNode(string moduleName, string alias = null, int line = 0, int column = 0) : base(line, column)
        {
            ModuleName = moduleName;
            Alias = alias;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var module = ModuleSystem.ImportModule(ModuleName, env.SearchPaths);
                string name = Alias ?? ModuleName.Split('.').Last(); // 패키지의 경우 마지막 이름 사용
                env.SetVariable(name, module);
                return module;
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("ImportError", $"Failed to import module '{ModuleName}': {ex.Message}");
            }
        }
    }

    public class FromImportNode : ASTNode
    {
        public string ModuleName { get; }
        public List<(string Name, string Alias)> ImportItems { get; }
        public bool ImportAll { get; }

        public FromImportNode(string moduleName, List<(string, string)> importItems, bool importAll = false, int line = 0, int column = 0) : base(line, column)
        {
            ModuleName = moduleName;
            ImportItems = importItems ?? new List<(string, string)>();
            ImportAll = importAll;
        }

        public override object Evaluate(Environment env)
        {
            try
            {
                var module = ModuleSystem.ImportModule(ModuleName, env.SearchPaths);
                
                if (ImportAll)
                {
                    // from module import * - Import all public attributes
                    var allVariables = module.ModuleEnv.GetAllVariables();
                    foreach (var kvp in allVariables)
                    {
                        // Skip private attributes (starting with _)
                        if (!kvp.Key.StartsWith("_"))
                        {
                            env.SetVariable(kvp.Key, kvp.Value);
                        }
                    }
                }
                else
                {
                    // from module import specific items
                    foreach (var (name, alias) in ImportItems)
                    {
                        try
                        {
                            var value = module.GetAttribute(name);
                            string varName = alias ?? name;
                            env.SetVariable(varName, value);
                        }
                        catch (PythonException)
                        {
                            throw CreateException("ImportError", $"Cannot import name '{name}' from '{ModuleName}'");
                        }
                    }
                }

                return null;
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (Exception ex)
            {
                throw CreateException("ImportError", $"Failed to import from module '{ModuleName}': {ex.Message}");
            }
        }
    }

    public class BlockNode : ASTNode
    {
        public List<ASTNode> Statements { get; }
        
        public BlockNode(List<ASTNode> statements, int line = 0, int column = 0) : base(line, column) 
            => Statements = statements;

        public override object Evaluate(Environment env)
        {
            try
            {
                object result = null;
                foreach (var stmt in Statements)
                    result = stmt.Evaluate(env);
                return result;
            }
            catch (PythonException)
            {
                throw; // Re-throw PythonExceptions as-is
            }
            catch (ReturnException)
            {
                throw; // Re-throw ReturnExceptions as-is
            }
            catch (Exception ex) when (!(ex is ReturnException || ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Internal error in block: {ex.Message}");
            }
        }
    }
}