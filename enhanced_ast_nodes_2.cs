// enhanced_ast_nodes_2.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    // Function and Class Definition Nodes with PythonTypeObject
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

        public override PythonTypeObject Evaluate(Environment env)
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

        public override PythonTypeObject Evaluate(Environment env)
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
                    BytecodeFunction bytecodeFunc => bytecodeFunc.Call(args),
                    _ => throw CreateException("TypeError", $"'{function?.Type}' object is not callable")
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

        public override PythonTypeObject Evaluate(Environment env)
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

    // Control Flow Nodes with PythonTypeObject
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

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var condition = Condition.Evaluate(env);
                var body = condition.IsTrue() ? ThenBody : ElseBody;

                PythonTypeObject result = PythonNone.Instance;
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

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var iterableObj = Iterable.Evaluate(env);
                var items = GetIterableItems(iterableObj);

                PythonTypeObject result = PythonNone.Instance;

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

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var iterableObj = Iterable.Evaluate(env);
                var items = GetIterableItems(iterableObj);

                PythonTypeObject result = PythonNone.Instance;

                try
                {
                    foreach (var item in items)
                    {
                        // Unpack the item into multiple variables
                        List<PythonTypeObject> values;
                        if (item is PythonTuple tuple)
                            values = tuple.Items;
                        else if (item is PythonList list)
                            values = list.Items;
                        else
                            throw CreateException("ValueError", $"Cannot unpack non-sequence {item?.Type}");

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

    public class WhileNode : ASTNode
    {
        public ASTNode Condition { get; }
        public List<ASTNode> Body { get; }

        public WhileNode(ASTNode condition, List<ASTNode> body, int line = 0, int column = 0) : base(line, column)
        {
            Condition = condition;
            Body = body;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                PythonTypeObject result = PythonNone.Instance;

                try
                {
                    while (Condition.Evaluate(env).IsTrue())
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
    }

    public class BreakNode : ASTNode
    {
        public BreakNode(int line = 0, int column = 0) : base(line, column) { }
        public override PythonTypeObject Evaluate(Environment env) => throw new BreakException();
    }

    public class ContinueNode : ASTNode
    {
        public ContinueNode(int line = 0, int column = 0) : base(line, column) { }
        public override PythonTypeObject Evaluate(Environment env) => throw new ContinueException();
    }

    public class ReturnNode : ASTNode
    {
        public ASTNode Value { get; }
        
        public ReturnNode(ASTNode value = null, int line = 0, int column = 0) : base(line, column) 
            => Value = value;

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var returnValue = Value?.Evaluate(env) ?? PythonNone.Instance;
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

    // Exception Handling Nodes with PythonTypeObject
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

        public override PythonTypeObject Evaluate(Environment env)
        {
            PythonTypeObject result = PythonNone.Instance;
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
                            env.SetVariable(variable, new PythonString(ex.Message));

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
                            env.SetVariable(variable, new PythonString(ex.Message));

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

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var ex = Exception.Evaluate(env);
                if (ex is PythonString message)
                    throw CreateException("Exception", message.Value);
                throw CreateException("Exception", ex?.ToPythonString() ?? "");
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

    // Import and Block Nodes with PythonTypeObject
    public class ImportNode : ASTNode
    {
        public string ModuleName { get; }
        public string Alias { get; }

        public ImportNode(string moduleName, string alias = null, int line = 0, int column = 0) : base(line, column)
        {
            ModuleName = moduleName;
            Alias = alias;
        }

        public override PythonTypeObject Evaluate(Environment env)
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

        public FromImportNode(string moduleName, List<(string, string)> importItems, int line = 0, int column = 0) : base(line, column)
        {
            ModuleName = moduleName;
            ImportItems = importItems ?? new List<(string, string)>();
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var module = ModuleSystem.ImportModule(ModuleName, env.SearchPaths);
                
                // import * 체크 - "*"라는 특별한 이름으로 구분
                if (ImportItems.Count == 1 && ImportItems[0].Name == "*")
                {
                    // 모듈의 모든 속성을 가져옴 (이제 builtin이 없으므로 깔끔함)
                    var allVars = module.ModuleEnv.GetAllVariables();
                    foreach (var kvp in allVars)
                    {
                        // 언더스코어로 시작하지 않는 것들만 import
                        if (!kvp.Key.StartsWith("_"))
                        {
                            env.SetVariable(kvp.Key, kvp.Value);
                        }
                    }
                }
                else
                {
                    // 기존 코드 그대로
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

                return PythonNone.Instance;
            }
            catch (PythonException)
            {
                throw;
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

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                PythonTypeObject result = PythonNone.Instance;
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

    public class MultipleAssignmentNode : ASTNode
    {
        public List<string> VariableNames { get; }
        public ASTNode Value { get; }

        public MultipleAssignmentNode(List<string> names, ASTNode value, int line = 0, int column = 0) : base(line, column)
        {
            VariableNames = names;
            Value = value;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                var value = Value.Evaluate(env);

                // Convert value to iterable
                List<PythonTypeObject> items;
                if (value is PythonList list)
                    items = list.Items;
                else if (value is PythonTuple tuple)
                    items = tuple.Items;
                else if (value is PythonString str)
                    items = str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList();
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

        public override PythonTypeObject Evaluate(Environment env)
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

                throw CreateException("AttributeError", $"'{obj?.Type}' object has no attribute '{Attribute}'");
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