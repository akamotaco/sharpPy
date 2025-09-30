using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpPy
{
    // CPython 3.12 스타일 SyntaxError 예외 클래스
    public class PySyntaxErrorException : Exception
    {
        public PySyntaxErrorException(string message) : base(message) { }
    }

    /// <summary>
    /// Return 문과 yield 문을 위한 예외 클래스
    /// </summary>
    public class PyReturnException : Exception
    {
        public PyObject Value { get; }
        public PyReturnException(PyObject value) { Value = value; }
    }
    
    public class PyYieldException : Exception
    {
        public PyObject Value { get; }
        public PyYieldException(PyObject value) { Value = value; }
    }
    
    public class PyYieldFromException : Exception
    {
        public PyObject Iterable { get; }
        public PyYieldFromException(PyObject iterable) { Iterable = iterable; }
    }

    public class PyBreakException : Exception { }
    public class PyContinueException : Exception { }

    // Import alias for from import statements
    public class ImportAlias
    {
        public string Name { get; set; }
        public string AsName { get; set; }

        public ImportAlias(string name, string asName = null)
        {
            Name = name;
            AsName = asName;
        }

        public override string ToString()
        {
            return AsName != null ? $"{Name} as {AsName}" : Name;
        }
    }

    #region CPython 3.12 Compatible AST Context and Operators

    /// <summary>
    /// CPython 3.12: Expression context (Store, Load, Del)
    /// </summary>
    public abstract class ExprContext
    {
        public abstract string ContextType { get; }
        public override string ToString() => $"{ContextType}()";
    }

    public class Store : ExprContext
    {
        public override string ContextType => "Store";
        public static Store Instance { get; } = new Store();
    }

    public class Load : ExprContext
    {
        public override string ContextType => "Load";
        public static Load Instance { get; } = new Load();
    }

    public class Del : ExprContext
    {
        public override string ContextType => "Del";
        public static Del Instance { get; } = new Del();
    }

    /// <summary>
    /// CPython 3.12: Binary operators
    /// </summary>
    public abstract class BinaryOperator
    {
        public abstract string OperatorType { get; }
        public abstract PyObject Apply(PyObject left, PyObject right);
        public abstract BinaryOpType GetOpType();
        public override string ToString() => $"{OperatorType}()";
    }

    public class Add : BinaryOperator
    {
        public override string OperatorType => "Add";
        public override PyObject Apply(PyObject left, PyObject right) => left.Add(right);
        public override BinaryOpType GetOpType() => BinaryOpType.ADD;
        public static Add Instance { get; } = new Add();
    }

    public class Sub : BinaryOperator
    {
        public override string OperatorType => "Sub";
        public override PyObject Apply(PyObject left, PyObject right) => left.Subtract(right);
        public override BinaryOpType GetOpType() => BinaryOpType.SUBTRACT;
        public static Sub Instance { get; } = new Sub();
    }

    public class Mult : BinaryOperator
    {
        public override string OperatorType => "Mult";
        public override PyObject Apply(PyObject left, PyObject right) => left.Multiply(right);
        public override BinaryOpType GetOpType() => BinaryOpType.MULTIPLY;
        public static Mult Instance { get; } = new Mult();
    }

    public class Div : BinaryOperator
    {
        public override string OperatorType => "Div";
        public override PyObject Apply(PyObject left, PyObject right) => left.Divide(right);
        public override BinaryOpType GetOpType() => BinaryOpType.TRUE_DIVIDE;
        public static Div Instance { get; } = new Div();
    }

    public class FloorDiv : BinaryOperator
    {
        public override string OperatorType => "FloorDiv";
        public override PyObject Apply(PyObject left, PyObject right) => left.FloorDivide(right);
        public override BinaryOpType GetOpType() => BinaryOpType.FLOOR_DIVIDE;
        public static FloorDiv Instance { get; } = new FloorDiv();
    }

    public class Mod : BinaryOperator
    {
        public override string OperatorType => "Mod";
        public override PyObject Apply(PyObject left, PyObject right) => left.Modulo(right);
        public override BinaryOpType GetOpType() => BinaryOpType.MODULO;
        public static Mod Instance { get; } = new Mod();
    }

    public class Pow : BinaryOperator
    {
        public override string OperatorType => "Pow";
        public override PyObject Apply(PyObject left, PyObject right) => left.Power(right);
        public override BinaryOpType GetOpType() => BinaryOpType.POWER;
        public static Pow Instance { get; } = new Pow();
    }

    public class LShift : BinaryOperator
    {
        public override string OperatorType => "LShift";
        public override PyObject Apply(PyObject left, PyObject right) => left.LeftShift(right);
        public override BinaryOpType GetOpType() => BinaryOpType.LSHIFT;
        public static LShift Instance { get; } = new LShift();
    }

    public class RShift : BinaryOperator
    {
        public override string OperatorType => "RShift";
        public override PyObject Apply(PyObject left, PyObject right) => left.RightShift(right);
        public override BinaryOpType GetOpType() => BinaryOpType.RSHIFT;
        public static RShift Instance { get; } = new RShift();
    }

    public class BitOr : BinaryOperator
    {
        public override string OperatorType => "BitOr";
        public override PyObject Apply(PyObject left, PyObject right) => left.BitwiseOr(right);
        public override BinaryOpType GetOpType() => BinaryOpType.OR;
        public static BitOr Instance { get; } = new BitOr();
    }

    public class BitXor : BinaryOperator
    {
        public override string OperatorType => "BitXor";
        public override PyObject Apply(PyObject left, PyObject right) => left.BitwiseXor(right);
        public override BinaryOpType GetOpType() => BinaryOpType.XOR;
        public static BitXor Instance { get; } = new BitXor();
    }

    public class BitAnd : BinaryOperator
    {
        public override string OperatorType => "BitAnd";
        public override PyObject Apply(PyObject left, PyObject right) => left.BitwiseAnd(right);
        public override BinaryOpType GetOpType() => BinaryOpType.AND;
        public static BitAnd Instance { get; } = new BitAnd();
    }

    public class MatMult : BinaryOperator
    {
        public override string OperatorType => "MatMult";
        public override PyObject Apply(PyObject left, PyObject right) => throw new NotImplementedException("Matrix multiply (@) not yet implemented");
        public override BinaryOpType GetOpType() => BinaryOpType.MATRIX_MULTIPLY;
        public static MatMult Instance { get; } = new MatMult();
    }

    /// <summary>
    /// CPython 3.12: Unary operators
    /// </summary>
    public abstract class UnaryOperator
    {
        public abstract string OperatorType { get; }
        public abstract PyObject Apply(PyObject operand);
        public override string ToString() => $"{OperatorType}()";
    }

    public class UAdd : UnaryOperator
    {
        public override string OperatorType => "UAdd";
        public override PyObject Apply(PyObject operand) => operand.Positive();
        public static UAdd Instance { get; } = new UAdd();
    }

    public class USub : UnaryOperator
    {
        public override string OperatorType => "USub";
        public override PyObject Apply(PyObject operand) => operand.Negative();
        public static USub Instance { get; } = new USub();
    }

    public class Not : UnaryOperator
    {
        public override string OperatorType => "Not";
        public override PyObject Apply(PyObject operand) => PyBool.FromBool(!operand.ToBool());
        public static Not Instance { get; } = new Not();
    }

    public class Invert : UnaryOperator
    {
        public override string OperatorType => "Invert";
        public override PyObject Apply(PyObject operand) => operand.BitwiseNot();
        public static Invert Instance { get; } = new Invert();
    }

    /// <summary>
    /// CPython 3.12: Comparison operators
    /// </summary>
    public abstract class ComparisonOperator
    {
        public abstract string OperatorType { get; }
        public override string ToString() => $"{OperatorType}()";
    }

    public class Eq : ComparisonOperator { public override string OperatorType => "Eq"; public static Eq Instance { get; } = new Eq(); }
    public class NotEq : ComparisonOperator { public override string OperatorType => "NotEq"; public static NotEq Instance { get; } = new NotEq(); }
    public class Lt : ComparisonOperator { public override string OperatorType => "Lt"; public static Lt Instance { get; } = new Lt(); }
    public class LtE : ComparisonOperator { public override string OperatorType => "LtE"; public static LtE Instance { get; } = new LtE(); }
    public class Gt : ComparisonOperator { public override string OperatorType => "Gt"; public static Gt Instance { get; } = new Gt(); }
    public class GtE : ComparisonOperator { public override string OperatorType => "GtE"; public static GtE Instance { get; } = new GtE(); }
    public class Is : ComparisonOperator { public override string OperatorType => "Is"; public static Is Instance { get; } = new Is(); }
    public class IsNot : ComparisonOperator { public override string OperatorType => "IsNot"; public static IsNot Instance { get; } = new IsNot(); }
    public class In : ComparisonOperator { public override string OperatorType => "In"; public static In Instance { get; } = new In(); }
    public class NotIn : ComparisonOperator { public override string OperatorType => "NotIn"; public static NotIn Instance { get; } = new NotIn(); }

    /// <summary>
    /// CPython 3.12: Boolean operators
    /// </summary>
    public abstract class BoolOperator
    {
        public abstract string OperatorType { get; }
        public abstract PyObject Apply(List<PyObject> values);
        public override string ToString() => $"{OperatorType}()";
    }

    public class And : BoolOperator
    {
        public override string OperatorType => "And";
        public override PyObject Apply(List<PyObject> values)
        {
            // Short-circuit evaluation: return first falsy value or last value
            foreach (var value in values)
            {
                if (!value.ToBool())
                    return value;
            }
            return values[values.Count - 1];
        }
        public static And Instance { get; } = new And();
    }

    public class Or : BoolOperator
    {
        public override string OperatorType => "Or";
        public override PyObject Apply(List<PyObject> values)
        {
            // Short-circuit evaluation: return first truthy value or last value
            foreach (var value in values)
            {
                if (value.ToBool())
                    return value;
            }
            return values[values.Count - 1];
        }
        public static Or Instance { get; } = new Or();
    }

    #endregion
}

namespace SharpPy
{
    #region AST Extension (Pipeline: AST Nodes)

    /// <summary>
    /// AST 노드의 베이스 클래스
    /// </summary>
    /// <summary>
    /// Visitor pattern interfaces for AST traversal (CPython style)
    /// </summary>
    public interface IASTVisitor<T>
    {
        T VisitNode(ASTNode node);
        T VisitStatement(Statement node);
        T VisitExpression(Expression node);
        // Add specific node visit methods as needed
        T GenericVisit(ASTNode node); // Default implementation
    }
    
    public interface IASTVisitor
    {
        void VisitNode(ASTNode node);
        void VisitStatement(Statement node);
        void VisitExpression(Expression node);
        void GenericVisit(ASTNode node);
    }

    /// <summary>
    /// Enhanced AST Node base class with source location tracking and visitor support (CPython 3.12 style)
    /// </summary>
    public abstract class ASTNode
    {
        public abstract string NodeType { get; }
        
        // Source Location Information (Python 3.12 requirement)
        public int LineNo { get; set; } = -1;
        public int ColOffset { get; set; } = -1;
        public int? EndLineNo { get; set; }
        public int? EndColOffset { get; set; }
        
        // Parent tracking for advanced analysis
        public ASTNode? Parent { get; set; }
        
        public override string ToString() => $"{NodeType} at {LineNo}:{ColOffset}";
        
        // Visitor pattern support
        public abstract T Accept<T>(IASTVisitor<T> visitor);
        public abstract void Accept(IASTVisitor visitor);
        
        /// <summary>
        /// Evaluate 메서드 - 각 노드별 구현으로 위임
        /// </summary>
        public abstract PyObject Evaluate(PyScope scope);
        
        /// <summary>
        /// Enhanced evaluation with execution context (for better error handling)
        /// </summary>
        public virtual PyObject Evaluate(PyScope scope, IExecutionContext? context = null)
        {
            try
            {
                return Evaluate(scope);
            }
            catch (Exception ex) when (context != null)
            {
                // Enhanced error handling with source location
                throw context.CreateException(this, ex.Message, ex);
            }
        }
    }
    
    /// <summary>
    /// Execution context for enhanced error handling and debugging
    /// </summary>
    public interface IExecutionContext
    {
        string FileName { get; set; }
        Stack<ASTNode> CallStack { get; }
        PyScope CurrentScope { get; set; }
        
        Exception CreateException(ASTNode node, string message, Exception? innerException = null);
    }

    #region Statement Nodes

    /// <summary>
    /// 문장 노드들의 베이스 클래스
    /// </summary>
    public abstract class Statement : ASTNode 
    {
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitStatement(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitStatement(this);
        }
    }

    /// <summary>
    /// 데코레이터 표현식 (@decorator_name or @decorator(args))
    /// </summary>
    public class DecoratorExpression : ASTNode
    {
        public override string NodeType => "Decorator";
        public Expression DecoratorFunction { get; }
        public List<Expression> Arguments { get; }

        public DecoratorExpression(Expression decoratorFunction, List<Expression>? arguments = null)
        {
            DecoratorFunction = decoratorFunction;
            Arguments = arguments ?? new List<Expression>();
        }

        public override PyObject Evaluate(PyScope scope)
        {
            // 데코레이터 함수를 평가
            var decoratorFunc = DecoratorFunction.Evaluate(scope);
            
            // 데코레이터에 인수가 있는 경우 (@decorator(arg1, arg2))
            if (Arguments.Any())
            {
                var args = Arguments.Select(arg => arg.Evaluate(scope)).ToArray();
                // 데코레이터 함수를 호출하여 실제 데코레이터를 반환
                if (decoratorFunc is PyFunction func)
                {
                    return func.Call(args, null);
                }
            }
            
            return decoratorFunc;
        }

        public override T Accept<T>(IASTVisitor<T> visitor) => visitor.GenericVisit(this);
        public override void Accept(IASTVisitor visitor) => visitor.GenericVisit(this);

        public override string ToString()
        {
            if (Arguments.Any())
            {
                var argsStr = string.Join(", ", Arguments.Select(a => a.ToString()));
                return $"@{DecoratorFunction}({argsStr})";
            }
            return $"@{DecoratorFunction}";
        }
    }

    /// <summary>
    /// CPython 3.12 compatible Assign statement
    /// Supports both single and chained assignments through targets list
    /// </summary>
    public class AssignStatement : Statement
    {
        public override string NodeType => "Assign";
        public List<Expression> Targets { get; }
        public Expression Value { get; }

        // CPython 3.12 compatible constructor: targets can be multiple
        public AssignStatement(List<Expression> targets, Expression value)
        {
            Targets = targets ?? throw new ArgumentNullException(nameof(targets));
            Value = value ?? throw new ArgumentNullException(nameof(value));

            if (Targets.Count == 0)
                throw new ArgumentException("At least one target required", nameof(targets));
        }

        // Convenience constructor for single target
        public AssignStatement(Expression target, Expression value)
            : this(new List<Expression> { target }, value)
        {
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var value = Value.Evaluate(scope);

            // Assign to all targets (CPython evaluates right-to-left)
            foreach (var target in Targets)
            {
                switch (target)
                {
                    case NameExpression name:
                        scope.SetVariable(name.Name, value);
                        break;
                    case AttributeExpression attr:
                        var obj = attr.Value.Evaluate(scope);
                        obj.SetAttribute(attr.Attr, value);
                        break;
                    case SubscriptExpression subscript:
                        var container = subscript.Value.Evaluate(scope);
                        var index = subscript.Slice.Evaluate(scope);
                        container.SetItem(index, value);
                        break;
                    case TupleExpression tuple:
                        // Tuple unpacking: a, b = value
                        var iterator = value.GetIterator();
                        var items = new List<PyObject>();
                        PyObject item;
                        while ((item = iterator.Next()) != null)
                            items.Add(item);

                        if (items.Count != tuple.Elements.Count)
                            throw new Exception($"Unpacking: expected {tuple.Elements.Count} values, got {items.Count}");

                        for (int i = 0; i < tuple.Elements.Count; i++)
                        {
                            if (tuple.Elements[i] is NameExpression tupleTarget)
                                scope.SetVariable(tupleTarget.Name, items[i]);
                            else
                                throw new Exception($"Invalid unpacking target: {tuple.Elements[i].GetType().Name}");
                        }
                        break;
                    default:
                        throw new Exception($"Unsupported assignment target: {target.GetType().Name}");
                }
            }

            return value;
        }

        public override string ToString() => $"{string.Join(" = ", Targets)} = {Value}";
    }

    // CPython 3.12: Attribute assignment (self.x = value)
    public class AttributeStatement : Statement
    {
        public override string NodeType => "AttributeAssign";
        public Expression Object { get; }
        public string Attr { get; }
        public Expression Value { get; }

        public AttributeStatement(Expression obj, string attr, Expression value)
        {
            Object = obj;
            Attr = attr;
            Value = value;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var obj = Object.Evaluate(scope);
            var value = Value.Evaluate(scope);
            obj.SetAttribute(Attr, value);
            return value;
        }

        public override string ToString() => $"{Object}.{Attr} = {Value}";
    }
    
    // CPython 3.12: General assignment with expression target
    public class AssignTargetStatement : Statement
    {
        public override string NodeType => "AssignTarget";
        public Expression Target { get; }
        public Expression Value { get; }
        
        public AssignTargetStatement(Expression target, Expression value)
        {
            Target = target;
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var value = Value.Evaluate(scope);
            
            // Handle different assignment targets
            switch (Target)
            {
                case NameExpression name:
                    scope.SetVariable(name.Name, value);
                    break;
                    
                case AttributeExpression attr:
                    var obj = attr.Value.Evaluate(scope);
                    obj.SetAttribute(attr.Attr, value);
                    break;
                    
                case SubscriptExpression subscript:
                    var target = subscript.Value.Evaluate(scope);
                    var index = subscript.Slice.Evaluate(scope);
                    target.SetItem(index, value);
                    break;
                    
                default:
                    throw new Exception($"Invalid assignment target: {Target.GetType().Name}");
            }
            
            return value;
        }
        
        public override string ToString() => $"{Target} = {Value}";
    }

    // CPython 3.12: Augmented assignment statement (x += y, obj.attr += z, list[0] += 1)
    public class AugmentedAssignStatement : Statement
    {
        public override string NodeType => "AugAssign";
        public Expression Target { get; }
        public string Op { get; }
        public Expression Value { get; }
        
        public AugmentedAssignStatement(Expression target, string op, Expression value)
        {
            Target = target;
            Op = op;
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // x += y is equivalent to x = x + y, but target is evaluated only once
            PyObject currentValue;
            
            switch (Target)
            {
                case NameExpression name:
                    currentValue = scope.GetVariable(name.Name);
                    break;
                    
                case AttributeExpression attr:
                    var obj = attr.Value.Evaluate(scope);
                    currentValue = obj.GetAttribute(attr.Attr);
                    break;
                    
                case SubscriptExpression subscript:
                    var target = subscript.Value.Evaluate(scope);
                    var index = subscript.Slice.Evaluate(scope);
                    currentValue = target.GetItem(index);
                    break;
                    
                default:
                    throw new Exception($"Invalid augmented assignment target: {Target.GetType().Name}");
            }
            
            // Perform binary operation
            var rightValue = Value.Evaluate(scope);
            var result = Op switch
            {
                "+" => currentValue.Add(rightValue),
                "-" => currentValue.Subtract(rightValue),
                "*" => currentValue.Multiply(rightValue),
                "/" => currentValue.Divide(rightValue),
                "%" => currentValue.Modulo(rightValue),
                "//" => currentValue.FloorDivide(rightValue),
                "**" => currentValue.Power(rightValue),
                "|" => currentValue.BitwiseOr(rightValue),
                "&" => currentValue.BitwiseAnd(rightValue),
                "^" => currentValue.BitwiseXor(rightValue),
                "<<" => currentValue.LeftShift(rightValue),
                ">>" => currentValue.RightShift(rightValue),
                _ => throw new Exception($"Unknown binary operator: {Op}")
            };
            
            // Store result back to target
            switch (Target)
            {
                case NameExpression name:
                    scope.SetVariable(name.Name, result);
                    break;
                    
                case AttributeExpression attr:
                    var obj = attr.Value.Evaluate(scope);
                    obj.SetAttribute(attr.Attr, result);
                    break;
                    
                case SubscriptExpression subscript:
                    var target = subscript.Value.Evaluate(scope);
                    var index = subscript.Slice.Evaluate(scope);
                    target.SetItem(index, result);
                    break;
            }
            
            return result;
        }
        
        public override string ToString() => $"{Target} {Op}= {Value}";
    }

    // Chained assignment statement (a = b = c + d)
    public class ChainedAssignStatement : Statement
    {
        public override string NodeType => "ChainedAssign";
        public List<Expression> Targets { get; }
        public Expression Value { get; }
        
        public ChainedAssignStatement(List<Expression> targets, Expression value)
        {
            Targets = targets;
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var value = Value.Evaluate(scope);
            
            // Assign to all targets (right-to-left like CPython)
            foreach (var target in Targets)
            {
                switch (target)
                {
                    case NameExpression name:
                        scope.SetVariable(name.Name, value);
                        break;
                    case AttributeExpression attr:
                        var obj = attr.Value.Evaluate(scope);
                        obj.SetAttribute(attr.Attr, value);
                        break;
                    case SubscriptExpression subscript:
                        var container = subscript.Value.Evaluate(scope);
                        var index = subscript.Slice.Evaluate(scope);
                        container.SetItem(index, value);
                        break;
                    default:
                        throw new Exception($"Unsupported assignment target: {target.GetType().Name}");
                }
            }
            
            return value;
        }
        
        public override string ToString() => $"{string.Join(" = ", Targets)} = {Value}";
    }

    // PEP 526: Annotated assignment statement (name: type or name: type = value)
    public class AnnAssignStatement : Statement
    {
        public override string NodeType => "AnnAssign";
        public string VariableName { get; }
        public Expression Annotation { get; }
        public Expression? Value { get; }

        public AnnAssignStatement(string variableName, Expression annotation, Expression? value = null)
        {
            VariableName = variableName;
            Annotation = annotation;
            Value = value;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            // Store the annotation in __annotations__ if at module/class level
            if (scope.Type == ScopeType.Module || scope.Type == ScopeType.Class || scope.Type == ScopeType.Global)
            {
                var annotationsDict = scope.GetVariable("__annotations__") as PyDict ??
                    new PyDict();
                annotationsDict.SetItem(new PyString(VariableName), Annotation.Evaluate(scope));
                scope.SetVariable("__annotations__", annotationsDict);
            }
            
            // Assign value if present
            if (Value != null)
            {
                var value = Value.Evaluate(scope);
                scope.SetVariable(VariableName, value);
                return value;
            }
            
            return PyNone.Instance;
        }

        public override string ToString()
        {
            return Value != null ? $"{VariableName}: {Annotation} = {Value}" : $"{VariableName}: {Annotation}";
        }
    }

    public class AugAssignStatement : Statement
    {
        public override string NodeType => "AugAssign";
        public string Target { get; }
        public string Op { get; }
        public Expression Value { get; }
        
        public AugAssignStatement(string target, string op, Expression value)
        {
            Target = target;
            Op = op;
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var currentValue = scope.GetVariable(Target);
            var rightValue = Value.Evaluate(scope);
            
            PyObject result = Op switch
            {
                "+="  => currentValue.Add(rightValue),
                "-="  => currentValue.Subtract(rightValue),
                "*="  => currentValue.Multiply(rightValue),
                "/="  => currentValue.Divide(rightValue),
                "//=" => currentValue.FloorDivide(rightValue),
                "%="  => currentValue.Modulo(rightValue),
                "**=" => currentValue.Power(rightValue),
                "&="  => currentValue.BitwiseAnd(rightValue),
                "|="  => currentValue.BitwiseOr(rightValue),
                "^="  => currentValue.BitwiseXor(rightValue),
                "<<=" => currentValue.LeftShift(rightValue),
                ">>=" => currentValue.RightShift(rightValue),
                _ => throw new NotImplementedException($"Augmented assignment operator {Op} not implemented")
            };
            
            scope.SetVariable(Target, result);
            return result;
        }
        
        public override string ToString() => $"{Target} {Op} {Value}";
    }

    public class WalrusStatement : Statement
    {
        public override string NodeType => "NamedExpr";
        public string Target { get; }
        public Expression Value { get; }
        
        public WalrusStatement(string target, Expression value)
        {
            Target = target;
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return PyExecutor.ExecuteWalrusOperator(this, scope);
        }
        
        public override string ToString() => $"{Target} := {Value}";
    }

    public class ExpressionStatement : Statement
    {
        public override string NodeType => "Expr";
        public Expression Expression { get; }
        
        public ExpressionStatement(Expression expression)
        {
            Expression = expression;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return Expression.Evaluate(scope);
        }
        
        public override string ToString() => Expression.ToString();
    }

    public class ReturnStatement : Statement
    {
        public override string NodeType => "Return";
        public Expression? Value { get; }
        
        public ReturnStatement(Expression? value = null)
        {
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var result = Value?.Evaluate(scope) ?? PyNone.Instance;
            throw new PyReturnException(result);
        }
        
        public override string ToString() => $"return {Value?.ToString() ?? ""}";
    }

    public class YieldStatement : Statement
    {
        public override string NodeType => "Yield";
        public Expression? Value { get; }

        public YieldStatement(Expression? value = null)
        {
            Value = value;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var result = Value?.Evaluate(scope) ?? PyNone.Instance;
            throw new PyYieldException(result);
        }

        public override string ToString() => $"yield {Value?.ToString() ?? ""}";
    }

    public class YieldExpression : Expression
    {
        public override string NodeType => "Yield";
        public Expression? Value { get; }

        public YieldExpression(Expression? value = null)
        {
            Value = value;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var result = Value?.Evaluate(scope) ?? PyNone.Instance;
            throw new PyYieldException(result);
        }

        public override string ToString() => $"yield {Value?.ToString() ?? ""}";
    }

    public class YieldFromStatement : Statement
    {
        public override string NodeType => "YieldFrom";
        public Expression Value { get; }

        public YieldFromStatement(Expression value)
        {
            Value = value;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var iterable = Value.Evaluate(scope);
            // yield from은 복잡한 구현이 필요하므로 간단히 구현
            if (iterable is PyGenerator generator)
            {
                // 실제로는 모든 값을 yield해야 하지만, 여기서는 마지막 값만 반환
                PyObject lastValue = PyNone.Instance;
                try
                {
                    while (true)
                    {
                        lastValue = generator.Next();
                        throw new PyYieldException(lastValue);
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    return lastValue;
                }
            }
            throw new PyYieldException(iterable);
        }

        public override string ToString() => $"yield from {Value}";
    }

    public class YieldFromExpression : Expression
    {
        public override string NodeType => "YieldFrom";
        public Expression Value { get; }

        public YieldFromExpression(Expression value)
        {
            Value = value;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var iterable = Value.Evaluate(scope);
            // yield from은 복잡한 구현이 필요하므로 간단히 구현
            if (iterable is PyGenerator generator)
            {
                // 실제로는 모든 값을 yield해야 하지만, 여기서는 마지막 값만 반환
                PyObject lastValue = PyNone.Instance;
                try
                {
                    while (true)
                    {
                        lastValue = generator.Next();
                        throw new PyYieldException(lastValue);
                    }
                }
                catch (PythonException ex) when (ex.PyException is PyStopIteration)
                {
                    return lastValue;
                }
            }
            throw new PyYieldException(iterable);
        }

        public override string ToString() => $"yield from {Value}";
    }

    public class FunctionDefStatement : Statement
    {
        public override string NodeType => "FunctionDef";
        public string Name { get; }
        public List<string> Parameters { get; }
        public List<Statement> Body { get; }
        public List<string> TypeParams { get; } // Python 3.12
        public List<DecoratorExpression> Decorators { get; } // Decorator support
        public Expression? ReturnTypeAnnotation { get; } // Python 3.12 Type Hints
        
        public FunctionDefStatement(string name, List<string> parameters, List<Statement> body, List<string>? typeParams = null, List<DecoratorExpression>? decorators = null, Expression? returnTypeAnnotation = null)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
            TypeParams = typeParams ?? new List<string>();
            Decorators = decorators ?? new List<DecoratorExpression>();
            ReturnTypeAnnotation = returnTypeAnnotation;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            Console.WriteLine($"📋 FunctionDefStatement.Evaluate 실행: {Name}");
            
            // 제네릭 함수도 일반 함수처럼 실행 (임시 해결책)
            // TODO: 적절한 제네릭 타입 시스템 구현
            if (TypeParams.Any())
            {
                return PyExecutor.ExecuteGenericFunction(this, scope);
            }
            
            // CPython 호환: 매개변수와 기본값 분리
            var (paramNames, defaults) = ParseParametersAndDefaults(Parameters, scope);

            // Check if function has *args or **kwargs - if so, use VM execution
            bool hasStarArgs = Parameters.Any(p => p.StartsWith("*") && !p.StartsWith("**"));
            bool hasKwArgs = Parameters.Any(p => p.StartsWith("**"));

            PyFunction function;

            if (hasStarArgs || hasKwArgs)
            {
                // Use bytecode compilation and VM's MAKE_FUNCTION for *args/**kwargs functions
                var compiler = new PythonCompiler();

                // Set the appropriate flags for *args/**kwargs
                int flags = 0;
                if (hasStarArgs) flags |= PyCodeObject.CO_VARARGS;
                if (hasKwArgs) flags |= PyCodeObject.CO_VARKEYWORDS;

                // Count positional-only parameters (CPython 3.12)
                int posonlyArgCount = 0;
                foreach (var param in Parameters)
                {
                    if (param.Contains("[posonly]"))
                    {
                        posonlyArgCount++;
                    }
                    else
                    {
                        break; // positional-only parameters must come first
                    }
                }

                Console.WriteLine($"[DEBUG] Function '{Name}': {posonlyArgCount} positional-only parameters");

                var funcCode = compiler.CompileFunction(Body, Name, paramNames, defaults, flags, posonlyArgCount);

                // Create a simple implementation that properly executes the function using VM frame
                function = new PyFunction(Name, args =>
                {
                    // Create a proper scope chain for VM execution
                    var scopeChain = new PyScopeChain();

                    // Add the current scope to the chain
                    if (scope != null && scope.Type == ScopeType.Global)
                    {
                        // If we're already in global scope, use it as the base
                        foreach (var variable in scope.Variables)
                        {
                            scopeChain.AssignVariable(variable.Key, variable.Value);
                        }
                    }

                    // Create and execute a frame for this function call
                    var frame = new PyFrame(funcCode, args, scopeChain);
                    return PyVM.Instance.ExecuteFrame(frame);
                }, codeObject: funcCode);
            }
            else
            {
                // Use direct AST evaluation for simple functions
                function = new PyFunction(Name, args =>
                {
                    // 함수 스코프 생성
                    var funcScope = new PyScope(ScopeType.Local, scope, Name);

                    // CPython 호환: 매개변수 바인딩 (기본값 처리 포함)
                    BindArgumentsToParameters(paramNames, defaults, args, funcScope);

                    // 함수 몸체 실행
                    try
                    {
                        PyObject result = PyNone.Instance;
                        foreach (var stmt in Body)
                        {
                            result = stmt.Evaluate(funcScope);
                        }
                        return result;
                    }
                    catch (PyReturnException ret)
                    {
                        return ret.Value;
                    }
                });
            }
            
            // 데코레이터 적용 (역순으로 적용)
            PyObject decoratedFunction = function;
            for (int i = Decorators.Count - 1; i >= 0; i--)
            {
                var decorator = Decorators[i].Evaluate(scope);
                if (decorator is PyFunction decoratorFunc)
                {
                    decoratedFunction = decoratorFunc.Call(new PyObject[] { decoratedFunction }, null);
                }
                else
                {
                    // 데코레이터가 함수가 아닌 경우 (예: property 등)
                    // 여기서는 단순히 데코레이터를 무시하고 원본 함수를 반환
                    // 실제로는 더 복잡한 로직이 필요할 수 있음
                    Console.WriteLine($"Warning: Decorator {decorator} is not a function, skipping");
                }
            }
            
            scope.SetVariable(Name, decoratedFunction);
            return decoratedFunction;
        }
        
        /// <summary>
        /// CPython 호환: 매개변수와 기본값 분리 (함수 정의 시)
        /// CPython func_defaults 방식 구현
        /// </summary>
        private (List<string> paramNames, List<PyObject> defaults) ParseParametersAndDefaults(List<string> parameters, PyScope scope)
        {
            var paramNames = new List<string>();
            var defaults = new List<PyObject>();

            foreach (var param in parameters)
            {
                string cleanName = param;
                PyObject defaultValue = null;

                // CPython 방식: 매개변수 문자열 파싱
                if (param.Contains("="))
                {
                    // name=value 또는 name:type=value 처리
                    var equalIndex = param.LastIndexOf('=');
                    var nameTypePart = param.Substring(0, equalIndex).Trim();
                    var defaultValueStr = param.Substring(equalIndex + 1).Trim();

                    // *args/**kwargs 처리: *name=value, **name=value
                    if (nameTypePart.StartsWith("**"))
                    {
                        cleanName = nameTypePart.Substring(2).Trim();
                        if (cleanName.Contains(":"))
                        {
                            cleanName = cleanName.Substring(0, cleanName.IndexOf(':')).Trim();
                        }
                        cleanName = "**" + cleanName; // Keep ** prefix for identification
                    }
                    else if (nameTypePart.StartsWith("*"))
                    {
                        cleanName = nameTypePart.Substring(1).Trim();
                        if (cleanName.Contains(":"))
                        {
                            cleanName = cleanName.Substring(0, cleanName.IndexOf(':')).Trim();
                        }
                        cleanName = "*" + cleanName; // Keep * prefix for identification
                    }
                    else
                    {
                        // 타입 주석 제거: name:type -> name
                        if (nameTypePart.Contains(":"))
                        {
                            cleanName = nameTypePart.Substring(0, nameTypePart.IndexOf(':')).Trim();
                        }
                        else
                        {
                            cleanName = nameTypePart;
                        }
                    }

                    // CPython 호환: 기본값을 실행 시점이 아닌 정의 시점에서 평가
                    defaultValue = ParseAndEvaluateDefaultValue(defaultValueStr, scope);
                }
                else if (param.Contains(":"))
                {
                    // 타입 주석만 있는 경우: name:type, *name:type, **name:type
                    if (param.StartsWith("**"))
                    {
                        var nameType = param.Substring(2).Trim();
                        cleanName = nameType.Substring(0, nameType.IndexOf(':')).Trim();
                        cleanName = "**" + cleanName;
                    }
                    else if (param.StartsWith("*"))
                    {
                        var nameType = param.Substring(1).Trim();
                        cleanName = nameType.Substring(0, nameType.IndexOf(':')).Trim();
                        cleanName = "*" + cleanName;
                    }
                    else
                    {
                        cleanName = param.Substring(0, param.IndexOf(':')).Trim();
                    }
                }
                else
                {
                    // 단순 매개변수 이름: name, *name, **name
                    cleanName = param.Trim();
                    // Keep */** prefixes for *args/**kwargs identification
                }

                paramNames.Add(cleanName);
                defaults.Add(defaultValue); // null if no default
            }

            return (paramNames, defaults);
        }
        
        /// <summary>
        /// CPython 호환: 인수를 매개변수에 바인딩 (함수 호출 시)
        /// </summary>
        private void BindArgumentsToParameters(List<string> paramNames, List<PyObject> defaults, PyObject[] args, PyScope funcScope)
        {
            Console.WriteLine($"🔗 [AST] 매개변수 바인딩: {args.Length}개 인수, {paramNames.Count}개 매개변수");
            Console.WriteLine($"  DefaultValues.Count: {defaults.Count}");
            for (int j = 0; j < defaults.Count; j++)
            {
                Console.WriteLine($"    [{j}]: {defaults[j]?.ToString() ?? "null"}");
            }

            var regularParams = new List<(string name, int index)>();
            string starArgsParam = null;
            string kwArgsParam = null;
            var starArgsValues = new List<PyObject>();

            // Separate regular parameters, *args, and **kwargs
            for (int i = 0; i < paramNames.Count; i++)
            {
                var paramName = paramNames[i];
                Console.WriteLine($"  분석중: 매개변수[{i}] = '{paramName}'");

                if (paramName.StartsWith("**"))
                {
                    kwArgsParam = paramName.Substring(2);
                    Console.WriteLine($"    **kwargs 매개변수 발견: '{kwArgsParam}'");
                }
                else if (paramName.StartsWith("*"))
                {
                    starArgsParam = paramName.Substring(1);
                    Console.WriteLine($"    *args 매개변수 발견: '{starArgsParam}'");
                }
                else
                {
                    regularParams.Add((paramName, i));
                    Console.WriteLine($"    일반 매개변수: '{paramName}'");
                }
            }

            // Bind regular positional arguments
            int argIndex = 0;
            for (int i = 0; i < regularParams.Count && argIndex < args.Length; i++)
            {
                var (paramName, paramIndex) = regularParams[i];
                Console.WriteLine($"  위치인수 바인딩: '{paramName}' = {args[argIndex]}");
                funcScope.SetVariable(paramName, args[argIndex]);
                argIndex++;
            }

            // Handle remaining arguments for *args
            if (starArgsParam != null)
            {
                for (int i = argIndex; i < args.Length; i++)
                {
                    starArgsValues.Add(args[i]);
                }
                var argsTuple = new PyTuple(starArgsValues.ToArray());
                Console.WriteLine($"  *args 바인딩: '{starArgsParam}' = {argsTuple}");
                funcScope.SetVariable(starArgsParam, argsTuple);
            }
            else
            {
                // Check for too many arguments when no *args
                if (argIndex < args.Length)
                {
                    throw PyTypeError.Create($"{Name}() takes {regularParams.Count} positional argument{(regularParams.Count != 1 ? "s" : "")} but {args.Length} {(args.Length != 1 ? "were" : "was")} given");
                }
            }

            // Handle default values for unbound regular parameters
            for (int i = argIndex; i < regularParams.Count; i++)
            {
                var (paramName, paramIndex) = regularParams[i];
                if (paramIndex < defaults.Count && defaults[paramIndex] != null)
                {
                    Console.WriteLine($"  기본값 바인딩: '{paramName}' = {defaults[paramIndex]}");
                    funcScope.SetVariable(paramName, defaults[paramIndex]);
                }
                else
                {
                    throw PyTypeError.Create($"missing required argument: '{paramName}'");
                }
            }

            // Handle **kwargs (empty dict for now - keyword args would be handled in compiler)
            if (kwArgsParam != null)
            {
                var kwargsDict = new PyDict();
                Console.WriteLine($"  **kwargs 바인딩: '{kwArgsParam}' = {{}}");
                funcScope.SetVariable(kwArgsParam, kwargsDict);
            }
        }
        
        /// <summary>
        /// CPython 호환: 기본값을 정의 시점에서 파싱하고 평가
        /// CPython은 함수 정의 시점에 기본값을 평가함
        /// </summary>
        private PyObject ParseAndEvaluateDefaultValue(string defaultValueStr, PyScope scope)
        {
            // CPython 방식: 리터럴 우선 처리
            if (int.TryParse(defaultValueStr, out int intValue))
            {
                return new PyInt(intValue);
            }
            
            if (double.TryParse(defaultValueStr, out double floatValue))
            {
                return new PyFloat(floatValue);
            }
            
            // 문자열 리터럴 처리
            if ((defaultValueStr.StartsWith("\"") && defaultValueStr.EndsWith("\"")) ||
                (defaultValueStr.StartsWith("'") && defaultValueStr.EndsWith("'")))
            {
                var content = defaultValueStr.Substring(1, defaultValueStr.Length - 2);
                // 기본적인 이스케이프 처리
                content = content.Replace("\\n", "\n")
                              .Replace("\\t", "\t")
                              .Replace("\\r", "\r")
                              .Replace("\\'", "'")
                              .Replace("\\\"", "\"")
                              .Replace("\\\\", "\\");
                return new PyString(content);
            }
            
            // 불린 리터럴
            if (defaultValueStr == "True")
                return PyBool.True;
            if (defaultValueStr == "False")
                return PyBool.False;
            if (defaultValueStr == "None")
                return PyNone.Instance;
            
            // CPython 방식: 복잡한 표현식은 현재 스코프에서 평가
            try
            {
                // 변수명인 경우 현재 스코프에서 조회
                if (System.Text.RegularExpressions.Regex.IsMatch(defaultValueStr, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                {
                    return scope.GetVariable(defaultValueStr);
                }
                
                // 더 복잡한 표현식은 향후 확장 가능
                // 현재는 단순히 None으로 처리
                return PyNone.Instance;
            }
            catch
            {
                // CPython과 동일: 평가 실패 시 NameError 발생하지만
                // 여기서는 함수 정의를 완료하기 위해 None 사용
                return PyNone.Instance;
            }
        }

        public override string ToString()
        {
            var typeParamStr = TypeParams.Any() ? $"[{string.Join(", ", TypeParams)}]" : "";
            return $"def {Name}{typeParamStr}({string.Join(", ", Parameters)}): ...";
        }
    }

    public class AsyncFunctionDefStatement : Statement
    {
        public override string NodeType => "AsyncFunctionDef";
        public string Name { get; }
        public List<string> Parameters { get; }
        public List<Statement> Body { get; }
        public List<string> TypeParams { get; } // Python 3.12
        
        public AsyncFunctionDefStatement(string name, List<string> parameters, List<Statement> body, List<string>? typeParams = null)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
            TypeParams = typeParams ?? new List<string>();
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Async 함수는 C# Task를 반환하는 함수로 처리
            var asyncFunction = new PyFunction(Name, args =>
            {
                // 비동기 작업이 필요하지만 여기서는 바로 실행
                var funcScope = new PyScope(ScopeType.Local, scope, Name);
                
                for (int i = 0; i < Math.Min(Parameters.Count, args.Length); i++)
                {
                    funcScope.SetVariable(Parameters[i], args[i]);
                }
                
                try
                {
                    PyObject result = PyNone.Instance;
                    foreach (var stmt in Body)
                    {
                        result = stmt.Evaluate(funcScope);
                    }
                    return result;
                }
                catch (PyReturnException ret)
                {
                    return ret.Value;
                }
            });
            
            scope.SetVariable(Name, asyncFunction);
            return asyncFunction;
        }
        
        public override string ToString()
        {
            var typeParamStr = TypeParams.Any() ? $"[{string.Join(", ", TypeParams)}]" : "";
            return $"async def {Name}{typeParamStr}({string.Join(", ", Parameters)}): ...";
        }
    }

    public class ClassDefStatement : Statement
    {
        public override string NodeType => "ClassDef";
        public string Name { get; }
        public List<Expression> Bases { get; }
        public List<Statement> Body { get; }
        public List<string> TypeParams { get; } // Python 3.12
        public Expression? Metaclass { get; } // metaclass= keyword
        
        public ClassDefStatement(string name, List<Expression> bases, List<Statement> body, List<string>? typeParams = null, Expression? metaclass = null)
        {
            Name = name;
            Bases = bases;
            Body = body;
            TypeParams = typeParams ?? new List<string>();
            Metaclass = metaclass;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // 제네릭 클래스도 일반 클래스처럼 실행 (임시 해결책)
            // TODO: 적절한 제네릭 타입 시스템 구현
            if (TypeParams.Any())
            {
                return PyExecutor.ExecuteGenericClass(this, scope);
            }
            
            // 상속 클래스 평가
            var baseTypes = new List<PyType>();
            foreach (var baseExpr in Bases)
            {
                var baseValue = baseExpr.Evaluate(scope);
                if (baseValue is PyType baseType)
                {
                    baseTypes.Add(baseType);
                }
            }
            
            // 클래스 생성
            var classDict = new Dictionary<string, PyObject>();
            var classScope = new PyScope(ScopeType.Local, scope, Name);
            
            // 클래스 별체 실행
            foreach (var stmt in Body)
            {
                var result = stmt.Evaluate(classScope);
                
                // 함수 정의는 클래스 사전에 추가
                if (stmt is FunctionDefStatement funcDef)
                {
                    classDict[funcDef.Name] = result;
                }
            }
            
            var pyClass = new PyClass(Name, baseTypes.ToArray(), classDict);
            scope.SetVariable(Name, pyClass);
            return pyClass;
        }
        
        public override string ToString()
        {
            var typeParamStr = TypeParams.Any() ? $"[{string.Join(", ", TypeParams)}]" : "";
            var baseStr = Bases.Any() ? $"({string.Join(", ", Bases)})" : "";
            var metaclassStr = Metaclass != null ? $", metaclass={Metaclass}" : "";
            return $"class {Name}{typeParamStr}({string.Join(", ", Bases)}{metaclassStr}): ...";
        }
    }

    public class TypeAliasStatement : Statement
    {
        public override string NodeType => "TypeAlias";
        public string Name { get; }
        public Expression Value { get; }
        public List<string> TypeParams { get; } // Python 3.12
        
        public TypeAliasStatement(string name, Expression value, List<string>? typeParams = null)
        {
            Name = name;
            Value = value;
            TypeParams = typeParams ?? new List<string>();
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return PyExecutor.ExecuteTypeAlias(this, scope);
        }
        
        public override string ToString()
        {
            var typeParamStr = TypeParams.Any() ? $"[{string.Join(", ", TypeParams)}]" : "";
            return $"type {Name}{typeParamStr} = {Value}";
        }
    }


    public class WhileStatement : Statement
    {
        public override string NodeType => "While";
        public Expression Test { get; }
        public List<Statement> Body { get; }
        public List<Statement>? ElseClause { get; }
        
        public WhileStatement(Expression test, List<Statement> body, List<Statement>? elseClause = null)
        {
            Test = test;
            Body = body;
            ElseClause = elseClause;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            PyObject result = PyNone.Instance;
            try
            {
                while (Test.Evaluate(scope).ToBool())
                {
                    try
                    {
                        foreach (var stmt in Body)
                        {
                            result = stmt.Evaluate(scope);
                        }
                    }
                    catch (PyContinueException)
                    {
                        continue;
                    }
                }
            }
            catch (PyBreakException)
            {
                // break로 루프 탈출
            }
            return result;
        }
        
        public override string ToString() => $"while {Test}: ...";
    }

    public class ForStatement : Statement
    {
        public override string NodeType => "For";
        public string Target { get; }
        public Expression Iter { get; }
        public List<Statement> Body { get; }
        public List<Statement>? ElseClause { get; }
        
        public ForStatement(string target, Expression iter, List<Statement> body, List<Statement>? elseClause = null)
        {
            Target = target;
            Iter = iter;
            Body = body;
            ElseClause = elseClause;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var iterable = Iter.Evaluate(scope);
            PyObject result = PyNone.Instance;
            
            try
            {
                // 반복 가능 객체에 따른 처리
                if (iterable is PyList list)
                {
                    foreach (var item in list.Items)
                    {
                        scope.SetVariable(Target, item);
                        try
                        {
                            foreach (var stmt in Body)
                            {
                                result = stmt.Evaluate(scope);
                            }
                        }
                        catch (PyContinueException)
                        {
                            continue;
                        }
                    }
                }
                else if (iterable is PyRange range)
                {
                    for (int i = range.Start; i < range.Stop; i += range.Step)
                    {
                        scope.SetVariable(Target, new PyInt(i));
                        try
                        {
                            foreach (var stmt in Body)
                            {
                                result = stmt.Evaluate(scope);
                            }
                        }
                        catch (PyContinueException)
                        {
                            continue;
                        }
                    }
                }
                else if (iterable is PyString str)
                {
                    for (int i = 0; i < str.Value.Length; i++)
                    {
                        scope.SetVariable(Target, new PyString(str.Value[i].ToString()));
                        try
                        {
                            foreach (var stmt in Body)
                            {
                                result = stmt.Evaluate(scope);
                            }
                        }
                        catch (PyContinueException)
                        {
                            continue;
                        }
                    }
                }
            }
            catch (PyBreakException)
            {
                // break로 루프 탈출
            }
            
            return result;
        }
        
        public override string ToString() => $"for {Target} in {Iter}: ...";
    }

    /// <summary>
    /// For statement with tuple unpacking (e.g., for key, value in items:)
    /// </summary>
    public class ForTupleStatement : Statement
    {
        public override string NodeType => "ForTuple";
        public List<string> Targets { get; }
        public Expression Iter { get; }
        public List<Statement> Body { get; }
        public List<Statement>? ElseClause { get; }
        
        public ForTupleStatement(List<string> targets, Expression iter, List<Statement> body, List<Statement>? elseClause = null)
        {
            Targets = targets;
            Iter = iter;
            Body = body;
            ElseClause = elseClause;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var iterable = Iter.Evaluate(scope);
            PyObject result = PyNone.Instance;
            
            try
            {
                // Handle different iterable types with tuple unpacking
                if (iterable is PyList list)
                {
                    foreach (var item in list.Items)
                    {
                        // Unpack the item into target variables
                        UnpackItem(item, scope);
                        try
                        {
                            foreach (var stmt in Body)
                            {
                                result = stmt.Evaluate(scope);
                            }
                        }
                        catch (PyContinueException)
                        {
                            continue;
                        }
                    }
                }
                else if (iterable is PyDict dict)
                {
                    // For dict.items() iteration - assume it returns key-value pairs
                    var items = dict.GetAttribute("items");
                    if (items is PyFunction itemsMethod)
                    {
                        var itemsList = itemsMethod.Call(new PyObject[] {  }, null);
                        if (itemsList is PyList dictItems)
                        {
                            foreach (var item in dictItems.Items)
                            {
                                UnpackItem(item, scope);
                                try
                                {
                                    foreach (var stmt in Body)
                                    {
                                        result = stmt.Evaluate(scope);
                                    }
                                }
                                catch (PyContinueException)
                                {
                                    continue;
                                }
                            }
                        }
                    }
                }
                else
                {
                    throw PyTypeError.Create($"'{iterable.GetTypeName()}' object is not iterable");
                }
            }
            catch (PyBreakException)
            {
                // break로 루프 탈출
            }
            
            // Execute else clause if loop completed normally (no break)
            if (ElseClause != null)
            {
                foreach (var stmt in ElseClause)
                {
                    result = stmt.Evaluate(scope);
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Unpack an item into the target variables
        /// </summary>
        private void UnpackItem(PyObject item, PyScope scope)
        {
            if (item is PyTuple tuple)
            {
                // Unpack tuple elements
                if (tuple.Items.Length != Targets.Count)
                {
                    throw PyValueError.Create($"not enough values to unpack (expected {Targets.Count}, got {tuple.Items.Length})");
                }
                
                for (int i = 0; i < Targets.Count; i++)
                {
                    scope.SetVariable(Targets[i], tuple.Items[i]);
                }
            }
            else if (item is PyList itemList)
            {
                // Unpack list elements
                if (itemList.Items.Length != Targets.Count)
                {
                    throw PyValueError.Create($"not enough values to unpack (expected {Targets.Count}, got {itemList.Items.Length})");
                }
                
                for (int i = 0; i < Targets.Count; i++)
                {
                    scope.SetVariable(Targets[i], itemList.Items[i]);
                }
            }
            else
            {
                // Try to iterate over the item
                throw PyTypeError.Create($"cannot unpack non-sequence {item.GetTypeName()}");
            }
        }
        
        public override string ToString() => $"for {string.Join(", ", Targets)} in {Iter}: ...";
    }

    public class ForComplexStatement : Statement
    {
        public override string NodeType => "ForComplex";
        public Expression Target { get; }
        public Expression Iter { get; }
        public List<Statement> Body { get; }
        public List<Statement>? ElseClause { get; }
        
        public ForComplexStatement(Expression target, Expression iter, List<Statement> body, List<Statement>? elseClause = null)
        {
            Target = target;
            Iter = iter;
            Body = body;
            ElseClause = elseClause;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // This method should not be called in compiled mode
            // The PyVM handles for loop execution through bytecode
            throw new NotImplementedException("ForComplexStatement evaluation should be handled by compiled bytecode");
        }
        
        public override string ToString() => $"for {Target} in {Iter}: ...";
    }

    public class TryStatement : Statement
    {
        public override string NodeType => "Try";
        public List<Statement> Body { get; }
        public List<ExceptHandler> Handlers { get; }
        public List<Statement>? OrElse { get; }
        public List<Statement>? FinalBody { get; }
        
        public TryStatement(List<Statement> body, List<ExceptHandler> handlers, 
                           List<Statement>? orElse = null, List<Statement>? finalBody = null)
        {
            Body = body;
            Handlers = handlers;
            OrElse = orElse;
            FinalBody = finalBody;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            PyObject result = PyNone.Instance;
            bool exceptionHandled = false;
            
            try
            {
                foreach (var stmt in Body)
                {
                    result = stmt.Evaluate(scope);
                }
                
                // If no exception occurred, execute else block if present
                if (OrElse != null)
                {
                    foreach (var stmt in OrElse)
                    {
                        result = stmt.Evaluate(scope);
                    }
                }
            }
            catch (Exception ex)
            {
                // Try to find a matching exception handler
                foreach (var handler in Handlers)
                {
                    if (handler.CanHandle(ex, scope))
                    {
                        // If handler has a name binding, bind the exception to that name
                        if (handler.Name != null)
                        {
                            // Convert to proper Python exception object
                            PyBaseException exceptionObj;
                            if (ex is PythonException pythonException)
                            {
                                exceptionObj = pythonException.PyException;
                            }
                            else
                            {
                                // Convert C# exception to Python exception
                                exceptionObj = ExceptionSystem.FromCSharpException(ex);
                            }
                            
                            scope.SetVariable(handler.Name, exceptionObj);
                        }
                        
                        // Execute the handler body
                        try
                        {
                            result = handler.Evaluate(scope);
                            exceptionHandled = true;
                            break;
                        }
                        catch (Exception handlerException)
                        {
                            // If handler itself raises an exception, that becomes the new exception
                            // Unless it's a control flow exception (return, break, continue)
                            if (handlerException is PyReturnException or PyBreakException or PyContinueException)
                            {
                                throw; // Re-throw control flow exceptions
                            }
                            // Replace original exception with handler exception
                            throw;
                        }
                    }
                }
                
                // If no handler matched, re-throw the exception
                if (!exceptionHandled)
                    throw;
            }
            finally
            {
                if (FinalBody != null)
                {
                    foreach (var stmt in FinalBody)
                    {
                        stmt.Evaluate(scope);
                    }
                }
            }
            
            return result;
        }
        
        public override string ToString() => "try: ...";
    }

    public class WithStatement : Statement
    {
        public override string NodeType => "With";
        public List<WithItem> Items { get; }
        public List<Statement> Body { get; }
        
        public WithStatement(List<WithItem> items, List<Statement> body)
        {
            Items = items;
            Body = body;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // CPython 3.12 호환 with statement 구현
            PyObject result = PyNone.Instance;
            var contextManagers = new List<(PyObject manager, PyObject exitMethod, PyObject? target)>();
            
            try
            {
                // Phase 1: Initialize all context managers and call __enter__
                foreach (var item in Items)
                {
                    var contextManager = item.ContextExpr.Evaluate(scope);
                    
                    // Get __exit__ method (must be callable)
                    var exitMethod = contextManager.GetAttribute("__exit__");
                    if (!exitMethod.IsCallable())
                    {
                        throw PyAttributeError.Create($"'{contextManager.GetTypeName()}' object has no attribute '__exit__'");
                    }
                    
                    // Get __enter__ method (must be callable) 
                    var enterMethod = contextManager.GetAttribute("__enter__");
                    if (!enterMethod.IsCallable())
                    {
                        throw PyAttributeError.Create($"'{contextManager.GetTypeName()}' object has no attribute '__enter__'");
                    }
                    
                    // Call __enter__()
                    var enterResult = enterMethod.Call(new PyObject[] {  }, null);
                    
                    // Bind to target variable if specified (e.g., "as f:")
                    PyObject? target = null;
                    if (item.OptionalVars != null)
                    {
                        if (item.OptionalVars is NameExpression nameExpr)
                        {
                            target = enterResult;
                            scope.SetVariable(nameExpr.Name, enterResult);
                        }
                        else
                        {
                            // Handle complex assignment targets (tuples, etc.)
                            target = item.OptionalVars.Evaluate(scope);
                            // TODO: Implement tuple unpacking for complex targets
                        }
                    }
                    
                    contextManagers.Add((contextManager, exitMethod, target));
                }
                
                // Phase 2: Execute the with block body
                foreach (var stmt in Body)
                {
                    result = stmt.Evaluate(scope);
                }
                
                // Phase 3: Normal exit - call __exit__(None, None, None)
                for (int i = contextManagers.Count - 1; i >= 0; i--)
                {
                    var (manager, exitMethod, target) = contextManagers[i];
                    try
                    {
                        exitMethod.Call(new PyObject[] { PyNone.Instance, PyNone.Instance, PyNone.Instance }, null);
                    }
                    catch (Exception exitEx)
                    {
                        // If __exit__ raises an exception, it propagates
                        throw PyRuntimeError.Create($"Exception in __exit__: {exitEx.Message}");
                    }
                }
                
                return result;
            }
            catch (PythonException ex)
            {
                // Phase 3b: Exception exit - call __exit__(exc_type, exc_value, traceback)
                bool suppressException = false;
                
                for (int i = contextManagers.Count - 1; i >= 0; i--)
                {
                    var (manager, exitMethod, target) = contextManagers[i];
                    try
                    {
                        // CPython calls __exit__(exc_type, exc_value, traceback)
                        // For simplicity, we pass the exception as exc_value
                        var excType = new PyString(ex.PyException.GetTypeName());
                        var excValue = ex.PyException;
                        var traceback = PyNone.Instance; // TODO: implement traceback
                        
                        var exitResult = exitMethod.Call(new PyObject[] { excType, excValue, traceback }, null);
                        
                        // If __exit__ returns True, suppress the exception
                        if (exitResult.PyBoolValue())
                        {
                            suppressException = true;
                        }
                    }
                    catch (Exception exitEx)
                    {
                        // If __exit__ raises an exception, it replaces the original
                        throw PyRuntimeError.Create($"Exception in __exit__: {exitEx.Message}");
                    }
                }
                
                // Re-raise the exception unless suppressed
                if (!suppressException)
                {
                    throw;
                }
                
                return PyNone.Instance;
            }
        }
        
        public override string ToString() => $"with {string.Join(", ", Items)}: ...";
    }

    public class AsyncForStatement : Statement
    {
        public override string NodeType => "AsyncFor";
        public string Target { get; }
        public Expression Iter { get; }
        public List<Statement> Body { get; }
        public List<Statement>? ElseClause { get; }

        public AsyncForStatement(string target, Expression iter, List<Statement> body, List<Statement>? elseClause = null)
        {
            Target = target;
            Iter = iter;
            Body = body;
            ElseClause = elseClause;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            // Async for loop - similar to regular for but with await support
            var asyncIterable = Iter.Evaluate(scope);
            PyObject result = PyNone.Instance;

            try
            {
                // For now, treat as regular for loop
                // TODO: Implement proper async iteration protocol (__aiter__, __anext__)
                if (asyncIterable is PyList list)
                {
                    foreach (var item in list.Items)
                    {
                        scope.SetVariable(Target, item);

                        try
                        {
                            foreach (var stmt in Body)
                            {
                                result = stmt.Evaluate(scope);
                            }
                        }
                        catch (PyBreakException)
                        {
                            return result;
                        }
                        catch (PyContinueException)
                        {
                            continue;
                        }
                    }

                    // Execute else clause if loop completed normally
                    if (ElseClause != null)
                    {
                        foreach (var stmt in ElseClause)
                        {
                            result = stmt.Evaluate(scope);
                        }
                    }
                }

                return result;
            }
            catch (PyBreakException)
            {
                return result;
            }
        }

        public override string ToString() => $"async for {Target} in {Iter}: ...";
    }

    public class AsyncWithStatement : Statement
    {
        public override string NodeType => "AsyncWith";
        public List<WithItem> Items { get; }
        public List<Statement> Body { get; }

        public AsyncWithStatement(List<WithItem> items, List<Statement> body)
        {
            Items = items;
            Body = body;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            // Async with statement - similar to regular with but with await support
            PyObject result = PyNone.Instance;
            var contextManagers = new List<(PyObject manager, PyObject exitMethod, PyObject? target)>();

            try
            {
                // Phase 1: Initialize all async context managers and call __aenter__
                // For now, treat as regular context managers
                // TODO: Implement proper async context manager protocol (__aenter__, __aexit__)
                foreach (var item in Items)
                {
                    var contextManager = item.ContextExpr.Evaluate(scope);

                    // Call __enter__ (should be __aenter__ for async)
                    var enterMethod = contextManager.GetAttribute("__enter__");
                    var enterResult = enterMethod.Call(new PyObject[0], null);

                    // Get __exit__ method (should be __aexit__ for async)
                    var exitMethod = contextManager.GetAttribute("__exit__");

                    // Store context manager info
                    contextManagers.Add((contextManager, exitMethod, null));

                    // Bind to target variable if specified
                    if (item.OptionalVars != null)
                    {
                        if (item.OptionalVars is NameExpression nameExpr)
                        {
                            scope.SetVariable(nameExpr.Name, enterResult);
                        }
                    }
                }

                // Phase 2: Execute body
                foreach (var stmt in Body)
                {
                    result = stmt.Evaluate(scope);
                }

                // Phase 3: Normal exit - call __aexit__(None, None, None)
                for (int i = contextManagers.Count - 1; i >= 0; i--)
                {
                    var (manager, exitMethod, target) = contextManagers[i];
                    try
                    {
                        exitMethod.Call(new PyObject[] { PyNone.Instance, PyNone.Instance, PyNone.Instance }, null);
                    }
                    catch (Exception exitEx)
                    {
                        throw PyRuntimeError.Create($"Exception in async __aexit__: {exitEx.Message}");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                // Phase 3: Exception exit - call __aexit__ with exception info
                bool suppressException = false;

                for (int i = contextManagers.Count - 1; i >= 0; i--)
                {
                    var (manager, exitMethod, target) = contextManagers[i];
                    try
                    {
                        var excType = new PyType(ex.GetType().Name, new PyType[0]);
                        var excValue = new PyString(ex.Message);
                        var traceback = PyNone.Instance;

                        var exitResult = exitMethod.Call(new PyObject[] { excType, excValue, traceback }, null);

                        if (exitResult.PyBoolValue())
                        {
                            suppressException = true;
                        }
                    }
                    catch (Exception exitEx)
                    {
                        throw PyRuntimeError.Create($"Exception in async __aexit__: {exitEx.Message}");
                    }
                }

                if (!suppressException)
                {
                    throw;
                }

                return PyNone.Instance;
            }
        }

        public override string ToString() => $"async with {string.Join(", ", Items)}: ...";
    }

    public class WithItem : ASTNode
    {
        public override string NodeType => "withitem";
        public Expression ContextExpr { get; }
        public Expression? OptionalVars { get; }
        
        public WithItem(Expression contextExpr, Expression? optionalVars = null)
        {
            ContextExpr = contextExpr;
            OptionalVars = optionalVars;
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitNode(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitNode(this);
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return ContextExpr.Evaluate(scope);
        }
        
        public override string ToString() => 
            OptionalVars != null ? $"{ContextExpr} as {OptionalVars}" : ContextExpr.ToString();
    }

    public class MatchStatement : Statement
    {
        public override string NodeType => "Match";
        public Expression Subject { get; }
        public List<MatchCase> Cases { get; }
        
        public MatchStatement(Expression subject, List<MatchCase> cases)
        {
            Subject = subject;
            Cases = cases;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return PyExecutor.ExecuteMatchStatement(this, scope);
        }
        
        public override string ToString() => $"match {Subject}: ...";
    }

    /// <summary>
    /// CPython 3.12 PEP 634: Or pattern (pattern1 | pattern2)
    /// </summary>
    public class OrPattern : Expression
    {
        public override string NodeType => "OrPattern";
        public List<Expression> Patterns { get; }
        
        public OrPattern(List<Expression> patterns)
        {
            Patterns = patterns;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Or pattern is only used in match statements, not as regular expression
            throw new Exception("Or pattern can only be used in match statements");
        }
        
        public override string ToString() => string.Join(" | ", Patterns);
    }

    public class MatchCase : ASTNode
    {
        public override string NodeType => "match_case";
        public Expression Pattern { get; }
        public Expression? Guard { get; }
        public List<Statement> Body { get; }
        
        public MatchCase(Expression pattern, List<Statement> body, Expression? guard = null)
        {
            Pattern = pattern;
            Guard = guard;
            Body = body;
            // Console.WriteLine($"🔍 MatchCase created: Pattern={pattern?.GetType().Name}, Guard={guard?.GetType().Name} ({guard})");
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitNode(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitNode(this);
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // MatchCase는 단독으로 실행되지 않고 MatchStatement에서 처리
            return PyNone.Instance;
        }
        
        public override string ToString() => $"case {Pattern}: ...";
    }

    // Simple statements
    public class BreakStatement : Statement
    {
        public override string NodeType => "Break";
        
        public override PyObject Evaluate(PyScope scope)
        {
            throw new PyBreakException();
        }
        
        public override string ToString() => "break";
    }

    public class ContinueStatement : Statement
    {
        public override string NodeType => "Continue";
        
        public override PyObject Evaluate(PyScope scope)
        {
            throw new PyContinueException();
        }
        
        public override string ToString() => "continue";
    }

    public class PassStatement : Statement
    {
        public override string NodeType => "Pass";

        public override PyObject Evaluate(PyScope scope)
        {
            return PyNone.Instance;
        }

        public override string ToString() => "pass";
    }

    /// <summary>
    /// CPython 3.12 호환을 위한 NOP 명령어를 생성하는 Statement
    /// Dead branch elimination 최적화에서 사용
    /// </summary>
    public class NopStatement : Statement
    {
        public override string NodeType => "Nop";

        public override PyObject Evaluate(PyScope scope)
        {
            return PyNone.Instance;
        }

        public override string ToString() => "# nop";
    }

    public class AssertStatement : Statement
    {
        public override string NodeType => "Assert";
        public Expression Test { get; }
        public Expression? Msg { get; }
        
        public AssertStatement(Expression test, Expression? msg = null)
        {
            Test = test;
            Msg = msg;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var testResult = Test.Evaluate(scope);
            if (!testResult.ToBool())
            {
                var message = Msg?.Evaluate(scope)?.ToStr() ?? "Assertion failed";
                throw new Exception($"AssertionError: {message}");
            }
            return PyNone.Instance;
        }
        
        public override string ToString() => $"assert {Test}" + (Msg != null ? $", {Msg}" : "");
    }

    public class RaiseStatement : Statement
    {
        public override string NodeType => "Raise";
        public Expression? Exc { get; }
        public Expression? Cause { get; }
        
        public RaiseStatement(Expression? exc = null, Expression? cause = null)
        {
            Exc = exc;
            Cause = cause;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            if (Exc != null)
            {
                var exception = Exc.Evaluate(scope);
                
                // Handle different exception types
                if (exception is PyBaseException pyException)
                {
                    throw new PythonException(pyException);
                }
                else if (exception is PyBuiltinType builtinType)
                {
                    // Exception type constructor call (e.g., ValueError("message"))
                    var exceptionInstance = builtinType.Call(new PyObject[] {  }, null);
                    if (exceptionInstance is PyBaseException pyExceptionInstance)
                    {
                        throw new PythonException(pyExceptionInstance);
                    }
                }
                // If it's already evaluated, try direct cast to exception
                else
                {
                    // Try to create a RuntimeError with the object as message
                    throw new PythonException(new PyRuntimeError(exception.ToStr()));
                }
            }
            
            // Bare raise - re-raise current exception (simplified)
            throw new PythonException(new PyRuntimeError("No active exception to re-raise"));
        }
        
        public override string ToString() => "raise" + (Exc != null ? $" {Exc}" : "");
    }

    public class DeleteStatement : Statement
    {
        public override string NodeType => "Delete";
        public List<Expression> Targets { get; }
        
        public DeleteStatement(List<Expression> targets)
        {
            Targets = targets;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // 간단한 del 구현 - 변수 삭제만 지원
            foreach (var target in Targets)
            {
                if (target is NameExpression nameExpr)
                {
                    // Delete variable (간단한 구현)
                    scope.SetVariable(nameExpr.Name, PyNone.Instance);
                }
            }
            return PyNone.Instance;
        }
        
        public override string ToString() => $"del {string.Join(", ", Targets)}";
    }

    public class GlobalStatement : Statement
    {
        public override string NodeType => "Global";
        public List<string> Names { get; }
        
        public GlobalStatement(List<string> names)
        {
            Names = names;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // global 선언 - 실제로는 스코프에서 전역 변수 설정
            foreach (var name in Names)
            {
                // global 선언 - 간단한 구현
                // scope.MarkAsGlobal(name); // 생략
            }
            return PyNone.Instance;
        }
        
        public override string ToString() => $"global {string.Join(", ", Names)}";
    }

    public class NonlocalStatement : Statement
    {
        public override string NodeType => "Nonlocal";
        public List<string> Names { get; }
        
        public NonlocalStatement(List<string> names)
        {
            Names = names;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // nonlocal 선언
            foreach (var name in Names)
            {
                // scope.MarkAsNonlocal(name); // 생략
            }
            return PyNone.Instance;
        }
        
        public override string ToString() => $"nonlocal {string.Join(", ", Names)}";
    }

    public class ImportStatement : Statement
    {
        public override string NodeType => "Import";
        public List<string> Names { get; }
        
        public ImportStatement(List<string> names)
        {
            Names = names;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // 간단한 import 구현
            foreach (var name in Names)
            {
                var module = new PyModule(name); // 간단한 구현
                scope.SetVariable(name, module);
            }
            return PyNone.Instance;
        }
        
        public override string ToString() => $"import {string.Join(", ", Names)}";
    }

    public class ImportFromStatement : Statement
    {
        public override string NodeType => "ImportFrom";
        public string? Module { get; }          // CPython 3.12: None for relative imports
        public List<ImportAlias> Names { get; } // CPython 3.12: support for aliases
        public int Level { get; }               // CPython 3.12: relative import level (0=absolute, 1=., 2=..)

        public ImportFromStatement(string? module, List<ImportAlias> names, int level = 0)
        {
            Module = module;
            Names = names;
            Level = level;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            Console.WriteLine($"📦 ImportFromStatement.Evaluate: from {Module ?? "."} import {string.Join(", ", Names.Select(n => n.ToString()))} (level={Level})");

            try
            {
                // Use existing PyImportSystem for CPython 3.12 compatibility
                string moduleName = ConstructModuleName(Module, Level, scope);

                // Extract item names for PyImportSystem.FromImport
                var itemNames = Names.Select(alias => alias.Name).ToArray();

                // Use PyImportSystem.FromImport which handles modules, caching, relative imports
                var importedItems = PyImportSystem.FromImport(moduleName, itemNames);

                // Set variables in scope with proper aliases
                foreach (var alias in Names)
                {
                    if (importedItems.TryGetValue(alias.Name, out var value))
                    {
                        var localName = alias.AsName ?? alias.Name;
                        scope.SetVariable(localName, value);
                        Console.WriteLine($"📥 Imported {alias.Name} as {localName}");
                    }
                    else
                    {
                        throw PyImportError.Create($"cannot import name '{alias.Name}' from '{moduleName}'");
                    }
                }

                return PyNone.Instance;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ImportFromStatement failed: {ex.Message}");
                if (ex is PythonException) throw; // Re-throw Python exceptions as-is
                throw PyImportError.Create($"cannot import name '{string.Join(", ", Names.Select(n => n.Name))}' from '{Module ?? "."}'");
            }
        }

        private string ConstructModuleName(string? module, int level, PyScope scope)
        {
            if (level == 0)
            {
                // Absolute import
                return module ?? "";
            }

            // Relative import - construct dotted name
            var prefix = new string('.', level);
            return prefix + (module ?? "");
        }

        public override string ToString() => $"from {Module ?? "."} import {string.Join(", ", Names)}";
    }


    #endregion

    #region Expression Nodes

    /// <summary>
    /// 표현식 노드들의 베이스 클래스
    /// </summary>
    public abstract class Expression : ASTNode 
    {
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitExpression(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitExpression(this);
        }
    }

    public class ConstantExpression : Expression
    {
        public override string NodeType => "Constant";
        public PyObject Value { get; }
        
        public ConstantExpression(PyObject value)
        {
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return Value;
        }
        
        public override string ToString() => Value.ToString();
    }

    /// <summary>
    /// Walrus 연산자 표현식 (n := value)
    /// </summary>
    public class WalrusExpression : Expression
    {
        public override string NodeType => "NamedExpr";
        public string Target { get; }
        public Expression Value { get; }
        
        public WalrusExpression(string target, Expression value)
        {
            Target = target;
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // 표현식을 평가하고 변수에 할당
            var result = Value.Evaluate(scope);
            scope.SetVariable(Target, result);
            return result; // walrus operator returns the assigned value
        }
        
        public override string ToString() => $"({Target} := {Value})";
    }

    public class NameExpression : Expression
    {
        public override string NodeType => "Name";
        public string Name { get; }
        public ExprContext? Ctx { get; set; }  // CPython 3.12: Store, Load, Del

        public NameExpression(string name, ExprContext? ctx = null)
        {
            Name = name;
            Ctx = ctx ?? Load.Instance;  // 기본값: Load context
        }

        public override PyObject Evaluate(PyScope scope)
        {
            return scope.GetVariable(Name);
        }

        public override string ToString() => Ctx != null ? $"Name(id='{Name}', ctx={Ctx})" : Name;
    }


    public class UnaryOpExpression : Expression
    {
        public override string NodeType => "UnaryOp";
        public UnaryOperator OpNode { get; }  // CPython 3.12: UAdd, USub, Not, Invert
        public Expression Operand { get; }

        public UnaryOpExpression(UnaryOperator op, Expression operand)
        {
            OpNode = op ?? throw new ArgumentNullException(nameof(op));
            Operand = operand;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var operand = Operand.Evaluate(scope);
            return OpNode.Apply(operand);
        }

        public override string ToString() => $"UnaryOp(op={OpNode}, operand={Operand})";
    }

    /// <summary>
    /// CPython 3.12: Binary Operation Expression
    /// BinOp(left=..., op=Add(), right=...)
    /// </summary>
    public class BinOpExpression : Expression
    {
        public override string NodeType => "BinOp";
        public Expression Left { get; }
        public BinaryOperator OpNode { get; }  // CPython 3.12: Add, Sub, Mult, etc.
        public Expression Right { get; }

        public BinOpExpression(Expression left, BinaryOperator op, Expression right)
        {
            Left = left;
            OpNode = op ?? throw new ArgumentNullException(nameof(op));
            Right = right;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var left = Left.Evaluate(scope);
            var right = Right.Evaluate(scope);
            return OpNode.Apply(left, right);
        }

        public override string ToString() => $"BinOp(left={Left}, op={OpNode}, right={Right})";
    }

    // CompareExpression moved to CompareExpressions.cs

    public class BoolOpExpression : Expression
    {
        public override string NodeType => "BoolOp";
        public BoolOperator OpNode { get; }
        public List<Expression> Values { get; }

        public BoolOpExpression(BoolOperator op, List<Expression> values)
        {
            OpNode = op ?? throw new ArgumentNullException(nameof(op));
            Values = values;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            // Evaluate all values first
            var evaluatedValues = Values.Select(v => v.Evaluate(scope)).ToList();
            return OpNode.Apply(evaluatedValues);
        }

        public override string ToString() => $"BoolOp(op={OpNode}, values=[{string.Join(", ", Values)}])";
    }

    public class JoinedStrExpression : Expression
    {
        public override string NodeType => "JoinedStr";
        public List<Expression> Values { get; }

        public JoinedStrExpression(List<Expression> values)
        {
            Values = values ?? new List<Expression>();
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var result = new StringBuilder();

            foreach (var value in Values)
            {
                var evaluated = value.Evaluate(scope);

                if (evaluated is PyString pyStr)
                {
                    result.Append(pyStr.Value);
                }
                else
                {
                    // For FormattedValue expressions, call their string representation
                    result.Append(evaluated.ToString());
                }
            }

            return new PyString(result.ToString());
        }

        public override string ToString() => $"f\"{string.Join("", Values)}\"";
    }

    public class FormattedValueExpression : Expression
    {
        public override string NodeType => "FormattedValue";
        public Expression Value { get; }
        public int Conversion { get; } // -1: no conversion, 114: !r, 115: !s, 97: !a
        public Expression? FormatSpec { get; }

        public FormattedValueExpression(Expression value, int conversion = -1, Expression? formatSpec = null)
        {
            Value = value;
            Conversion = conversion;
            FormatSpec = formatSpec;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var value = Value.Evaluate(scope);

            // Apply conversion if specified
            if (Conversion == 114) // !r (repr)
            {
                value = new PyString($"'{value}'");
            }
            else if (Conversion == 115) // !s (str)
            {
                value = new PyString(value.ToString());
            }
            else if (Conversion == 97) // !a (ascii)
            {
                var str = value.ToString();
                var escaped = str.Replace("\\", "\\\\").Replace("'", "\\'");
                value = new PyString($"'{escaped}'");
            }

            // Apply format specification if present
            if (FormatSpec != null)
            {
                var formatSpec = FormatSpec.Evaluate(scope);
                // TODO: Implement full format specification support
                // For now, just use default string representation
            }

            return value;
        }

        public override string ToString() =>
            $"{{{Value}{(Conversion != -1 ? $"!{(char)Conversion}" : "")}{(FormatSpec != null ? $":{FormatSpec}" : "")}}}";
    }

    /// <summary>
    /// CPython 호환: 키워드 인수를 표현하는 AST 노드 (keyword)
    /// </summary>
    public class KeywordExpression : Expression
    {
        public override string NodeType => "keyword";
        public string? Arg { get; }  // null for **kwargs
        public Expression Value { get; }
        
        public KeywordExpression(string? arg, Expression value)
        {
            Arg = arg;
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return Value.Evaluate(scope);
        }
        
        public override string ToString() => Arg != null ? $"{Arg}={Value}" : $"**{Value}";
    }
    
    public class CallExpression : Expression
    {
        public override string NodeType => "Call";
        public Expression Function { get; }
        public List<Expression> Arguments { get; }
        public List<KeywordExpression> Keywords { get; }  // CPython 호환: 키워드 인수 분리
        
        public CallExpression(Expression function, List<Expression> arguments, List<KeywordExpression> keywords = null)
        {
            Function = function;
            Arguments = arguments;
            Keywords = keywords ?? new List<KeywordExpression>();
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var function = Function.Evaluate(scope);
            var args = Arguments.Select(arg => arg.Evaluate(scope)).ToArray();
            
            // 키워드 인수가 있는 경우 처리 (향후 구현)
            if (Keywords.Any())
            {
                // TODO: 키워드 인수 처리 로직 구현
                throw new NotImplementedException("Keyword arguments not yet implemented");
            }
            
            return function.Call(args, null);
        }
        
        public override string ToString() 
        {
            var allArgs = Arguments.Select(a => a.ToString())
                .Concat(Keywords.Select(k => k.ToString()));
            return $"{Function}({string.Join(", ", allArgs)})";
        }
    }

    public class AttributeExpression : Expression
    {
        public override string NodeType => "Attribute";
        public Expression Value { get; }
        public string Attr { get; }
        
        public AttributeExpression(Expression value, string attr)
        {
            Value = value;
            Attr = attr;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var obj = Value.Evaluate(scope);
            return obj.GetAttribute(Attr);
        }
        
        public override string ToString() => $"{Value}.{Attr}";
    }

    public class SubscriptExpression : Expression
    {
        public override string NodeType => "Subscript";
        public Expression Value { get; }
        public Expression Slice { get; }
        
        public SubscriptExpression(Expression value, Expression slice)
        {
            Value = value;
            Slice = slice;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var obj = Value.Evaluate(scope);
            var index = Slice.Evaluate(scope);
            
            // GetItem 대신 기존 메서드 사용
            if (obj is PyDict dict)
                return dict.GetItem(index);
            else if (obj is PyList list && index is PyInt intIndex)
                return list.Items[intIndex.Value]; // 간단한 구현
            else
                return PyNone.Instance;
        }
        
        public override string ToString() => $"{Value}[{Slice}]";
    }

    // Starred expression: *variable (for unpacking)
    public class StarExpression : Expression
    {
        public override string NodeType => "Starred";
        public Expression Value { get; }
        
        public StarExpression(Expression value)
        {
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // StarExpression는 평가되지 않고 unpacking 컨텍스트에서만 사용됨
            throw new Exception("StarExpression cannot be evaluated directly");
        }
        
        public override string ToString() => $"*{Value}";
    }

    // Container expressions
    public class ListExpression : Expression
    {
        public override string NodeType => "List";
        public List<Expression> Elements { get; }
        
        public ListExpression(List<Expression> elements)
        {
            Elements = elements;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var list = new PyList();
            foreach (var element in Elements)
            {
                list.Add(element.Evaluate(scope));
            }
            return list;
        }
        
        public override string ToString() => $"[{string.Join(", ", Elements)}]";
    }

    public class TupleExpression : Expression
    {
        public override string NodeType => "Tuple";
        public List<Expression> Elements { get; }
        
        public TupleExpression(List<Expression> elements)
        {
            Elements = elements;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var items = Elements.Select(element => element.Evaluate(scope)).ToArray();
            return new PyTuple(items);
        }
        
        public override string ToString() => $"({string.Join(", ", Elements)})";
    }

    public class SetExpression : Expression
    {
        public override string NodeType => "Set";
        public List<Expression> Elements { get; }
        
        public SetExpression(List<Expression> elements)
        {
            Elements = elements;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var set = new PySet();
            foreach (var element in Elements)
            {
                set.Add(element.Evaluate(scope));
            }
            return set;
        }
        
        public override string ToString() => $"{{{string.Join(", ", Elements)}}}";
    }

    public class DictExpression : Expression
    {
        public override string NodeType => "Dict";
        public List<(Expression Key, Expression Value)> Items { get; }
        
        public DictExpression(List<(Expression Key, Expression Value)> items)
        {
            Items = items;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var dict = new PyDict();
            foreach (var (key, value) in Items)
            {
                var keyObj = key.Evaluate(scope);
                var valueObj = value.Evaluate(scope);
                dict.SetItem(keyObj, valueObj);
            }
            return dict;
        }
        
        public override string ToString() => $"{{{string.Join(", ", Items.Select(i => $"{i.Key}: {i.Value}"))}}}";
    }

    // Advanced expressions
    public class LambdaExpression : Expression
    {
        public override string NodeType => "Lambda";
        public List<string> Args { get; }
        public Expression Body { get; }
        public List<PyObject> Defaults { get; } // CPython 3.12: Lambda default values
        
        public LambdaExpression(List<string> args, Expression body)
        {
            Args = args;
            Body = body;
            Defaults = new List<PyObject>();
        }
        
        // CPython 호환: 기본값이 있는 lambda 생성자 
        public LambdaExpression(List<string> args, Expression body, List<PyObject> defaults)
        {
            Args = args;
            Body = body;
            Defaults = defaults ?? new List<PyObject>();
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // CPython 3.12: Lambda 함수를 기본값 지원으로 생성 
            var cleanArgNames = new List<string>();
            var evaluatedDefaults = new List<PyObject>();
            
            // CPython 방식: 매개변수와 기본값 분리 처리
            for (int i = 0; i < Args.Count; i++)
            {
                var param = Args[i];
                if (param.Contains("="))
                {
                    // 기본값이 있는 매개변수: name=defaultValue
                    var parts = param.Split('=', 2);
                    var paramName = parts[0].Trim();
                    var defaultValueStr = parts[1].Trim();
                    
                    cleanArgNames.Add(paramName);
                    
                    // CPython 호환: 기본값을 정의 시점에서 평가
                    var defaultValue = ParseAndEvaluateDefaultValue(defaultValueStr, scope);
                    evaluatedDefaults.Add(defaultValue);
                }
                else
                {
                    // 기본값이 없는 매개변수
                    cleanArgNames.Add(param.Trim());
                }
            }
            
            // Lambda 함수 생성 (CPython과 동일한 기본값 처리)
            return new PyFunction("<lambda>", providedArgs =>
            {
                var lambdaScope = new PyScope(ScopeType.Local, scope, "<lambda>");
                
                // CPython 호환: 매개변수 바인딩 (기본값 포함)
                int defaultStartIndex = cleanArgNames.Count - evaluatedDefaults.Count;
                
                for (int i = 0; i < cleanArgNames.Count; i++)
                {
                    var paramName = cleanArgNames[i];
                    
                    if (i < providedArgs.Length)
                    {
                        // 제공된 인수가 있는 경우
                        lambdaScope.SetVariable(paramName, providedArgs[i]);
                    }
                    else if (i >= defaultStartIndex)
                    {
                        // 기본값이 있는 매개변수
                        int defaultIndex = i - defaultStartIndex;
                        if (defaultIndex < evaluatedDefaults.Count)
                        {
                            lambdaScope.SetVariable(paramName, evaluatedDefaults[defaultIndex]);
                        }
                        else
                        {
                            throw PyTypeError.Create($"<lambda>() missing required argument: '{paramName}'");
                        }
                    }
                    else
                    {
                        // 필수 매개변수가 누락됨
                        throw PyTypeError.Create($"<lambda>() missing required argument: '{paramName}'");
                    }
                }
                
                return Body.Evaluate(lambdaScope);
            });
        }
        
        public override string ToString() => $"lambda {string.Join(", ", Args)}: {Body}";
        
        /// <summary>
        /// CPython 호환: Lambda 기본값을 정의 시점에서 파싱하고 평가
        /// </summary>
        private PyObject ParseAndEvaluateDefaultValue(string defaultValueStr, PyScope scope)
        {
            // CPython 방식: 리터럴 우선 처리
            if (int.TryParse(defaultValueStr, out int intValue))
            {
                return new PyInt(intValue);
            }
            
            if (double.TryParse(defaultValueStr, out double floatValue))
            {
                return new PyFloat(floatValue);
            }
            
            // 문자열 리터럴 처리
            if ((defaultValueStr.StartsWith("\"") && defaultValueStr.EndsWith("\"")) ||
                (defaultValueStr.StartsWith("'") && defaultValueStr.EndsWith("'")))
            {
                var content = defaultValueStr.Substring(1, defaultValueStr.Length - 2);
                // 기본적인 이스케이프 처리
                content = content.Replace("\\n", "\n")
                              .Replace("\\t", "\t")
                              .Replace("\\r", "\r")
                              .Replace("\\'", "'")
                              .Replace("\\\"", "\"")
                              .Replace("\\\\", "\\");
                return new PyString(content);
            }
            
            // 불린 리터럴
            if (defaultValueStr == "True")
                return PyBool.True;
            if (defaultValueStr == "False")
                return PyBool.False;
            if (defaultValueStr == "None")
                return PyNone.Instance;
                
            // CPython 호환: 변수 참조나 복잡한 표현식은 현재 스코프에서 평가
            try
            {
                // 간단한 변수 참조 처리
                if (scope.HasVariable(defaultValueStr))
                {
                    return scope.GetVariable(defaultValueStr);
                }
            }
            catch
            {
                // 평가 실패 시 문자열로 처리 (안전장치)
            }
            
            // 기본값: 문자열로 처리
            return new PyString(defaultValueStr);
        }
    }

    public class ConditionalExpression : Expression
    {
        public override string NodeType => "IfExp";
        public Expression Body { get; }
        public Expression Test { get; }
        public Expression OrElse { get; }
        
        public ConditionalExpression(Expression body, Expression test, Expression orElse)
        {
            Body = body;
            Test = test;
            OrElse = orElse;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var testResult = Test.Evaluate(scope);
            if (testResult.ToBool())
            {
                return Body.Evaluate(scope);
            }
            else
            {
                return OrElse.Evaluate(scope);
            }
        }
        
        public override string ToString() => $"{Body} if {Test} else {OrElse}";
    }

    public class AwaitExpression : Expression
    {
        public override string NodeType => "Await";
        public Expression Value { get; }
        
        public AwaitExpression(Expression value)
        {
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // 간단한 await 구현 - C# Task를 곧바로 실행
            var coroutine = Value.Evaluate(scope);
            // 실제로는 비동기 처리가 필요하지만 여기서는 바로 반환
            return coroutine;
        }
        
        public override string ToString() => $"await {Value}";
    }

    public class StarredExpression : Expression
    {
        public override string NodeType => "Starred";
        public Expression Value { get; }
        
        public StarredExpression(Expression value)
        {
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Starred expression - 주로 unpacking에서 사용
            var value = Value.Evaluate(scope);
            // 실제로는 unpacking 로직이 필요하지만 여기서는 그냥 반환
            return value;
        }
        
        public override string ToString() => $"*{Value}";
    }

    public class NamedExpression : Expression
    {
        public override string NodeType => "NamedExpr";
        public Expression Target { get; }
        public Expression Value { get; }

        public NamedExpression(Expression target, Expression value)
        {
            Target = target;
            Value = value;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            // Walrus operator (:=) - assigns and returns the value
            var result = Value.Evaluate(scope);

            // Assign to target
            if (Target is NameExpression nameExpr)
            {
                scope.SetVariable(nameExpr.Name, result);
            }

            return result;
        }

        public override string ToString() => $"({Target} := {Value})";
    }

    public class FStringExpression : Expression
    {
        public override string NodeType => "JoinedStr";
        public List<Expression> Values { get; }
        
        public FStringExpression(List<Expression> values)
        {
            Values = values;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return PyExecutor.ExecuteFString(this, scope);
        }
        
        public override string ToString() => $"f\"{string.Join("", Values)}\"";
    }

    /// <summary>
    /// CPython 3.12 compatible f-string formatted value
    /// Generates FORMAT_VALUE bytecode instruction
    /// </summary>
    public class FStringFormattedValue : Expression
    {
        public override string NodeType => "FormattedValue";
        public Expression Value { get; }
        public int? Conversion { get; } // -1=no conversion, 115='s', 114='r', 97='a'
        public Expression FormatSpec { get; } // None or expression

        public FStringFormattedValue(Expression value, int? conversion, Expression formatSpec)
        {
            Value = value;
            Conversion = conversion;
            FormatSpec = formatSpec;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            return PyExecutor.ExecuteFormattedValue(this, scope);
        }

        public override string ToString() => $"{{FormattedValue {Value}}}";

        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitExpression(this);
        }

        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitExpression(this);
        }
    }

    /// <summary>
    /// f-string 내의 포맷 지정자가 있는 표현식 (예: {value:.2f})
    /// </summary>
    public class FormatExpression : Expression
    {
        public override string NodeType => "FormattedValue";
        public Expression Value { get; }
        public string FormatSpec { get; }

        public FormatExpression(Expression value, string formatSpec)
        {
            Value = value;
            FormatSpec = formatSpec;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            return PyExecutor.ExecuteFormattedValue(this, scope);
        }

        public override string ToString() => $"{Value}:{FormatSpec}";
    }

    /// <summary>
    /// CPython 3.12 호환: 중첩된 표현식을 포함한 포맷 지시자
    /// f"{value:{width}.{precision}f}" → formatSpec이 표현식이 됨
    /// </summary>
    public class FormatExpressionWithSpec : Expression
    {
        public override string NodeType => "FormattedValueWithSpec";
        public Expression Value { get; }
        public Expression FormatSpec { get; }

        public FormatExpressionWithSpec(Expression value, Expression formatSpec)
        {
            Value = value;
            FormatSpec = formatSpec;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            // 값과 포맷 지시자를 각각 평가
            var valueObj = Value.Evaluate(scope);
            var formatSpecObj = FormatSpec.Evaluate(scope);

            // 포맷 지시자를 문자열로 변환
            var formatStr = formatSpecObj.ToStr();

            // 간단한 포맷팅 적용
            return ApplySimpleFormatting(valueObj, formatStr);
        }

        private static PyObject ApplySimpleFormatting(PyObject obj, string formatSpec)
        {
            try
            {
                // 숫자 포매팅 지원
                if (obj is PyFloat floatObj)
                {
                    if (formatSpec.EndsWith("f"))
                    {
                        // 소수점 자릿수 지정 (예: .2f)
                        if (formatSpec.StartsWith(".") && formatSpec.Length > 2)
                        {
                            var digits = formatSpec.Substring(1, formatSpec.Length - 2);
                            if (int.TryParse(digits, out int decimalPlaces))
                            {
                                var formatted = floatObj.Value.ToString($"F{decimalPlaces}");
                                return new PyString(formatted);
                            }
                        }
                        else if (formatSpec == "f")
                        {
                            return new PyString(floatObj.Value.ToString("F6"));
                        }
                    }
                }
                else if (obj is PyInt intObj)
                {
                    if (formatSpec == "d" || string.IsNullOrEmpty(formatSpec))
                    {
                        return new PyString(intObj.Value.ToString());
                    }
                }

                // 기본 문자열 변환
                return new PyString(obj.ToStr());
            }
            catch
            {
                // 포맷팅 실패 시 기본 문자열 반환
                return new PyString(obj.ToStr());
            }
        }

        public override string ToString() => $"{Value}:{FormatSpec}";
    }

    /// <summary>
    /// f-string 표현식 내의 포맷 값 (예: {value:format})
    /// </summary>
    public class FormattedValue : Expression
    {
        public override string NodeType => "FormattedValue";
        public Expression Value { get; }
        public string? FormatSpec { get; }
        
        public FormattedValue(Expression value, string? formatSpec = null)
        {
            Value = value;
            FormatSpec = formatSpec;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var obj = Value.Evaluate(scope);
            
            // 포맷 지정자가 있으면 적용
            if (!string.IsNullOrEmpty(FormatSpec))
            {
                return ApplyFormat(obj, FormatSpec);
            }
            
            return obj;
        }
        
        /// <summary>
        /// Python 스타일 포매팅 적용
        /// </summary>
        private PyObject ApplyFormat(PyObject obj, string formatSpec)
        {
            try
            {
                // 숫자 포매팅 지원
                if (obj is PyFloat floatObj)
                {
                    if (formatSpec.EndsWith("f"))
                    {
                        // 소수점 자릿수 지정 (예: .2f)
                        if (formatSpec.StartsWith(".") && formatSpec.Length > 2)
                        {
                            var digits = formatSpec.Substring(1, formatSpec.Length - 2);
                            if (int.TryParse(digits, out int decimalPlaces))
                            {
                                var formatted = floatObj.Value.ToString($"F{decimalPlaces}");
                                return new PyString(formatted);
                            }
                        }
                        else if (formatSpec == "f")
                        {
                            return new PyString(floatObj.Value.ToString("F"));
                        }
                    }
                    else if (formatSpec.EndsWith("e"))
                    {
                        return new PyString(floatObj.Value.ToString("E"));
                    }
                    else if (formatSpec.EndsWith("%"))
                    {
                        return new PyString((floatObj.Value * 100).ToString("F") + "%");
                    }
                }
                else if (obj is PyInt intObj)
                {
                    if (formatSpec == "d")
                    {
                        return new PyString(intObj.Value.ToString());
                    }
                    else if (formatSpec == "x")
                    {
                        return new PyString(intObj.Value.ToString("x"));
                    }
                    else if (formatSpec == "X")
                    {
                        return new PyString(intObj.Value.ToString("X"));
                    }
                    else if (formatSpec == "o")
                    {
                        return new PyString(Convert.ToString(intObj.Value, 8));
                    }
                    else if (formatSpec == "b")
                    {
                        return new PyString(Convert.ToString(intObj.Value, 2));
                    }
                }
                
                // 문자열 정렬 지원 (예: >10, <10, ^10)
                if (formatSpec.Length > 0)
                {
                    var align = formatSpec[0];
                    var remaining = formatSpec.Substring(1);
                    
                    if ((align == '<' || align == '>' || align == '^') && int.TryParse(remaining, out int width))
                    {
                        var str = obj.ToStr();
                        switch (align)
                        {
                            case '<': return new PyString(str.PadRight(width));
                            case '>': return new PyString(str.PadLeft(width));
                            case '^': 
                                var totalPadding = width - str.Length;
                                var leftPadding = totalPadding / 2;
                                var rightPadding = totalPadding - leftPadding;
                                return new PyString(new string(' ', leftPadding) + str + new string(' ', rightPadding));
                        }
                    }
                }
            }
            catch
            {
                // 포매팅 실패 시 원본 값 반환
            }
            
            return obj;
        }
        
        public override string ToString() => FormatSpec != null ? $"{{{Value}:{FormatSpec}}}" : $"{{{Value}}}";
    }

    // Comprehension expressions
    public class ListComprehension : Expression
    {
        public override string NodeType => "ListComp";
        public Expression Element { get; }
        public List<Comprehension> Generators { get; }
        
        public ListComprehension(Expression element, List<Comprehension> generators)
        {
            Element = element;
            Generators = generators;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return PyExecutor.ExecuteListComprehension(this, scope);
        }
        
        public override string ToString() => $"[{Element} for ...]";
    }

    public class DictComprehension : Expression
    {
        public override string NodeType => "DictComp";
        public Expression Key { get; }
        public Expression Value { get; }
        public List<Comprehension> Generators { get; }
        
        public DictComprehension(Expression key, Expression value, List<Comprehension> generators)
        {
            Key = key;
            Value = value;
            Generators = generators;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return PyExecutor.ExecuteDictComprehension(this, scope);
        }
        
        public override string ToString() => $"{{{Key}: {Value} for ...}}";
    }

    public class SetComprehension : Expression
    {
        public override string NodeType => "SetComp";
        public Expression Element { get; }
        public List<Comprehension> Generators { get; }
        
        public SetComprehension(Expression element, List<Comprehension> generators)
        {
            Element = element;
            Generators = generators;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return PyExecutor.ExecuteSetComprehension(this, scope);
        }
        
        public override string ToString() => $"{{{Element} for ...}}";
    }

    public class GeneratorExpression : Expression
    {
        public override string NodeType => "GeneratorExp";
        public Expression Element { get; }
        public List<Comprehension> Generators { get; }
        
        public GeneratorExpression(Expression element, List<Comprehension> generators)
        {
            Element = element;
            Generators = generators;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return PyExecutor.ExecuteGeneratorExpression(this, scope);
        }
        
        public override string ToString() => $"({Element} for ...)";
    }

    public class Comprehension : ASTNode
    {
        public override string NodeType => "comprehension";
        public Expression Target { get; }
        public Expression Iter { get; }
        public List<Expression> Ifs { get; }
        
        public Comprehension(Expression target, Expression iter, List<Expression>? ifs = null)
        {
            Target = target;
            Iter = iter;
            Ifs = ifs ?? new List<Expression>();
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitNode(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitNode(this);
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Comprehension은 단독으로 실행되지 않거나 다른 comprehension expression에서 처리
            return PyNone.Instance;
        }
        
        public override string ToString() => $"for {Target} in {Iter}";
    }

    // Python 3.12 Type Parameter expressions
    public class TypeVarExpression : Expression
    {
        public override string NodeType => "TypeVar";
        public string Name { get; }
        public Expression? Bound { get; }
        
        public TypeVarExpression(string name, Expression? bound = null)
        {
            Name = name;
            Bound = bound;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // TypeVar는 타입 매개변수를 나타냄 - C# 에서는 문자열로 처리
            return new PyString(Name);
        }
        
        public override string ToString() => Bound != null ? $"{Name}: {Bound}" : Name;
    }

    public class ParamSpecExpression : Expression
    {
        public override string NodeType => "ParamSpec";
        public string Name { get; }
        
        public ParamSpecExpression(string name)
        {
            Name = name;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return new PyString($"**{Name}");
        }
        
        public override string ToString() => $"**{Name}";
    }

    public class TypeVarTupleExpression : Expression
    {
        public override string NodeType => "TypeVarTuple";
        public string Name { get; }
        
        public TypeVarTupleExpression(string name)
        {
            Name = name;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return new PyString($"*{Name}");
        }
        
        public override string ToString() => $"*{Name}";
    }

    public class SliceExpression : Expression
    {
        public override string NodeType => "Slice";
        public Expression? Start { get; }
        public Expression? Stop { get; }
        public Expression? Step { get; }
        
        public SliceExpression(Expression? start, Expression? stop, Expression? step = null)
        {
            Start = start;
            Stop = stop;
            Step = step;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            var startObj = Start?.Evaluate(scope);
            var stopObj = Stop?.Evaluate(scope);
            var stepObj = Step?.Evaluate(scope);
            
            return new PySlice(startObj, stopObj, stepObj);
        }
        
        public override string ToString()
        {
            var startStr = Start?.ToString() ?? "";
            var stopStr = Stop?.ToString() ?? "";
            var stepStr = Step != null ? $":{Step}" : "";
            return $"{startStr}:{stopStr}{stepStr}";
        }
    }

    #endregion

    #region Python 3.12 Enhanced AST Nodes

    /// <summary>
    /// Exception Groups (PEP 654) - except* syntax
    /// </summary>
    public class TryStarStatement : Statement
    {
        public override string NodeType => "TryStar";
        public List<Statement> Body { get; }
        public List<ExceptStarHandler> Handlers { get; }
        public List<Statement>? OrElse { get; }
        public List<Statement>? FinalBody { get; }
        
        public TryStarStatement(List<Statement> body, List<ExceptStarHandler> handlers, 
                               List<Statement>? orElse = null, List<Statement>? finalBody = null)
        {
            Body = body;
            Handlers = handlers;
            OrElse = orElse;
            FinalBody = finalBody;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Implementation for exception group handling
            try
            {
                foreach (var stmt in Body)
                    stmt.Evaluate(scope);
            }
            catch (Exception ex)
            {
                // Handle exception groups with except* handlers
                foreach (var handler in Handlers)
                {
                    if (handler.CanHandle(ex))
                    {
                        foreach (var stmt in handler.Body)
                            stmt.Evaluate(scope);
                        break;
                    }
                }
            }
            finally
            {
                if (FinalBody != null)
                {
                    foreach (var stmt in FinalBody)
                        stmt.Evaluate(scope);
                }
            }
            
            return PyNone.Instance;
        }
    }
    
    /// <summary>
    /// Regular exception handler for except syntax
    /// </summary>
    public class ExceptHandler : ASTNode
    {
        public override string NodeType => "ExceptHandler";
        public Expression? Type { get; }
        public string? Name { get; }
        public List<Statement> Body { get; }
        public bool IsStar { get; }  // true for except* handlers (PEP 654)
        
        public ExceptHandler(Expression? type, string? name, List<Statement> body, bool isStar = false)
        {
            Type = type;
            Name = name;
            Body = body;
            IsStar = isStar;
        }
        
        public bool CanHandle(Exception exception, PyScope scope)
        {
            // Handle catch-all except clause (no type specified)
            if (Type == null)
            {
                return true;
            }
            
            try
            {
                // Evaluate the exception type expression
                var expectedExceptionType = Type.Evaluate(scope);
                
                // Extract the Python exception from the C# exception
                PyBaseException pyException;
                if (exception is PythonException pythonException)
                {
                    pyException = pythonException.PyException;
                }
                else
                {
                    // Convert C# exception to Python exception
                    pyException = ExceptionSystem.FromCSharpException(exception);
                }
                
                // Check if the Python exception matches the expected type
                if (expectedExceptionType is PyType expectedType)
                {
                    return pyException.GetPyType().IsSubclassOf(expectedType) || 
                           pyException.GetPyType() == expectedType;
                }
                else if (expectedExceptionType is PyString typeName)
                {
                    // Handle string-based type names (for builtin exceptions)
                    return MatchesBuiltinExceptionType(pyException, typeName.Value);
                }
                else if (Type is NameExpression nameExpr)
                {
                    // Handle bare names like "ValueError" - evaluate from scope
                    var exceptionTypeObj = scope.GetVariable(nameExpr.Name);
                    if (exceptionTypeObj is PyBuiltinType builtinType)
                    {
                        return MatchesBuiltinExceptionType(pyException, builtinType.Name);
                    }
                    else if (exceptionTypeObj is PyType pyType)
                    {
                        return pyException.GetPyType().IsSubclassOf(pyType) || 
                               pyException.GetPyType() == pyType;
                    }
                    else
                    {
                        // Fallback to name matching
                        return MatchesBuiltinExceptionType(pyException, nameExpr.Name);
                    }
                }
                
                return false;
            }
            catch
            {
                // If type evaluation fails, don't handle the exception
                return false;
            }
        }
        
        /// <summary>
        /// Match builtin exception types by name
        /// </summary>
        private bool MatchesBuiltinExceptionType(PyBaseException pyException, string typeName)
        {
            var exceptionTypeName = pyException.GetTypeName();
            
            // Direct match
            if (exceptionTypeName == typeName)
                return true;
            
            // Check inheritance hierarchy
            return typeName switch
            {
                "BaseException" => true, // All Python exceptions inherit from BaseException
                "Exception" => pyException is PyException, // Most exceptions inherit from Exception
                "ArithmeticError" => pyException is PyArithmeticError or PyZeroDivisionError or PyOverflowError,
                "LookupError" => pyException is PyLookupError or PyIndexError or PyKeyError,
                "RuntimeError" => pyException is PyRuntimeError or PyNotImplementedError or PyRecursionError,
                "ImportError" => pyException is PyImportError or PyModuleNotFoundError,
                "NameError" => pyException is PyNameError or PyUnboundLocalError,
                "SyntaxError" => pyException is PySyntaxError or PyIndentationError,
                _ => false
            };
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitNode(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitNode(this);
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            PyObject result = PyNone.Instance;
            foreach (var stmt in Body)
            {
                result = stmt.Evaluate(scope);
            }
            return result;
        }
        
        public override string ToString() => $"except {Type?.ToString() ?? ""}:";
    }
    
    /// <summary>
    /// Exception handler for except* syntax
    /// </summary>
    public class ExceptStarHandler : ASTNode
    {
        public override string NodeType => "ExceptHandler";
        public Expression? Type { get; }
        public string? Name { get; }
        public List<Statement> Body { get; }
        
        public ExceptStarHandler(Expression? type, string? name, List<Statement> body)
        {
            Type = type;
            Name = name;
            Body = body;
        }
        
        public bool CanHandle(Exception exception)
        {
            // Simplified exception type matching
            return true; // TODO: Implement proper type checking
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitNode(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitNode(this);
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Handlers are evaluated by TryStarStatement
            return PyNone.Instance;
        }
    }
    
    /// <summary>
    /// Advanced Pattern Matching Nodes (Python 3.10+ enhanced for 3.12)
    /// </summary>
    public class MatchValue : Expression
    {
        public override string NodeType => "MatchValue";
        public Expression Value { get; }
        
        public MatchValue(Expression value)
        {
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return Value.Evaluate(scope);
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitExpression(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitExpression(this);
        }
    }
    
    public class MatchSingleton : Expression
    {
        public override string NodeType => "MatchSingleton";
        public PyObject Value { get; } // None, True, False
        
        public MatchSingleton(PyObject value)
        {
            Value = value;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            return Value;
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitExpression(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitExpression(this);
        }
    }
    
    public class MatchSequence : Expression
    {
        public override string NodeType => "MatchSequence";
        public List<Expression> Patterns { get; }
        
        public MatchSequence(List<Expression> patterns)
        {
            Patterns = patterns;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Pattern matching logic for sequences
            var results = new List<PyObject>();
            foreach (var pattern in Patterns)
                results.Add(pattern.Evaluate(scope));
            // Return a simple list object for pattern matching
            return results.Count > 0 ? results[0] : PyNone.Instance;
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitExpression(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitExpression(this);
        }
    }
    
    public class MatchMapping : Expression
    {
        public override string NodeType => "MatchMapping";
        public List<Expression> Keys { get; }
        public List<Expression> Patterns { get; }
        public string? Rest { get; } // **rest pattern
        
        public MatchMapping(List<Expression> keys, List<Expression> patterns, string? rest = null)
        {
            Keys = keys;
            Patterns = patterns;
            Rest = rest;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Pattern matching logic for mappings
            var dict = new PyDict();
            for (int i = 0; i < Keys.Count && i < Patterns.Count; i++)
            {
                var key = Keys[i].Evaluate(scope);
                var value = Patterns[i].Evaluate(scope);
                dict.SetItem(key, value);
            }
            return dict;
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitExpression(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitExpression(this);
        }
    }
    
    public class MatchClass : Expression
    {
        public override string NodeType => "MatchClass";
        public Expression Cls { get; }
        public List<Expression> Patterns { get; }
        public List<string> KwdAttrs { get; }
        public List<Expression> KwdPatterns { get; }
        
        public MatchClass(Expression cls, List<Expression> patterns, 
                         List<string> kwdAttrs, List<Expression> kwdPatterns)
        {
            Cls = cls;
            Patterns = patterns;
            KwdAttrs = kwdAttrs;
            KwdPatterns = kwdPatterns;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Pattern matching logic for class instances
            var cls = Cls.Evaluate(scope);
            var instance = ((PyType)cls).CreateInstance();
            
            // Set positional patterns
            for (int i = 0; i < Patterns.Count; i++)
            {
                var value = Patterns[i].Evaluate(scope);
                // TODO: Set positional attributes
            }
            
            // Set keyword patterns
            for (int i = 0; i < KwdAttrs.Count && i < KwdPatterns.Count; i++)
            {
                var value = KwdPatterns[i].Evaluate(scope);
                instance.SetAttribute(KwdAttrs[i], value);
            }
            
            return instance;
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitExpression(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitExpression(this);
        }
    }
    
    /// <summary>
    /// Enhanced Type Parameter Support (PEP 695)
    /// </summary>
    public class TypeParamStatement : Statement
    {
        public override string NodeType => "TypeParam";
        public string Name { get; }
        public Expression? Bound { get; }
        public Expression? Default { get; }
        public bool IsCovariant { get; }
        public bool IsContravariant { get; }
        
        public TypeParamStatement(string name, Expression? bound = null, Expression? @default = null, 
                                 bool isCovariant = false, bool isContravariant = false)
        {
            Name = name;
            Bound = bound;
            Default = @default;
            IsCovariant = isCovariant;
            IsContravariant = isContravariant;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Type parameter evaluation
            var typeParam = new PyString(Name); // Use concrete PyString instead of abstract PyObject
            scope.SetVariable(Name, typeParam);
            return typeParam;
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitStatement(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitStatement(this);
        }
    }

    #endregion
    
    #region Additional Match Pattern Classes
    
    /// <summary>
    /// CPython 3.12 PEP 634: As pattern (pattern as name)
    /// </summary>
    public class AsPattern : Expression
    {
        public override string NodeType => "AsPattern";
        public Expression Pattern { get; }
        public string Name { get; }
        
        public AsPattern(Expression pattern, string name)
        {
            Pattern = pattern;
            Name = name;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // As patterns are handled during pattern matching compilation
            return new PyString($"{Pattern} as {Name}");
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitExpression(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitExpression(this);
        }
        
        public override string ToString() => $"{Pattern} as {Name}";
    }
    
    /// <summary>
    /// Star pattern in match case: *rest
    /// </summary>
    public class StarPattern : Expression
    {
        public override string NodeType => "StarPattern";
        public string Name { get; }
        
        public StarPattern(string name)
        {
            Name = name;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Star patterns are handled during pattern matching compilation
            return new PyString($"*{Name}");
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitExpression(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitExpression(this);
        }
        
        public override string ToString() => $"*{Name}";
    }
    
    /// <summary>
    /// Sequence pattern in match case: [1, 2, *rest]
    /// </summary>
    public class SequencePattern : Expression
    {
        public override string NodeType => "SequencePattern";
        public List<Expression> Patterns { get; }
        
        public SequencePattern(List<Expression> patterns)
        {
            Patterns = patterns;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Sequence patterns are handled during pattern matching compilation
            var items = Patterns.Select(p => p.Evaluate(scope)).ToArray();
            return new PyList(items);
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitExpression(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitExpression(this);
        }
        
        public override string ToString()
        {
            return $"[{string.Join(", ", Patterns)}]";
        }
    }
    
    /// <summary>
    /// Mapping pattern in match case: {"key": value}
    /// </summary>
    public class MappingPattern : Expression
    {
        public override string NodeType => "MappingPattern";
        public Dictionary<string, Expression> Patterns { get; }
        public string? RestVariable { get; }

        public MappingPattern(Dictionary<string, Expression> patterns, string? restVariable = null)
        {
            Patterns = patterns;
            RestVariable = restVariable;
        }
        
        public override PyObject Evaluate(PyScope scope)
        {
            // Mapping patterns are handled during pattern matching compilation
            var dict = new PyDict();
            foreach (var kvp in Patterns)
            {
                dict.SetItem(new PyString(kvp.Key), kvp.Value.Evaluate(scope));
            }
            return dict;
        }
        
        public override T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitExpression(this);
        }
        
        public override void Accept(IASTVisitor visitor)
        {
            visitor.VisitExpression(this);
        }
        
        public override string ToString()
        {
            var pairs = Patterns.Select(kvp => $"\"{kvp.Key}\": {kvp.Value}");
            return $"{{{string.Join(", ", pairs)}}}";
        }
    }
    
    #endregion

    #endregion
}