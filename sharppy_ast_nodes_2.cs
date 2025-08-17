// enhanced_ast_nodes_2.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpPy
{
    // Optimized Function and Class Definition Nodes
    public sealed class FunctionDefNode : ASTNode
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
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error defining function '{Name}': {ex.Message}");
            }
        }
    }

    public sealed class FunctionCallNode : ASTNode
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
                
                // Pre-evaluate all arguments
                var args = new List<PythonTypeObject>(Arguments.Count);
                foreach (var arg in Arguments)
                    args.Add(arg.Evaluate(env));

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
                throw;
            }
            catch (ReturnException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error calling function: {ex.Message}");
            }
        }
    }

    public sealed class ClassDefNode : ASTNode
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

                // Inherit methods from parent class
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
                throw;
            }
            catch (Exception ex) when (!(ex is ReturnException || ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Internal error defining class '{Name}': {ex.Message}");
            }
        }
    }

    // Optimized Control Flow Nodes
    public sealed class IfNode : ASTNode
    {
        public ASTNode Condition { get; }
        public List<ASTNode> ThenBody { get; }
        public List<(ASTNode Condition, List<ASTNode> Body)> ElifClauses { get; } // elif 추가
        public List<ASTNode> ElseBody { get; }

        public IfNode(ASTNode condition, List<ASTNode> thenBody, 
                    List<(ASTNode, List<ASTNode>)> elifClauses = null,
                    List<ASTNode> elseBody = null, 
                    int line = 0, int column = 0) : base(line, column)
        {
            Condition = condition;
            ThenBody = thenBody;
            ElifClauses = elifClauses ?? new List<(ASTNode, List<ASTNode>)>();
            ElseBody = elseBody ?? new List<ASTNode>();
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                // if 조건 확인
                var condition = Condition.Evaluate(env);
                if (condition.IsTrue())
                {
                    PythonTypeObject result = PythonNone.Instance;
                    foreach (var stmt in ThenBody)
                        result = stmt.Evaluate(env);
                    return result;
                }

                // elif 체인 확인
                foreach (var (elifCondition, elifBody) in ElifClauses)
                {
                    var elifCond = elifCondition.Evaluate(env);
                    if (elifCond.IsTrue())
                    {
                        PythonTypeObject result = PythonNone.Instance;
                        foreach (var stmt in elifBody)
                            result = stmt.Evaluate(env);
                        return result;
                    }
                }

                // else 실행
                PythonTypeObject elseResult = PythonNone.Instance;
                foreach (var stmt in ElseBody)
                    elseResult = stmt.Evaluate(env);
                return elseResult;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex) when (!(ex is ReturnException || ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Internal error in if statement: {ex.Message}");
            }
        }
    }

    public sealed class ForNode : ASTNode
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
                throw CreateException("RuntimeError", $"Internal error in for loop: {ex.Message}");
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

    public sealed class MultiForNode : ASTNode
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
                throw CreateException("RuntimeError", $"Internal error in for loop with multiple variables: {ex.Message}");
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

    public sealed class WhileNode : ASTNode
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
                throw CreateException("RuntimeError", $"Internal error in while loop: {ex.Message}");
            }
        }
    }

    public sealed class BreakNode : ASTNode
    {
        public BreakNode(int line = 0, int column = 0) : base(line, column) { }
        public override PythonTypeObject Evaluate(Environment env) => throw new BreakException();
    }

    public sealed class ContinueNode : ASTNode
    {
        public ContinueNode(int line = 0, int column = 0) : base(line, column) { }
        public override PythonTypeObject Evaluate(Environment env) => throw new ContinueException();
    }

    public sealed class ReturnNode : ASTNode
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
                throw;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in return statement: {ex.Message}");
            }
        }
    }

    // Optimized Exception Handling Nodes
    public sealed class TryNode : ASTNode
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
                        if (!string.IsNullOrEmpty(variable))
                            env.SetVariable(variable, new PythonString(ex.Message));

                        foreach (var stmt in body)
                            result = stmt.Evaluate(env);
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
                        if (!string.IsNullOrEmpty(variable))
                            env.SetVariable(variable, new PythonString(ex.Message));

                        foreach (var stmt in body)
                            result = stmt.Evaluate(env);
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

    public sealed class RaiseNode : ASTNode
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
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error raising exception: {ex.Message}");
            }
        }
    }

    // Optimized Import and Block Nodes
    public sealed class ImportNode : ASTNode
    {
        public string ModuleName { get; }
        public string Alias { get; }

        public ImportNode(string moduleName, string alias = null, int line = 0, int column = 0) 
            : base(line, column)
        {
            ModuleName = moduleName;
            Alias = alias;
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                // env.SearchPaths를 전달
                var module = ModuleSystem.ImportModule(env, ModuleName, env.SearchPaths);
                string name = Alias ?? ModuleName.Split('.').Last();
                env.SetVariable(name, module);
                return module;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("ImportError", 
                    $"Failed to import module '{ModuleName}': {ex.Message}");
            }
        }
    }

    public sealed class FromImportNode : ASTNode
    {
        public string ModuleName { get; }
        public List<(string Name, string Alias)> ImportItems { get; }

        public FromImportNode(string moduleName, List<(string, string)> importItems, 
                            int line = 0, int column = 0) : base(line, column)
        {
            ModuleName = moduleName;
            ImportItems = importItems ?? new List<(string, string)>();
        }

        public override PythonTypeObject Evaluate(Environment env)
        {
            try
            {
                Console.WriteLine($"Importing from module: {ModuleName}");
                
                // import * 처리
                if (ImportItems.Count == 1 && ImportItems[0].Name == "*")
                {
                    var module = ModuleSystem.ImportModule(env, ModuleName, env.SearchPaths);
                    var allVars = module.ModuleEnv.GetAllVariables();
                    foreach (var kvp in allVars)
                    {
                        // __로 시작하는 private 변수는 제외
                        if (!kvp.Key.StartsWith("_"))
                        {
                            env.SetVariable(kvp.Key, kvp.Value);
                        }
                    }
                }
                else
                {
                    // 일반 import 처리
                    foreach (var (name, alias) in ImportItems)
                    {
                        try
                        {
                            var value = ModuleSystem.ImportFrom(env, ModuleName, name, env.SearchPaths);
                            string varName = alias ?? name;
                            env.SetVariable(varName, value);
                        }
                        catch (PythonException ex)
                        {
                            throw CreateException("ImportError",
                                $"Cannot import name '{name}' from '{ModuleName}': {ex.Message}");
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
                throw CreateException("ImportError", 
                    $"Failed to import from module '{ModuleName}': {ex.Message}");
            }
        }
    }

    public sealed class BlockNode : ASTNode
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
                throw;
            }
            catch (ReturnException)
            {
                throw;
            }
            catch (Exception ex) when (!(ex is ReturnException || ex is BreakException || ex is ContinueException))
            {
                throw CreateException("RuntimeError", $"Internal error in block: {ex.Message}");
            }
        }
    }

    public sealed class MultipleAssignmentNode : ASTNode
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
                List<PythonTypeObject> items = value switch
                {
                    PythonList list => list.Items,
                    PythonTuple tuple => tuple.Items,
                    PythonString str => str.Value.Select(c => new PythonString(c.ToString()) as PythonTypeObject).ToList(),
                    _ => throw CreateException("TypeError", "Cannot unpack non-iterable object")
                };

                // Handle underscore (_) - variables to ignore
                var validNames = new List<string>(VariableNames.Count);
                var validIndices = new List<int>(VariableNames.Count);

                for (int i = 0; i < VariableNames.Count; i++)
                {
                    if (VariableNames[i] != "_")
                    {
                        validNames.Add(VariableNames[i]);
                        validIndices.Add(i);
                    }
                }

                // Length validation
                if (items.Count != VariableNames.Count)
                    throw CreateException("ValueError", $"Cannot unpack {items.Count} values into {VariableNames.Count} variables");

                // Assign values to variables
                for (int i = 0; i < validNames.Count; i++)
                {
                    int actualIndex = validIndices[i];
                    env.SetVariable(validNames[i], items[actualIndex]);
                }

                return value;
            }
            catch (PythonException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in multiple assignment: {ex.Message}");
            }
        }
    }

    public sealed class AttributeAssignmentNode : ASTNode
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
                throw;
            }
            catch (Exception ex)
            {
                throw CreateException("RuntimeError", $"Internal error in attribute assignment: {ex.Message}");
            }
        }
    }
}

// enhanced_ast_nodes_2.cs