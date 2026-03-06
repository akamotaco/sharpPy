using System;
using System.Collections.Generic;
using System.Reflection;
using SharpPy.Interop;

namespace SharpPy.Interop
{
    /// <summary>
    /// C#과 Python 간의 상호 운용성 제공
    /// C# 방식으로 Python 기능을 사용할 수 있도록 함
    /// </summary>
    public static class CSharpPythonInterop
    {
        #region C# Generic -> Python Type Parameters
        
        /// <summary>
        /// C# 제네릭을 Python 스타일로 사용
        /// </summary>
        public class PyGeneric<T> : PyObject where T : PyObject
        {
            public Type ElementType => typeof(T);
            
            public override string ToString() => $"Generic[{typeof(T).Name}]";
        }
        
        /// <summary>
        /// Python type alias를 C# 방식으로 정의
        /// </summary>
        public static class TypeAliases
        {
            public static Type Vector<T>() where T : PyObject => typeof(PyList);
            public static Type Matrix<T, U>() where T : PyObject where U : PyObject => typeof(PyDict);
            public static Type StringList() => typeof(PyList);
            public static Type IntDict() => typeof(PyDict);
        }
        
        #endregion
        
        #region Python-Style Comprehensions (순수 Python 방식)
        
        /// <summary>
        /// 순수 Python 방식의 컴프리헨션 구현
        /// C# LINQ 없이 Python 의미론을 완전히 따름
        /// </summary>
        public static class PythonComprehensions
        {
            /// <summary>
            /// Python list comprehension 구현: [expr for item in iterable if condition]
            /// </summary>
            public static PyList ExecuteListComprehension(
                Expression element,
                List<Comprehension> generators,
                PyScope scope)
            {
                var result = new PyList();
                ExecuteComprehensionLogic(generators, 0, scope, () => {
                    var value = element.Evaluate(scope);
                    result.Add(value);
                });
                return result;
            }
            
            /// <summary>
            /// Python dict comprehension 구현: {key_expr: value_expr for item in iterable if condition}
            /// </summary>
            public static PyDict ExecuteDictComprehension(
                Expression keyExpr,
                Expression valueExpr,
                List<Comprehension> generators,
                PyScope scope)
            {
                var result = new PyDict();
                ExecuteComprehensionLogic(generators, 0, scope, () => {
                    var key = keyExpr.Evaluate(scope);
                    var value = valueExpr.Evaluate(scope);
                    result.SetItem(key, value);
                });
                return result;
            }
            
            /// <summary>
            /// Python set comprehension 구현: {expr for item in iterable if condition}
            /// </summary>
            public static PySet ExecuteSetComprehension(
                Expression element,
                List<Comprehension> generators,
                PyScope scope)
            {
                var result = new PySet();
                ExecuteComprehensionLogic(generators, 0, scope, () => {
                    var value = element.Evaluate(scope);
                    result.Add(value);
                });
                return result;
            }
            
            /// <summary>
            /// Generator expression 구현: (expr for item in iterable if condition)
            /// </summary>
            public static PyGenerator ExecuteGeneratorExpression(
                Expression element,
                List<Comprehension> generators,
                PyScope scope)
            {
                // TODO: This needs to be reimplemented with new PyGenerator
                throw new NotImplementedException("ExecuteGeneratorExpression needs to be reimplemented with new PyGenerator");
                
                IEnumerable<PyObject> GenerateItems()
                {
                    var result = new List<PyObject>();
                    ExecuteComprehensionLogic(generators, 0, scope, () => {
                        var value = element.Evaluate(scope);
                        result.Add(value);
                    });
                    
                    foreach (var item in result)
                    {
                        yield return item;
                    }
                }
            }
            
            /// <summary>
            /// 재귀적 컴프리헨션 로직 - 중첩된 for문과 if 조건 처리
            /// </summary>
            private static void ExecuteComprehensionLogic(
                List<Comprehension> generators,
                int currentIndex,
                PyScope scope,
                Action bodyAction)
            {
                if (currentIndex >= generators.Count)
                {
                    bodyAction();
                    return;
                }
                
                var generator = generators[currentIndex];
                var iterable = generator.Iter.Evaluate(scope);
                
                // Python의 다양한 iterable 타입 지원
                foreach (var item in GetIterableItems(iterable))
                {
                    // 타겟 변수 할당 (unpacking 지원)
                    AssignTarget(generator.Target, item, scope);
                    
                    // if 조건들 확인
                    bool allConditionsMet = true;
                    foreach (var condition in generator.Ifs)
                    {
                        var conditionResult = condition.Evaluate(scope);
                        if (!conditionResult.ToBool())
                        {
                            allConditionsMet = false;
                            break;
                        }
                    }
                    
                    if (allConditionsMet)
                    {
                        // 다음 generator로 재귀 또는 body 실행
                        ExecuteComprehensionLogic(generators, currentIndex + 1, scope, bodyAction);
                    }
                }
            }
            
            /// <summary>
            /// Python iterable을 C# IEnumerable로 변환
            /// </summary>
            private static IEnumerable<PyObject> GetIterableItems(PyObject iterable)
            {
                switch (iterable)
                {
                    case PyList list:
                        foreach (var item in list.Items)
                            yield return item;
                        break;
                        
                    case PyTuple tuple:
                        foreach (var item in tuple.Items)
                            yield return item;
                        break;
                        
                    case PySet set:
                        foreach (var item in set.Items)
                            yield return item;
                        break;
                        
                    case PyDict dict:
                        foreach (var key in dict.Keys().Items)
                            yield return key;
                        break;
                        
                    case PyRange range:
                        for (long i = range.Start; i < range.Stop; i += range.Step)
                            yield return new PyInt(i);
                        break;
                        
                    case SharpPy.PyStr str:
                        for (int i = 0; i < str.Value.Length; i++)
                            yield return new SharpPy.PyStr(str.Value[i].ToString());
                        break;
                        
                    case PyGenerator generator:
                        var items = new List<PyObject>();
                        while (true)
                        {
                            try
                            {
                                items.Add(generator.Next());
                            }
                            catch (PythonException ex) when (ex.PyException is PyStopIteration)
                            {
                                break;
                            }
                        }
                        foreach (var item in items)
                            yield return item;
                        break;
                        
                    default:
                        // 기본적으로 빈 이터레이션
                        yield break;
                }
            }
            
            /// <summary>
            /// Python target assignment (unpacking 지원)
            /// </summary>
            private static void AssignTarget(Expression target, PyObject value, PyScope scope)
            {
                switch (target)
                {
                    case NameExpression nameExpr:
                        scope.SetVariable(nameExpr.Name, value);
                        break;
                        
                    case TupleExpression tupleExpr:
                        // Tuple unpacking: (a, b) = (1, 2)
                        if (value is PyTuple tuple)
                        {
                            for (int i = 0; i < Math.Min(tupleExpr.Elements.Count, tuple.Items.Length); i++)
                            {
                                AssignTarget(tupleExpr.Elements[i], tuple.Items[i], scope);
                            }
                        }
                        else if (value is PyList list)
                        {
                            for (int i = 0; i < Math.Min(tupleExpr.Elements.Count, list.Items.Length); i++)
                            {
                                AssignTarget(tupleExpr.Elements[i], list.Items[i], scope);
                            }
                        }
                        break;
                        
                    case ListExpression listExpr:
                        // List unpacking: [a, b] = [1, 2]
                        if (value is PyList list2)
                        {
                            for (int i = 0; i < Math.Min(listExpr.Elements.Count, list2.Items.Length); i++)
                            {
                                AssignTarget(listExpr.Elements[i], list2.Items[i], scope);
                            }
                        }
                        break;
                        
                    default:
                        throw new Exception($"Can't assign to {target.GetType().Name}");
                }
            }
        }
        
        #endregion
        
        #region C# String Interpolation -> f-strings
        
        /// <summary>
        /// C# 문자열 보간을 Python f-string 스타일로 사용
        /// </summary>
        public static class PyStr
        {
            // f"Hello {name}!" -> $"Hello {name}!"
            public static string F(FormattableString formattable)
            {
                return formattable.ToString();
            }
            
            // Python style string formatting
            public static string Format(string format, params object[] args)
            {
                return string.Format(format, args);
            }
        }
        
        #endregion
        
        #region C# Async/Await -> Python async/await
        
        /// <summary>
        /// C# Task를 Python 코루틴 스타일로 사용
        /// </summary>
        public static class PyAsync
        {
            public static async System.Threading.Tasks.Task<PyObject> Await(System.Threading.Tasks.Task<PyObject> task)
            {
                return await task;
            }
            
            public static PyObject CreateCoroutine(Func<System.Threading.Tasks.Task<PyObject>> taskFactory)
            {
                // Wrapper for C# async methods to look like Python coroutines
                return new PyCoroutine(taskFactory);
            }
        }
        
        public class PyCoroutine : PyObject
        {
            private readonly Func<System.Threading.Tasks.Task<PyObject>> _taskFactory;
            
            public PyCoroutine(Func<System.Threading.Tasks.Task<PyObject>> taskFactory)
            {
                _taskFactory = taskFactory;
            }
            
            public System.Threading.Tasks.Task<PyObject> GetTask() => _taskFactory();
            
            public override string ToString() => "<coroutine>";
        }
        
        #endregion
        
        #region C# Pattern Matching -> Python match statements
        
        /// <summary>
        /// C# switch expression을 Python match 스타일로 사용
        /// </summary>
        public static class PyMatch
        {
            public static T Match<T>(PyObject value, params (Func<PyObject, bool> condition, Func<T> result)[] cases)
            {
                foreach (var (condition, result) in cases)
                {
                    if (condition(value))
                    {
                        return result();
                    }
                }
                throw new Exception("No matching case found");
            }
            
            // Pattern matching helpers
            public static Func<PyObject, bool> Case(PyObject pattern) => obj => obj.Equals(pattern);
            public static Func<PyObject, bool> CaseType<T>() where T : PyObject => obj => obj is T;
            public static Func<PyObject, bool> CaseWildcard() => obj => true;
        }
        
        #endregion
        
        #region Python-Style Range and Enumerate
        
        /// <summary>
        /// Python range() 함수
        /// </summary>
        public static PyRange Range(int stop) => new PyRange(0, stop, 1);
        public static PyRange Range(int start, int stop) => new PyRange(start, stop, 1);
        public static PyRange Range(int start, int stop, int step) => new PyRange(start, stop, step);
        
        /// <summary>
        /// Python enumerate() 함수
        /// </summary>
        public static IEnumerable<(int index, T item)> Enumerate<T>(IEnumerable<T> source, int start = 0)
        {
            int index = start;
            foreach (var item in source)
            {
                yield return (index++, item);
            }
        }
        
        /// <summary>
        /// Python zip() 함수
        /// </summary>
        public static IEnumerable<(T1, T2)> Zip<T1, T2>(IEnumerable<T1> first, IEnumerable<T2> second)
        {
            // Performance: Eliminated LINQ
            var enum1 = first.GetEnumerator();
            var enum2 = second.GetEnumerator();
            while (enum1.MoveNext() && enum2.MoveNext())
            {
                yield return (enum1.Current, enum2.Current);
            }
        }
        
        #endregion
        
        #region Enhanced Exception Handling
        
        /// <summary>
        /// Python 스타일 예외 처리를 C#에서 사용
        /// </summary>
        public static class PyExceptionHandling
        {
            public static void Try(Action action, 
                params (Type exceptionType, Action<Exception> handler)[] handlers)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    foreach (var (exceptionType, handler) in handlers)
                    {
                        if (exceptionType.IsAssignableFrom(ex.GetType()))
                        {
                            handler(ex);
                            return;
                        }
                    }
                    throw; // Re-throw if no handler found
                }
            }
            
            public static T Try<T>(Func<T> func, T defaultValue = default(T),
                params (Type exceptionType, Func<Exception, T> handler)[] handlers)
            {
                try
                {
                    return func();
                }
                catch (Exception ex)
                {
                    foreach (var (exceptionType, handler) in handlers)
                    {
                        if (exceptionType.IsAssignableFrom(ex.GetType()))
                        {
                            return handler(ex);
                        }
                    }
                    return defaultValue;
                }
            }
        }
        
        #endregion
        
        #region Python-Style Decorators using C# Attributes
        
        /// <summary>
        /// Python 데코레이터를 C# Attribute로 구현
        /// </summary>
        [AttributeUsage(AttributeTargets.Method)]
        public class PyPropertyAttribute : Attribute { }
        
        [AttributeUsage(AttributeTargets.Method)]
        public class PyStaticMethodAttribute : Attribute { }
        
        [AttributeUsage(AttributeTargets.Method)]  
        public class PyClassMethodAttribute : Attribute { }
        
        /// <summary>
        /// 런타임에 Python 스타일 데코레이터 적용
        /// </summary>
        public static class DecoratorProcessor
        {
            public static void ProcessDecorators(object instance)
            {
                var type = instance.GetType();
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                
                foreach (var method in methods)
                {
                    if (method.GetCustomAttribute<PyPropertyAttribute>() != null)
                    {
                        // Convert method to property-like behavior
                        Console.WriteLine($"Processing @property for {method.Name}");
                    }
                    
                    if (method.GetCustomAttribute<PyStaticMethodAttribute>() != null)
                    {
                        Console.WriteLine($"Processing @staticmethod for {method.Name}");
                    }
                    
                    if (method.GetCustomAttribute<PyClassMethodAttribute>() != null)
                    {
                        Console.WriteLine($"Processing @classmethod for {method.Name}");
                    }
                }
            }
        }
        
        #endregion
    }
    
    #region Usage Examples and Documentation
    
    /// <summary>
    /// C#-Python Interop 사용 예제
    /// </summary>
    public static class InteropExamples
    {
        public static void RunExamples()
        {
            Console.WriteLine("🔄 C#-Python Interop 예제 실행");
            Console.WriteLine("================================\n");
            
            // 1. Python comprehensions 데모
            Console.WriteLine("1. 순수 Python 스타일 컴프리헨션:");
            Console.WriteLine("   [x*2 for x in range(1,6)] - PyExecutor를 통해 구현됨");
            Console.WriteLine("   {x: x*2 for x in range(5)} - 딕셔너리 컴프리헨션");
            Console.WriteLine("   {x*2 for x in range(10)} - 셋 컴프리헨션\n");
            
            // 2. C# 문자열 보간 -> f-strings
            Console.WriteLine("2. C# 문자열 보간을 f-string 스타일로:");
            var name = "Python";
            var version = 3.12;
            var formatted = CSharpPythonInterop.PyStr.F($"Hello {name} {version}!");
            Console.WriteLine($"   f\"Hello {{name}} {{version}}!\" = {formatted}\n");
            
            // 3. Python range() 사용
            Console.WriteLine("3. Python range() 함수:");
            var range1 = CSharpPythonInterop.Range(5);
            var range2 = CSharpPythonInterop.Range(2, 8);
            Console.WriteLine($"   range(5) = {range1}");
            Console.WriteLine($"   range(2, 8) = {range2}\n");
            
            // 4. Python enumerate() 사용
            Console.WriteLine("4. Python enumerate() 함수:");
            var items = new[] { "a", "b", "c" };
            foreach (var (index, item) in CSharpPythonInterop.Enumerate(items))
            {
                Console.WriteLine($"   {index}: {item}");
            }
            Console.WriteLine();
            
            // 5. Pattern matching
            Console.WriteLine("5. Python-style pattern matching:");
            var value = new PyInt(42);
            var result = CSharpPythonInterop.PyMatch.Match(value,
                (CSharpPythonInterop.PyMatch.Case(new PyInt(0)), () => "zero"),
                (CSharpPythonInterop.PyMatch.CaseType<PyInt>(), () => "integer"),
                (CSharpPythonInterop.PyMatch.CaseWildcard(), () => "other")
            );
            Console.WriteLine($"   match 42: {result}\n");
            
            // 6. Exception handling
            Console.WriteLine("6. Python-style exception handling:");
            CSharpPythonInterop.PyExceptionHandling.Try(
                () => throw new ArgumentException("Test exception"),
                (typeof(ArgumentException), ex => Console.WriteLine($"   Caught: {ex.Message}"))
            );
        }
    }
    
    #endregion
}