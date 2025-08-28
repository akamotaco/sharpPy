using System;
using System.Collections.Generic;
using System.Linq;
using SharpPy.Interop;

namespace SharpPy
{
    /// <summary>
    /// 부분적으로 지원되는 Python 3.12 기능들의 실행 로직
    /// </summary>
    public static class PyExecutor
    {
        #region Walrus Operator (:=) Execution
        
        /// <summary>
        /// Walrus 연산자 실행
        /// </summary>
        public static PyObject ExecuteWalrusOperator(WalrusStatement stmt, PyScope scope)
        {
            // 표현식을 평가하고 변수에 할당
            var value = stmt.Value.Evaluate(scope);
            scope.SetVariable(stmt.Target, value);
            return value; // walrus operator returns the assigned value
        }
        
        #endregion
        
        #region Type Alias Execution
        
        /// <summary>
        /// Type alias 실행 (Python 3.12 PEP 695)
        /// </summary>
        public static PyObject ExecuteTypeAlias(TypeAliasStatement stmt, PyScope scope)
        {
            // Type alias를 C# Type으로 등록
            var aliasName = stmt.Name;
            var targetType = stmt.Value;
            
            // 간단한 구현: 타입 이름을 스코프에 등록
            var typeAlias = new PyTypeAlias(aliasName, targetType, stmt.TypeParams);
            scope.SetVariable(aliasName, typeAlias);
            
            Console.WriteLine($"📝 Type alias registered: {aliasName} = {targetType}");
            return typeAlias;
        }
        
        #endregion
        
        #region Match Statement Execution
        
        /// <summary>
        /// Match statement 실행 (기본적인 패턴 매칭)
        /// </summary>
        public static PyObject ExecuteMatchStatement(MatchStatement stmt, PyScope scope)
        {
            var subject = stmt.Subject.Evaluate(scope);
            
            foreach (var matchCase in stmt.Cases)
            {
                if (MatchPattern(subject, matchCase.Pattern, scope))
                {
                    // Guard 조건 확인
                    if (matchCase.Guard != null)
                    {
                        var guardResult = matchCase.Guard.Evaluate(scope);
                        if (!guardResult.ToBool())
                            continue;
                    }
                    
                    // 매칭된 케이스 실행
                    PyObject? result = null;
                    foreach (var bodyStmt in matchCase.Body)
                    {
                        result = bodyStmt.Evaluate(scope);
                    }
                    return result ?? PyNone.Instance;
                }
            }
            
            // 매칭되는 케이스가 없음
            throw new Exception("No matching case found in match statement");
        }
        
        private static bool MatchPattern(PyObject subject, Expression pattern, PyScope scope)
        {
            switch (pattern)
            {
                case ConstantExpression constExpr:
                    return subject.Equals(constExpr.Value);
                    
                case NameExpression nameExpr when nameExpr.Name == "_":
                    return true; // wildcard pattern
                    
                case NameExpression nameExpr:
                    // Variable binding
                    scope.SetVariable(nameExpr.Name, subject);
                    return true;
                    
                default:
                    // More complex patterns would be implemented here
                    return false;
            }
        }
        
        #endregion
        
        #region f-string Execution
        
        /// <summary>
        /// f-string 실행 (기본적인 구현)
        /// </summary>
        public static PyObject ExecuteFString(FStringExpression expr, PyScope scope)
        {
            // 간단한 f-string 처리: C# 문자열 보간 사용
            var parts = new List<string>();
            
            foreach (var value in expr.Values)
            {
                if (value is ConstantExpression constExpr && constExpr.Value is PyString pyStr)
                {
                    parts.Add(pyStr.Value);
                }
                else
                {
                    var evaluated = value.Evaluate(scope);
                    parts.Add(evaluated.ToString());
                }
            }
            
            var result = string.Join("", parts);
            return new PyString(result);
        }
        
        #endregion
        
        #region Python-Style Comprehension Execution
        
        /// <summary>
        /// List comprehension 실행 - 순수 Python 방식
        /// [expr for item in iterable if condition]
        /// </summary>
        public static PyObject ExecuteListComprehension(ListComprehension expr, PyScope scope)
        {
            return SharpPy.Interop.CSharpPythonInterop.PythonComprehensions.ExecuteListComprehension(
                expr.Element, expr.Generators, scope);
        }
        
        /// <summary>
        /// Dict comprehension 실행 - 순수 Python 방식
        /// {key_expr: value_expr for item in iterable if condition}
        /// </summary>
        public static PyObject ExecuteDictComprehension(DictComprehension expr, PyScope scope)
        {
            return SharpPy.Interop.CSharpPythonInterop.PythonComprehensions.ExecuteDictComprehension(
                expr.Key, expr.Value, expr.Generators, scope);
        }
        
        /// <summary>
        /// Set comprehension 실행 - 순수 Python 방식
        /// {expr for item in iterable if condition}
        /// </summary>
        public static PyObject ExecuteSetComprehension(SetComprehension expr, PyScope scope)
        {
            return SharpPy.Interop.CSharpPythonInterop.PythonComprehensions.ExecuteSetComprehension(
                expr.Element, expr.Generators, scope);
        }
        
        /// <summary>
        /// Generator expression 실행 - 순수 Python 방식  
        /// (expr for item in iterable if condition)
        /// </summary>
        public static PyObject ExecuteGeneratorExpression(GeneratorExpression expr, PyScope scope)
        {
            return SharpPy.Interop.CSharpPythonInterop.PythonComprehensions.ExecuteGeneratorExpression(
                expr.Element, expr.Generators, scope);
        }
        
        #endregion
        
        #region Generic Function/Class Execution
        
        /// <summary>
        /// 제네릭 함수 실행 (C# 제네릭 연동)
        /// </summary>
        public static PyObject ExecuteGenericFunction(FunctionDefStatement funcDef, PyScope scope)
        {
            // Type parameters를 C# 제네릭 정보로 변환
            var typeInfo = new GenericTypeInfo(funcDef.TypeParams);
            
            // 제네릭 함수를 PyFunction으로 생성 (간단한 구현)
            var genericFunc = new PyGenericFunction(funcDef.Name, args => PyNone.Instance, typeInfo);
            scope.SetVariable(funcDef.Name, genericFunc);
            
            Console.WriteLine($"📝 Generic function defined: {funcDef.Name}[{string.Join(", ", funcDef.TypeParams)}]");
            return genericFunc;
        }
        
        /// <summary>
        /// 제네릭 클래스 실행
        /// </summary>
        public static PyObject ExecuteGenericClass(ClassDefStatement classDef, PyScope scope)
        {
            var typeInfo = new GenericTypeInfo(classDef.TypeParams);
            var genericClass = new PyGenericClass(classDef.Name, new PyType[0], typeInfo);
            scope.SetVariable(classDef.Name, genericClass);
            
            Console.WriteLine($"📝 Generic class defined: {classDef.Name}[{string.Join(", ", classDef.TypeParams)}]");
            return genericClass;
        }
        
        #endregion
    }
    
    #region Supporting Classes
    
    /// <summary>
    /// Type alias를 나타내는 PyObject
    /// </summary>
    public class PyTypeAlias : PyObject
    {
        public string Name { get; }
        public Expression Target { get; }
        public List<string> TypeParams { get; }
        
        public PyTypeAlias(string name, Expression target, List<string> typeParams)
        {
            Name = name;
            Target = target;
            TypeParams = typeParams;
        }
        
        public override string ToString()
        {
            var typeParamStr = TypeParams.Any() ? $"[{string.Join(", ", TypeParams)}]" : "";
            return $"type {Name}{typeParamStr} = {Target}";
        }
    }
    
    /// <summary>
    /// 제네릭 타입 정보
    /// </summary>
    public class GenericTypeInfo
    {
        public List<string> TypeParameters { get; }
        
        public GenericTypeInfo(List<string> typeParams)
        {
            TypeParameters = typeParams;
        }
        
        public bool IsGeneric => TypeParameters.Any();
    }
    
    /// <summary>
    /// 제네릭 함수
    /// </summary>
    public class PyGenericFunction : PyFunction
    {
        public GenericTypeInfo TypeInfo { get; }
        
        public PyGenericFunction(string name, Func<PyObject[], PyObject> implementation, GenericTypeInfo typeInfo) 
            : base(name, implementation)
        {
            TypeInfo = typeInfo;
        }
        
        public override string ToString()
        {
            var typeParamStr = TypeInfo.IsGeneric ? $"[{string.Join(", ", TypeInfo.TypeParameters)}]" : "";
            return $"<generic function {Name}{typeParamStr}>";
        }
    }
    
    /// <summary>
    /// 제네릭 클래스
    /// </summary>
    public class PyGenericClass : PyClass
    {
        public GenericTypeInfo TypeInfo { get; }
        
        public PyGenericClass(string name, PyType[] baseTypes, GenericTypeInfo typeInfo)
            : base(name, baseTypes)
        {
            TypeInfo = typeInfo;
        }
        
        public override string ToString()
        {
            var typeParamStr = TypeInfo.IsGeneric ? $"[{string.Join(", ", TypeInfo.TypeParameters)}]" : "";
            return $"<generic class '{Name}{typeParamStr}'>";
        }
    }
    
    #endregion
}