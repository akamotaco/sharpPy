namespace SharpPy
{
    #region AST Extension (기존 시스템과 연동)

    // AST 노드의 베이스 클래스
    public abstract class ASTNode
    {
        public abstract string NodeType { get; }
        public override string ToString() => NodeType;
        
        // Evaluate 메서드 - 나중에 구현
        public virtual PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException($"{GetType().Name}.Evaluate() - 나중에 구현예정");
        }
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
        public List<string> TypeParams { get; } // Python 3.12
        
        public FunctionDefStatement(string name, List<string> parameters, List<Statement> body, List<string> typeParams = null)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
            TypeParams = typeParams ?? new List<string>();
        }
        
        public override string ToString()
        {
            var typeParamStr = TypeParams.Any() ? $"[{string.Join(", ", TypeParams)}]" : "";
            return $"def {Name}{typeParamStr}({string.Join(", ", Parameters)}): ...";
        }
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("FunctionDefStatement.Evaluate() - 나중에 구현예정");
        }
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

    // Python 3.12 추가 구문들
    
    // 제어 구조 문장들
    public class IfStatement : Statement
    {
        public override string NodeType => "If";
        public Expression Test { get; }
        public List<Statement> Body { get; }
        public List<Statement> OrElse { get; }
        
        public IfStatement(Expression test, List<Statement> body, List<Statement> orElse)
        {
            Test = test;
            Body = body;
            OrElse = orElse;
        }
        
        public override string ToString() => $"if {Test}: ...";
    }

    public class WhileStatement : Statement
    {
        public override string NodeType => "While";
        public Expression Test { get; }
        public List<Statement> Body { get; }
        
        public WhileStatement(Expression test, List<Statement> body)
        {
            Test = test;
            Body = body;
        }
        
        public override string ToString() => $"while {Test}: ...";
    }

    public class ForStatement : Statement
    {
        public override string NodeType => "For";
        public string Target { get; }
        public Expression Iter { get; }
        public List<Statement> Body { get; }
        
        public ForStatement(string target, Expression iter, List<Statement> body)
        {
            Target = target;
            Iter = iter;
            Body = body;
        }
        
        public override string ToString() => $"for {Target} in {Iter}: ...";
    }

    public class TryStatement : Statement
    {
        public override string NodeType => "Try";
        public List<Statement> Body { get; }
        public List<ExceptHandler> Handlers { get; }
        
        public TryStatement(List<Statement> body, List<ExceptHandler> handlers)
        {
            Body = body;
            Handlers = handlers;
        }
        
        public override string ToString() => "try: ...";
    }

    public class ExceptHandler : ASTNode
    {
        public override string NodeType => "ExceptHandler";
        public Expression Type { get; }
        public string Name { get; }
        public List<Statement> Body { get; }
        
        public ExceptHandler(Expression type, string name, List<Statement> body)
        {
            Type = type;
            Name = name;
            Body = body;
        }
    }

    public class ClassDefStatement : Statement
    {
        public override string NodeType => "ClassDef";
        public string Name { get; }
        public List<Expression> Bases { get; }
        public List<Statement> Body { get; }
        public List<string> TypeParams { get; } // Python 3.12
        
        public ClassDefStatement(string name, List<Expression> bases, List<Statement> body, List<string> typeParams = null)
        {
            Name = name;
            Bases = bases;
            Body = body;
            TypeParams = typeParams ?? new List<string>();
        }
        
        public override string ToString()
        {
            var typeParamStr = TypeParams.Any() ? $"[{string.Join(", ", TypeParams)}]" : "";
            return $"class {Name}{typeParamStr}: ...";
        }
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("ClassDefStatement.Evaluate() - 나중에 구현예정");
        }
    }

    // Import 문장들
    public class ImportStatement : Statement
    {
        public override string NodeType => "Import";
        public List<string> Names { get; }
        
        public ImportStatement(List<string> names)
        {
            Names = names;
        }
        
        public override string ToString() => $"import {string.Join(", ", Names)}";
    }

    public class ImportFromStatement : Statement
    {
        public override string NodeType => "ImportFrom";
        public string Module { get; }
        public List<string> Names { get; }
        
        public ImportFromStatement(string module, List<string> names)
        {
            Module = module;
            Names = names;
        }
        
        public override string ToString() => $"from {Module} import {string.Join(", ", Names)}";
    }

    // 복합 할당 및 특수 할당
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
        
        public override string ToString() => $"{Target} {Op} {Value}";
    }

    // Walrus operator (:=) - Python 3.8+
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
        
        public override string ToString() => $"{Target} := {Value}";
    }

    // 기타 문장들
    public class YieldStatement : Statement
    {
        public override string NodeType => "Yield";
        public Expression Value { get; }
        
        public YieldStatement(Expression value = null)
        {
            Value = value;
        }
        
        public override string ToString() => $"yield {Value?.ToString() ?? ""}";
    }

    public class BreakStatement : Statement
    {
        public override string NodeType => "Break";
        public override string ToString() => "break";
    }

    public class ContinueStatement : Statement
    {
        public override string NodeType => "Continue";
        public override string ToString() => "continue";
    }

    public class PassStatement : Statement
    {
        public override string NodeType => "Pass";
        public override string ToString() => "pass";
    }

    public class RaiseStatement : Statement
    {
        public override string NodeType => "Raise";
        public Expression Exception { get; }
        
        public RaiseStatement(Expression exception = null)
        {
            Exception = exception;
        }
        
        public override string ToString() => $"raise {Exception?.ToString() ?? ""}";
    }

    // 확장된 표현식들
    
    // 컨테이너 표현식들
    public class ListExpression : Expression
    {
        public override string NodeType => "List";
        public List<Expression> Elements { get; }
        
        public ListExpression(List<Expression> elements)
        {
            Elements = elements;
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
        
        public override string ToString() => $"({string.Join(", ", Elements)})";
    }

    public class DictExpression : Expression
    {
        public override string NodeType => "Dict";
        public Dictionary<Expression, Expression> Items { get; }
        
        public DictExpression(Dictionary<Expression, Expression> items)
        {
            Items = items;
        }
        
        public override string ToString() => "{dict}";
    }

    public class SetExpression : Expression
    {
        public override string NodeType => "Set";
        public List<Expression> Elements { get; }
        
        public SetExpression(List<Expression> elements)
        {
            Elements = elements;
        }
        
        public override string ToString() => $"{{{string.Join(", ", Elements)}}}";
    }

    // 고급 표현식들
    public class CompareExpression : Expression
    {
        public override string NodeType => "Compare";
        public Expression Left { get; }
        public string Op { get; }
        public Expression Right { get; }
        
        public CompareExpression(Expression left, string op, Expression right)
        {
            Left = left;
            Op = op;
            Right = right;
        }
        
        public override string ToString() => $"{Left} {Op} {Right}";
    }

    public class BoolOpExpression : Expression
    {
        public override string NodeType => "BoolOp";
        public string Op { get; }
        public List<Expression> Values { get; }
        
        public BoolOpExpression(string op, List<Expression> values)
        {
            Op = op;
            Values = values;
        }
        
        public override string ToString() => $"({string.Join($" {Op} ", Values)})";
    }

    public class UnaryOpExpression : Expression
    {
        public override string NodeType => "UnaryOp";
        public string Op { get; }
        public Expression Operand { get; }
        
        public UnaryOpExpression(string op, Expression operand)
        {
            Op = op;
            Operand = operand;
        }
        
        public override string ToString() => $"{Op} {Operand}";
    }

    public class LambdaExpression : Expression
    {
        public override string NodeType => "Lambda";
        public List<string> Args { get; }
        public Expression Body { get; }
        
        public LambdaExpression(List<string> args, Expression body)
        {
            Args = args;
            Body = body;
        }
        
        public override string ToString() => $"lambda {string.Join(", ", Args)}: {Body}";
    }

    public class ConditionalExpression : Expression
    {
        public override string NodeType => "IfExp";
        public Expression Test { get; }
        public Expression Body { get; }
        public Expression OrElse { get; }
        
        public ConditionalExpression(Expression test, Expression body, Expression orElse)
        {
            Test = test;
            Body = body;
            OrElse = orElse;
        }
        
        public override string ToString() => $"{Body} if {Test} else {OrElse}";
    }

    // 인덱싱 및 슬라이싱
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
        
        public override string ToString() => $"{Value}[{Slice}]";
    }

    public class SliceExpression : Expression
    {
        public override string NodeType => "Slice";
        public string SliceStr { get; }
        
        public SliceExpression(string sliceStr)
        {
            SliceStr = sliceStr;
        }
        
        public override string ToString() => SliceStr;
    }

    // Python 3.6+ f-strings
    public class FStringExpression : Expression
    {
        public override string NodeType => "JoinedStr";
        public string Value { get; }
        
        public FStringExpression(string value)
        {
            Value = value;
        }
        
        public override string ToString() => Value;
    }

    // 속성 접근
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
        
        public override string ToString() => $"{Value}.{Attr}";
    }

    // 별표 표현식 (unpacking)
    public class StarredExpression : Expression
    {
        public override string NodeType => "Starred";
        public Expression Value { get; }
        
        public StarredExpression(Expression value)
        {
            Value = value;
        }
        
        public override string ToString() => $"*{Value}";
    }

    // Python 3.12 새로운 기능들
    
    // Type alias statement (Python 3.12)
    public class TypeAliasStatement : Statement
    {
        public override string NodeType => "TypeAlias";
        public string Name { get; }
        public List<string> TypeParams { get; }
        public Expression Value { get; }
        
        public TypeAliasStatement(string name, List<string> typeParams, Expression value)
        {
            Name = name;
            TypeParams = typeParams ?? new List<string>();
            Value = value;
        }
        
        public override string ToString()
        {
            var typeParamStr = TypeParams.Any() ? $"[{string.Join(", ", TypeParams)}]" : "";
            return $"type {Name}{typeParamStr} = {Value}";
        }
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("TypeAliasStatement.Evaluate() - 나중에 구현예정");
        }
    }

    // Match statement (Python 3.10+)
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
        
        public override string ToString() => $"match {Subject}: ...";
    }

    public class MatchCase : ASTNode
    {
        public override string NodeType => "match_case";
        public Expression Pattern { get; }
        public Expression Guard { get; }
        public List<Statement> Body { get; }
        
        public MatchCase(Expression pattern, Expression guard, List<Statement> body)
        {
            Pattern = pattern;
            Guard = guard;
            Body = body;
        }
    }

    // 컴프리헨션 표현식들
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
        
        public override string ToString() => $"({Element} for ...)";
    }

    public class Comprehension : ASTNode
    {
        public override string NodeType => "comprehension";
        public string Target { get; }
        public Expression Iter { get; }
        public List<Expression> Ifs { get; }
        
        public Comprehension(string target, Expression iter, List<Expression> ifs)
        {
            Target = target;
            Iter = iter;
            Ifs = ifs;
        }
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("Comprehension.Evaluate() - 나중에 구현예정");
        }
    }

    // 추가된 문장들 (Python 3.12 완전성)
    
    // async def 문
    public class AsyncFunctionDefStatement : Statement
    {
        public override string NodeType => "AsyncFunctionDef";
        public string Name { get; }
        public List<string> Parameters { get; }
        public List<Statement> Body { get; }
        public List<string> TypeParams { get; } // Python 3.12
        
        public AsyncFunctionDefStatement(string name, List<string> parameters, List<Statement> body, List<string> typeParams = null)
        {
            Name = name;
            Parameters = parameters;
            Body = body;
            TypeParams = typeParams ?? new List<string>();
        }
        
        public override string ToString()
        {
            var typeParamStr = TypeParams.Any() ? $"[{string.Join(", ", TypeParams)}]" : "";
            return $"async def {Name}{typeParamStr}({string.Join(", ", Parameters)}): ...";
        }
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("AsyncFunctionDefStatement.Evaluate() - 나중에 구현예정");
        }
    }

    // with 문
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
        
        public override string ToString() => "with ...";
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("WithStatement.Evaluate() - 나중에 구현예정");
        }
    }

    public class WithItem : ASTNode
    {
        public override string NodeType => "withitem";
        public Expression ContextExpr { get; }
        public Expression OptionalVars { get; }
        
        public WithItem(Expression contextExpr, Expression optionalVars = null)
        {
            ContextExpr = contextExpr;
            OptionalVars = optionalVars;
        }
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("WithItem.Evaluate() - 나중에 구현예정");
        }
    }

    // yield from 문
    public class YieldFromStatement : Statement
    {
        public override string NodeType => "YieldFrom";
        public Expression Value { get; }
        
        public YieldFromStatement(Expression value)
        {
            Value = value;
        }
        
        public override string ToString() => $"yield from {Value}";
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("YieldFromStatement.Evaluate() - 나중에 구현예정");
        }
    }

    // assert 문
    public class AssertStatement : Statement
    {
        public override string NodeType => "Assert";
        public Expression Test { get; }
        public Expression Msg { get; }
        
        public AssertStatement(Expression test, Expression msg = null)
        {
            Test = test;
            Msg = msg;
        }
        
        public override string ToString() => $"assert {Test}";
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("AssertStatement.Evaluate() - 나중에 구현예정");
        }
    }

    // delete 문
    public class DeleteStatement : Statement
    {
        public override string NodeType => "Delete";
        public List<Expression> Targets { get; }
        
        public DeleteStatement(List<Expression> targets)
        {
            Targets = targets;
        }
        
        public override string ToString() => $"del {string.Join(", ", Targets)}";
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("DeleteStatement.Evaluate() - 나중에 구현예정");
        }
    }

    // global 문
    public class GlobalStatement : Statement
    {
        public override string NodeType => "Global";
        public List<string> Names { get; }
        
        public GlobalStatement(List<string> names)
        {
            Names = names;
        }
        
        public override string ToString() => $"global {string.Join(", ", Names)}";
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("GlobalStatement.Evaluate() - 나중에 구현예정");
        }
    }

    // nonlocal 문
    public class NonlocalStatement : Statement
    {
        public override string NodeType => "Nonlocal";
        public List<string> Names { get; }
        
        public NonlocalStatement(List<string> names)
        {
            Names = names;
        }
        
        public override string ToString() => $"nonlocal {string.Join(", ", Names)}";
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("NonlocalStatement.Evaluate() - 나중에 구현예정");
        }
    }

    // await 표현식
    public class AwaitExpression : Expression
    {
        public override string NodeType => "Await";
        public Expression Value { get; }
        
        public AwaitExpression(Expression value)
        {
            Value = value;
        }
        
        public override string ToString() => $"await {Value}";
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("AwaitExpression.Evaluate() - 나중에 구현예정");
        }
    }

    // Python 3.12 Type Parameter 표현식들
    
    // TypeVar expression: T
    public class TypeVarExpression : Expression
    {
        public override string NodeType => "TypeVar";
        public string Name { get; }
        public Expression Bound { get; }
        
        public TypeVarExpression(string name, Expression bound = null)
        {
            Name = name;
            Bound = bound;
        }
        
        public override string ToString() => Name;
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("TypeVarExpression.Evaluate() - 나중에 구현예정");
        }
    }

    // ParamSpec expression: **P
    public class ParamSpecExpression : Expression
    {
        public override string NodeType => "ParamSpec";
        public string Name { get; }
        
        public ParamSpecExpression(string name)
        {
            Name = name;
        }
        
        public override string ToString() => $"**{Name}";
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("ParamSpecExpression.Evaluate() - 나중에 구현예정");
        }
    }

    // TypeVarTuple expression: *Ts
    public class TypeVarTupleExpression : Expression
    {
        public override string NodeType => "TypeVarTuple";
        public string Name { get; }
        
        public TypeVarTupleExpression(string name)
        {
            Name = name;
        }
        
        public override string ToString() => $"*{Name}";
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("TypeVarTupleExpression.Evaluate() - 나중에 구현예정");
        }
    }


    #endregion
}