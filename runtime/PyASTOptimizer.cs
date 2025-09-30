using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 호환 AST 수준 최적화 엔진
    /// Peephole 최적화를 대체하는 고급 AST 패턴 매칭 및 최적화 시스템
    /// </summary>
    public class PyASTOptimizer
    {
        private readonly OptimizationLevel _level;
        private readonly Dictionary<Type, List<IOptimizationRule>> _rules;
        private readonly ASTPatternMatcher _patternMatcher;
        private readonly TypeInferenceEngine _typeInference;

        public PyASTOptimizer(OptimizationLevel level = OptimizationLevel.Standard)
        {
            _level = level;
            _rules = new Dictionary<Type, List<IOptimizationRule>>();
            _patternMatcher = new ASTPatternMatcher();
            _typeInference = new TypeInferenceEngine();
            
            RegisterOptimizationRules();
        }

        /// <summary>
        /// AST 최적화 메인 엔트리 포인트
        /// </summary>
        public List<Statement> OptimizeAST(List<Statement> statements)
        {
            if (_level == OptimizationLevel.Disabled)
                return statements;

            #if DEBUG_LOG
            Console.WriteLine($"🔧 AST 최적화 시작 (Level: {_level})");
            #endif
            
            var optimizedStatements = new List<Statement>();
            var optimizationCount = 0;

            // Phase 1: Type inference (if enabled)
            if (_level >= OptimizationLevel.TypeAware)
            {
                _typeInference.AnalyzeStatements(statements);
            }

            // Phase 2: Apply optimization rules
            foreach (var stmt in statements)
            {
                var optimizedStmt = OptimizeStatement(stmt, ref optimizationCount);
                optimizedStatements.Add(optimizedStmt);
            }

            #if DEBUG_LOG
            Console.WriteLine($"✅ AST 최적화 완료: {optimizationCount}개 최적화 적용");
            #endif
            return optimizedStatements;
        }

        /// <summary>
        /// 개별 Statement 최적화
        /// </summary>
        private Statement OptimizeStatement(Statement stmt, ref int optimizationCount)
        {
            var stmtType = stmt.GetType();
            
            if (_rules.ContainsKey(stmtType))
            {
                foreach (var rule in _rules[stmtType])
                {
                    if (rule.CanOptimize(stmt))
                    {
                        var optimized = rule.Optimize(stmt);
                        if (optimized != stmt && optimized is Statement optimizedStmt)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"🔄 AST 최적화: {rule.RuleName} ({stmtType.Name})");
                            #endif
                            optimizationCount++;
                            return OptimizeStatement(optimizedStmt, ref optimizationCount); // 재귀적으로 최적화
                        }
                    }
                }
            }

            // Nested statements/expressions도 최적화
            return OptimizeNestedElements(stmt, ref optimizationCount);
        }

        /// <summary>
        /// Statement 내부의 중첩된 요소들 최적화
        /// </summary>
        private Statement OptimizeNestedElements(Statement stmt, ref int optimizationCount)
        {
            switch (stmt)
            {
                case IfStatement ifStmt:
                    var ifOptimizedBody = new List<Statement>();
                    foreach (var s in ifStmt.Body)
                        ifOptimizedBody.Add(OptimizeStatement(s, ref optimizationCount));
                    
                    var ifOptimizedElse = ifStmt.OrElse != null ? new List<Statement>() : null;
                    if (ifStmt.OrElse != null)
                    {
                        foreach (var s in ifStmt.OrElse)
                            ifOptimizedElse!.Add(OptimizeStatement(s, ref optimizationCount));
                    }
                    
                    return new IfStatement(
                        OptimizeExpression(ifStmt.Test, ref optimizationCount),
                        ifOptimizedBody,
                        ifOptimizedElse
                    );

                case WhileStatement whileStmt:
                    var whileOptimizedBody = new List<Statement>();
                    foreach (var s in whileStmt.Body)
                        whileOptimizedBody.Add(OptimizeStatement(s, ref optimizationCount));
                    
                    var whileOptimizedElse = whileStmt.ElseClause != null ? new List<Statement>() : null;
                    if (whileStmt.ElseClause != null)
                    {
                        foreach (var s in whileStmt.ElseClause)
                            whileOptimizedElse!.Add(OptimizeStatement(s, ref optimizationCount));
                    }
                    
                    return new WhileStatement(
                        OptimizeExpression(whileStmt.Test, ref optimizationCount),
                        whileOptimizedBody,
                        whileOptimizedElse
                    );

                case ForStatement forStmt:
                    var forOptimizedBody = new List<Statement>();
                    foreach (var s in forStmt.Body)
                        forOptimizedBody.Add(OptimizeStatement(s, ref optimizationCount));
                    
                    var forOptimizedElse = forStmt.ElseClause != null ? new List<Statement>() : null;
                    if (forStmt.ElseClause != null)
                    {
                        foreach (var s in forStmt.ElseClause)
                            forOptimizedElse!.Add(OptimizeStatement(s, ref optimizationCount));
                    }
                    
                    return new ForStatement(
                        forStmt.Target,
                        OptimizeExpression(forStmt.Iter, ref optimizationCount),
                        forOptimizedBody,
                        forOptimizedElse
                    );

                case ExpressionStatement exprStmt:
                    return new ExpressionStatement(OptimizeExpression(exprStmt.Expression, ref optimizationCount));

                case AssignStatement assignStmt:
                    // CPython 3.12: Optimize targets and value
                    var optimizedTargets = new List<Expression>();
                    foreach (var target in assignStmt.Targets)
                    {
                        optimizedTargets.Add(OptimizeExpression(target, ref optimizationCount));
                    }
                    return new AssignStatement(
                        optimizedTargets,
                        OptimizeExpression(assignStmt.Value, ref optimizationCount)
                    );

                default:
                    return stmt;
            }
        }

        /// <summary>
        /// Expression 최적화 (public wrapper)
        /// </summary>
        private Expression OptimizeExpression(Expression expr, ref int optimizationCount)
        {
            return OptimizeExpression(expr, ref optimizationCount, 0);
        }

        /// <summary>
        /// Expression 최적화 (무한 재귀 방지 포함)
        /// </summary>
        private Expression OptimizeExpression(Expression expr, ref int optimizationCount, int depth)
        {
            const int MAX_OPTIMIZATION_DEPTH = 50; // 최대 최적화 깊이

            // 무한 재귀 방지
            if (depth > MAX_OPTIMIZATION_DEPTH)
            {
                #if DEBUG_LOG
                Console.WriteLine($"⚠️ AST 최적화 깊이 제한 도달: {depth} (타입: {expr.GetType().Name})");
                #endif
                return expr; // 더 이상 최적화하지 않고 원본 반환
            }

            var exprType = expr.GetType();

            if (_rules.ContainsKey(exprType))
            {
                foreach (var rule in _rules[exprType])
                {
                    if (rule.CanOptimize(expr))
                    {
                        var optimized = rule.Optimize(expr);
                        if (optimized != expr)
                        {
                            #if DEBUG_LOG
                            Console.WriteLine($"🔄 AST 최적화 (깊이 {depth}): {rule.RuleName} ({exprType.Name})");
                            #endif
                            optimizationCount++;

                            // 깊이를 증가시켜 재귀 호출
                            return OptimizeExpression((Expression)optimized, ref optimizationCount, depth + 1);
                        }
                    }
                }
            }

            // Nested expressions도 최적화
            return OptimizeNestedExpressions(expr, ref optimizationCount, depth);
        }

        /// <summary>
        /// Expression 내부의 중첩된 요소들 최적화 (public wrapper)
        /// </summary>
        private Expression OptimizeNestedExpressions(Expression expr, ref int optimizationCount)
        {
            return OptimizeNestedExpressions(expr, ref optimizationCount, 0);
        }

        /// <summary>
        /// Expression 내부의 중첩된 요소들 최적화 (깊이 제한 포함)
        /// </summary>
        private Expression OptimizeNestedExpressions(Expression expr, ref int optimizationCount, int depth)
        {
            switch (expr)
            {
                case BinOpExpression binOp:
                    return new BinOpExpression(
                        OptimizeExpression(binOp.Left, ref optimizationCount, depth),
                        binOp.OpNode,
                        OptimizeExpression(binOp.Right, ref optimizationCount, depth)
                    );

                case UnaryOpExpression unaryOp:
                    return new UnaryOpExpression(
                        unaryOp.OpNode,
                        OptimizeExpression(unaryOp.Operand, ref optimizationCount, depth)
                    );

                case CallExpression call:
                    var optimizedArgs = new List<Expression>();
                    foreach (var arg in call.Arguments)
                        optimizedArgs.Add(OptimizeExpression(arg, ref optimizationCount, depth));

                    return new CallExpression(
                        OptimizeExpression(call.Function, ref optimizationCount, depth),
                        optimizedArgs,
                        call.Keywords
                    );

                case TupleExpression tuple:
                    var tupleOptimizedElements = new List<Expression>();
                    foreach (var elem in tuple.Elements)
                        tupleOptimizedElements.Add(OptimizeExpression(elem, ref optimizationCount, depth));

                    return new TupleExpression(tupleOptimizedElements);

                case ListExpression list:
                    var listOptimizedElements = new List<Expression>();
                    foreach (var elem in list.Elements)
                        listOptimizedElements.Add(OptimizeExpression(elem, ref optimizationCount, depth));

                    return new ListExpression(listOptimizedElements);

                default:
                    return expr;
            }
        }

        /// <summary>
        /// 최적화 규칙들을 등록
        /// </summary>
        private void RegisterOptimizationRules()
        {
            // Basic optimizations (Level: Basic+)
            RegisterRule<BinaryOpExpression>(new ConstantFoldingRule());
            RegisterRule<UnaryOpExpression>(new UnaryConstantFoldingRule());
            RegisterRule<IfStatement>(new DeadBranchEliminationRule());
            RegisterRule<WhileStatement>(new DeadLoopEliminationRule());
            RegisterRule<BinaryOpExpression>(new BooleanSimplificationRule());
            RegisterRule<BoolOpExpression>(new BooleanSimplificationRule());
            
            // Standard optimizations (Level: Standard+)
            if (_level >= OptimizationLevel.Standard)
            {
                RegisterRule<BinaryOpExpression>(new AlgebraicSimplificationRule());
                RegisterRule<CallExpression>(new BuiltinCallOptimizationRule());
                RegisterRule<CallExpression>(new ContainerSizeOptimizationRule(_typeInference));
            }
            
            // Type-aware optimizations (Level: TypeAware+)
            if (_level >= OptimizationLevel.TypeAware)
            {
                RegisterRule<BinaryOpExpression>(new TypeSpecializedBinaryOpRule(_typeInference));
                RegisterRule<CallExpression>(new BuiltinMethodInliningRule(_typeInference));
                RegisterRule<CallExpression>(new TypeGuardEliminationRule(_typeInference));
            }
            
            // Aggressive optimizations (Level: Aggressive)
            if (_level >= OptimizationLevel.Aggressive)
            {
                RegisterRule<ForStatement>(new LoopUnrollingRule());
                RegisterRule<CallExpression>(new FunctionInliningRule());
            }
        }

        /// <summary>
        /// 특정 AST 노드 타입에 대한 최적화 규칙 등록
        /// </summary>
        private void RegisterRule<T>(IOptimizationRule rule) where T : ASTNode
        {
            var type = typeof(T);
            if (!_rules.ContainsKey(type))
                _rules[type] = new List<IOptimizationRule>();
            
            _rules[type].Add(rule);
        }
    }

    /// <summary>
    /// 최적화 수준 열거형
    /// </summary>
    public enum OptimizationLevel
    {
        Disabled = 0,    // 최적화 비활성화
        Basic = 1,       // 기본 AST 최적화만
        Standard = 2,    // 표준 최적화 (기본값)  
        TypeAware = 3,   // 타입 추론 포함
        Aggressive = 4   // 공격적 최적화 (실험적)
    }

    /// <summary>
    /// 최적화 규칙 인터페이스
    /// </summary>
    public interface IOptimizationRule
    {
        string RuleName { get; }
        bool CanOptimize(ASTNode node);
        ASTNode Optimize(ASTNode node);
    }

    /// <summary>
    /// AST 패턴 매칭 엔진
    /// </summary>
    public class ASTPatternMatcher
    {
        public bool MatchesPattern(ASTNode node, ASTPattern pattern)
        {
            // 패턴 매칭 로직 구현
            return pattern.Matches(node);
        }
    }

    /// <summary>
    /// AST 패턴 인터페이스
    /// </summary>
    public interface ASTPattern
    {
        bool Matches(ASTNode node);
    }

    /// <summary>
    /// 타입 추론 엔진 (기본 구현)
    /// </summary>
    public class TypeInferenceEngine
    {
        private readonly PyTypeInference _typeInference;

        public TypeInferenceEngine()
        {
            _typeInference = new PyTypeInference();
        }

        public void AnalyzeStatements(List<Statement> statements)
        {
            _typeInference.AnalyzeStatements(statements);
        }

        public PyTypeInfo GetVariableType(string variableName)
        {
            return _typeInference.GetVariableType(variableName);
        }
        
        public PyTypeInfo GetExpressionType(Expression expr)
        {
            return _typeInference.GetExpressionType(expr);
        }
    }
}