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
        #region Pattern Matching Recursion Control - CPython 3.12 Style
        
        // CPython 3.12: Track recursion depth for pattern matching
        private static int _orPatternDepth = 0;
        private const int MAX_OR_PATTERN_DEPTH = 100;  // CPython style recursion limit
        
        #endregion
        
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
                
                // CPython 3.12: Sequence patterns [a, b, c] or (a, b, c)
                case ListExpression listExpr:
                    return MatchSequencePattern(subject, listExpr.Elements, scope);
                    
                case TupleExpression tupleExpr:
                    return MatchSequencePattern(subject, tupleExpr.Elements, scope);
                
                // CPython 3.12: Mapping patterns {"key": value}
                case DictExpression dictExpr:
                    return MatchMappingPattern(subject, dictExpr, scope);
                
                // CPython 3.12: Class patterns Point(x, y)
                case CallExpression callExpr:
                    return MatchClassPattern(subject, callExpr, scope);
                
                // CPython 3.12: Or patterns (pattern1 | pattern2)
                case OrPattern orPattern:
                    return MatchOrPattern(subject, orPattern, scope);
                    
                default:
                    // More complex patterns would be implemented here
                    return false;
            }
        }
        
        /// <summary>
        /// CPython 3.12: Match sequence patterns like [a, b, *rest]
        /// </summary>
        private static bool MatchSequencePattern(PyObject subject, List<Expression> patterns, PyScope scope)
        {
            // Check if subject is a sequence (list, tuple, etc.)
            if (!(subject is PyList pyList))
            {
                if (subject is PyTuple pyTuple)
                {
                    // Convert tuple to list for uniform processing
                    var tempList = new List<PyObject>(pyTuple.Items);
                    pyList = new PyList(tempList);
                }
                else
                {
                    return false; // Not a sequence
                }
            }
            
            var subjectItems = pyList.Items;
            
            // Handle star patterns (*rest)
            var starIndex = -1;
            for (int i = 0; i < patterns.Count; i++)
            {
                if (patterns[i] is NameExpression nameExpr && nameExpr.Name.StartsWith("*"))
                {
                    starIndex = i;
                    break;
                }
            }
            
            if (starIndex == -1)
            {
                // No star pattern - exact length match
                if (subjectItems.Count() != patterns.Count)
                    return false;
                    
                for (int i = 0; i < patterns.Count; i++)
                {
                    if (!MatchPattern(subjectItems[i], patterns[i], scope))
                        return false;
                }
                return true;
            }
            else
            {
                // Star pattern present - flexible length match
                var beforeStar = starIndex;
                var afterStar = patterns.Count - starIndex - 1;
                
                if (subjectItems.Count() < beforeStar + afterStar)
                    return false;
                
                // Match before star
                for (int i = 0; i < beforeStar; i++)
                {
                    if (!MatchPattern(subjectItems[i], patterns[i], scope))
                        return false;
                }
                
                // Match after star (from end)
                for (int i = 0; i < afterStar; i++)
                {
                    var subjectIdx = subjectItems.Count() - afterStar + i;
                    var patternIdx = starIndex + 1 + i;
                    if (!MatchPattern(subjectItems[subjectIdx], patterns[patternIdx], scope))
                        return false;
                }
                
                // Bind star variable
                var starPattern = patterns[starIndex] as NameExpression;
                if (starPattern != null)
                {
                    var starName = starPattern.Name.TrimStart('*');
                    var starItems = new List<PyObject>();
                    for (int i = beforeStar; i < subjectItems.Count() - afterStar; i++)
                    {
                        starItems.Add(subjectItems[i]);
                    }
                    scope.SetVariable(starName, new PyList(starItems));
                }
                
                return true;
            }
        }
        
        /// <summary>
        /// CPython 3.12: Match mapping patterns like {"key": value}
        /// </summary>
        private static bool MatchMappingPattern(PyObject subject, DictExpression dictExpr, PyScope scope)
        {
            if (!(subject is PyDict pyDict))
                return false;
            
            // Check all required keys and bind values
            foreach (var pair in dictExpr.Items)
            {
                // Key should be a constant expression
                if (!(pair.Key is ConstantExpression keyExpr))
                    return false;
                
                var keyObj = keyExpr.Value;
                if (!pyDict.Contains(keyObj).ToBool())
                    return false;
                
                var valueObj = pyDict.GetItem(keyObj);
                if (!MatchPattern(valueObj, pair.Value, scope))
                    return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// CPython 3.12: Match class patterns like Point(x, y)
        /// </summary>
        private static bool MatchClassPattern(PyObject subject, CallExpression callExpr, PyScope scope)
        {
            // Get the class name
            if (!(callExpr.Function is NameExpression classNameExpr))
                return false;
                
            var className = classNameExpr.Name;
            
            // Check if subject is an instance of the expected class
            if (!(subject is PyClass pyClass))
                return false;
            
            // Simple class name matching (could be enhanced with proper type checking)
            if (!pyClass.GetAttribute("__class__").ToString().Contains(className))
                return false;
            
            // Match positional arguments with object attributes
            // This is a simplified implementation - CPython 3.12 has more complex logic
            var args = callExpr.Arguments;
            for (int i = 0; i < args.Count; i++)
            {
                // Try to get attribute by position (simplified)
                try
                {
                    var attrName = $"arg{i}"; // This would need proper attribute name resolution
                    var attrValue = pyClass.GetAttribute(attrName);
                    if (!MatchPattern(attrValue, args[i], scope))
                        return false;
                }
                catch
                {
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// CPython 3.12: Match or patterns like (pattern1 | pattern2)
        /// </summary>
        private static bool MatchOrPattern(PyObject subject, OrPattern orPattern, PyScope scope)
        {
            // CPython 3.12: Check recursion depth to prevent infinite loops
            _orPatternDepth++;
            if (_orPatternDepth > MAX_OR_PATTERN_DEPTH)
            {
                _orPatternDepth--;
                throw PyRecursionError.Create("maximum recursion depth exceeded in pattern matching");
            }
            
            try
            {
                // Try each pattern in the or pattern
                // CPython 3.12: First matching pattern wins, no backtracking
                foreach (var pattern in orPattern.Patterns)
                {
                    // Create a temporary scope to avoid binding variables if pattern fails
                    var tempScope = new PyScope(ScopeType.Local, scope);
                    
                    if (MatchPattern(subject, pattern, tempScope))
                    {
                        // If pattern matches, copy bindings to actual scope
                        foreach (var binding in tempScope.Variables)
                        {
                            scope.SetVariable(binding.Key, binding.Value);
                        }
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                // CPython 3.12: Always decrement recursion depth
                _orPatternDepth--;
            }
        }
        
        #endregion
        
        #region f-string Execution
        
        /// <summary>
        /// f-string 실행 (기본적인 구현)
        /// </summary>
        public static PyObject ExecuteFString(FStringExpression expr, PyScope scope)
        {
            var parts = new List<string>();
            
            foreach (var value in expr.Values)
            {
                if (value is ConstantExpression constExpr && constExpr.Value is PyString pyStr)
                {
                    parts.Add(pyStr.Value);
                }
                else if (value is FormattedValue formattedValue)
                {
                    // FormattedValue는 자체적으로 포맷팅을 처리함
                    var evaluated = formattedValue.Evaluate(scope);
                    parts.Add(evaluated.ToStr());
                }
                else if (value is FormatExpression formatExpr)
                {
                    // FormatExpression는 포맷 지정자를 적용함
                    var evaluated = ExecuteFormattedValue(formatExpr, scope);
                    parts.Add(evaluated.ToStr());
                }
                else
                {
                    var evaluated = value.Evaluate(scope);
                    parts.Add(evaluated.ToStr());
                }
            }
            
            var result = string.Join("", parts);
            return new PyString(result);
        }

        /// <summary>
        /// FormatExpression 실행 (f-string 포맷 지정자 처리)
        /// </summary>
        public static PyObject ExecuteFormattedValue(FormatExpression expr, PyScope scope)
        {
            var value = expr.Value.Evaluate(scope);
            var formatSpec = expr.FormatSpec;

            // 간단한 포맷 지정자 처리
            if (value is PyFloat floatValue)
            {
                // .2f 같은 소수점 포맷 처리
                if (formatSpec.EndsWith("f") && formatSpec.Contains("."))
                {
                    var decimalPlaces = int.Parse(formatSpec.Substring(1, formatSpec.Length - 2));
                    var formatted = floatValue.Value.ToString($"F{decimalPlaces}");
                    return new PyString(formatted);
                }
            }
            else if (value is PyInt intValue)
            {
                // d 같은 정수 포맷 처리
                if (formatSpec == "d")
                {
                    return new PyString(intValue.Value.ToString());
                }
            }

            // 기본적으로 ToString() 사용
            return new PyString(value.ToStr());
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
            Console.WriteLine($"🔧 Executing generic function: {funcDef.Name}[{string.Join(", ", funcDef.TypeParams)}]");
            
            // 1. 제네릭 타입 정보 생성
            var typeInfo = new GenericTypeInfo(funcDef.TypeParams);
            
            // 2. 매개변수와 기본값 파싱
            var (paramNames, defaults) = ParseParametersAndDefaults(funcDef.Parameters, scope);
            
            // 3. 함수 본체를 바이트코드로 컴파일
            var compiler = new PythonCompiler();
            var bytecode = compiler.Compile(funcDef.Body, funcDef.Name, paramNames);
            
            // 4. 제네릭 함수 생성 - 실제 호출 가능한 구현
            var genericFunc = new PyGenericFunction(funcDef.Name, args =>
            {
                // 제네릭 함수 호출 시 타입 파라미터는 추론 또는 기본값 사용
                var functionScope = new PyScope(ScopeType.Local, scope, funcDef.Name);
                
                // 타입 파라미터를 함수 스코프에 추가 (임시로 문자열로 저장)
                foreach (var typeParam in funcDef.TypeParams)
                {
                    functionScope.SetVariable(typeParam, new PyString(typeParam));
                }
                
                // 함수 실행
                var vm = PyVM.Instance;
                var scopeChain = new PyScopeChain();
                scopeChain.PushScope(ScopeType.Local, funcDef.Name, scope);
                return vm.ExecuteModule(bytecode, scopeChain);
            }, typeInfo);
            
            // 5. 함수를 스코프에 등록
            scope.SetVariable(funcDef.Name, genericFunc);
            
            Console.WriteLine($"✅ Generic function complete: {funcDef.Name}[{string.Join(", ", funcDef.TypeParams)}]");
            return genericFunc;
        }
        
        /// <summary>
        /// 매개변수와 기본값을 분리하는 헬퍼 메서드
        /// </summary>
        private static (List<string> paramNames, List<PyObject> defaults) ParseParametersAndDefaults(
            List<string> parameters, PyScope scope)
        {
            var paramNames = new List<string>();
            var defaults = new List<PyObject>();
            
            foreach (var param in parameters)
            {
                if (param.Contains('='))
                {
                    var parts = param.Split('=', 2);
                    paramNames.Add(parts[0].Trim());
                    
                    // 기본값 파싱 (간단한 구현)
                    var defaultValue = parts[1].Trim();
                    if (defaultValue == "None")
                        defaults.Add(PyNone.Instance);
                    else if (int.TryParse(defaultValue, out int intVal))
                        defaults.Add(new PyInt(intVal));
                    else if (defaultValue.StartsWith('"') && defaultValue.EndsWith('"'))
                        defaults.Add(new PyString(defaultValue.Trim('"')));
                    else
                        defaults.Add(PyNone.Instance);
                }
                else
                {
                    paramNames.Add(param);
                }
            }
            
            return (paramNames, defaults);
        }
        
        /// <summary>
        /// 제네릭 클래스 실행
        /// </summary>
        public static PyObject ExecuteGenericClass(ClassDefStatement classDef, PyScope scope)
        {
            Console.WriteLine($"🔧 Executing generic class: {classDef.Name}[{string.Join(", ", classDef.TypeParams)}]");
            
            // 1. 상속 클래스 평가
            var baseTypes = new List<PyType>();
            foreach (var baseExpr in classDef.Bases)
            {
                var baseObj = baseExpr.Evaluate(scope);
                if (baseObj is PyType baseType)
                {
                    baseTypes.Add(baseType);
                }
                else
                {
                    throw PyTypeError.Create($"Base class must be a type, not {baseObj.GetTypeName()}");
                }
            }
            
            // 2. 메타클래스 처리
            PyType? metaclass = null;
            if (classDef.Metaclass != null)
            {
                var metaclassObj = classDef.Metaclass.Evaluate(scope);
                if (metaclassObj is PyType metaType)
                {
                    metaclass = metaType;
                }
            }
            
            // 3. 제네릭 타입 정보 생성
            var typeInfo = new GenericTypeInfo(classDef.TypeParams);
            
            // 4. 클래스 네임스페이스 생성 및 본체 실행
            var classNamespace = new Dictionary<string, PyObject>();
            var classScope = new PyScope(ScopeType.Class, scope, classDef.Name);
            
            // 5. 타입 파라미터를 클래스 스코프에 추가
            foreach (var typeParam in classDef.TypeParams)
            {
                // 타입 파라미터를 문자열로 저장 (실제 타입은 인스턴스화 시 결정)
                classScope.SetVariable(typeParam, new PyString(typeParam));
            }
            
            // 6. 클래스 본체 실행
            foreach (var stmt in classDef.Body)
            {
                var result = stmt.Evaluate(classScope);
                
                // 함수 정의는 클래스 네임스페이스에 추가
                if (stmt is FunctionDefStatement funcDef)
                {
                    classNamespace[funcDef.Name] = result;
                }
                else if (stmt is AssignStatement assignStmt)
                {
                    // 클래스 변수 할당 처리
                    var value = assignStmt.Value.Evaluate(classScope);
                    classNamespace[assignStmt.VariableName] = value;
                }
            }
            
            // 7. PyGenericClass 생성
            var genericClass = new PyGenericClass(classDef.Name, baseTypes.ToArray(), typeInfo);
            
            // 8. 네임스페이스를 클래스에 설정
            foreach (var kvp in classNamespace)
            {
                genericClass.SetAttribute(kvp.Key, kvp.Value);
            }
            
            // 9. 클래스를 스코프에 등록
            scope.SetVariable(classDef.Name, genericClass);
            
            Console.WriteLine($"✅ Generic class complete: {classDef.Name} with {classNamespace.Count} members");
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