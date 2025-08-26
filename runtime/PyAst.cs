namespace SharpPy
{
    #region AST Extension (기존 시스템과 연동)

    // AST 노드의 베이스 클래스
    public abstract class ASTNode
    {
        public abstract string NodeType { get; }
        public override string ToString() => NodeType;
    }

    // 문장 노드들
    public abstract class Statement : ASTNode { }

    public class AssignStatement : Statement
    {
        public override string NodeType => "Assign";
        public string VariableName { get; }
        public Expression Value { get; }
        
        public AssignStatement(string variableName, Expression value)
        {
            VariableName = variableName;
            Value = value;
        }
        
        public override string ToString() => $"{VariableName} = {Value}";
    }

    public class ExpressionStatement : Statement
    {
        public override string NodeType => "Expr";
        public Expression Expression { get; }
        
        public ExpressionStatement(Expression expression)
        {
            Expression = expression;
        }
        
        public override string ToString() => Expression.ToString();
    }

    public class ReturnStatement : Statement
    {
        public override string NodeType => "Return";
        public Expression Value { get; }
        
        public ReturnStatement(Expression value = null)
        {
            Value = value;
        }
        
        public override string ToString() => $"return {Value?.ToString() ?? ""}";
    }

    public class FunctionDefStatement : Statement
    {
        public override string NodeType => "FunctionDef";
        public string Name { get; }
        public List<string> Parameters { get; }
        public List<Statement> Body { get; }
        
        public FunctionDefStatement(string name, List<string> parameters, List<Statement> body)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
        }
        
        public override string ToString() => $"def {Name}({string.Join(", ", Parameters)}): ...";
    }

    // 표현식 노드들
    public abstract class Expression : ASTNode { }

    public class ConstantExpression : Expression
    {
        public override string NodeType => "Constant";
        public PyObject Value { get; }
        
        public ConstantExpression(PyObject value)
        {
            Value = value;
        }
        
        public override string ToString() => Value.ToString();
    }

    public class NameExpression : Expression
    {
        public override string NodeType => "Name";
        public string Name { get; }
        
        public NameExpression(string name)
        {
            Name = name;
        }
        
        public override string ToString() => Name;
    }

    public class BinaryOpExpression : Expression
    {
        public override string NodeType => "BinOp";
        public Expression Left { get; }
        public string Operator { get; }
        public Expression Right { get; }
        
        public BinaryOpExpression(Expression left, string op, Expression right)
        {
            Left = left;
            Operator = op;
            Right = right;
        }
        
        public override string ToString() => $"({Left} {Operator} {Right})";
    }

    public class CallExpression : Expression
    {
        public override string NodeType => "Call";
        public Expression Function { get; }
        public List<Expression> Arguments { get; }
        
        public CallExpression(Expression function, List<Expression> arguments)
        {
            Function = function;
            Arguments = arguments;
        }
        
        public override string ToString() => $"{Function}({string.Join(", ", Arguments)})";
    }

    #endregion
}