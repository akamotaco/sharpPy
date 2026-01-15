using System;
using SharpPy;

namespace SharpPy.Tests
{
    /// <summary>
    /// PyMethod kwargs 전달 버그 테스트
    ///
    /// 이슈: Python에서 bound method를 키워드 인자와 함께 호출할 때,
    ///       Generator 함수 내부에서 키워드 인자가 None으로 손실됨.
    ///
    /// 원인: PyVM.CallWithKeywords()에서 PyMethod 타입 처리 누락
    ///
    /// 재현 조건:
    /// 1. getattr()로 메서드를 가져옴 (PyMethod 생성)
    /// 2. 키워드 인자와 함께 호출
    /// 3. 메서드가 Generator 함수
    /// </summary>
    public static class PyMethodKwargsTest
    {
        public static void RunAllTests()
        {
            Console.WriteLine("=" + new string('=', 59));
            Console.WriteLine("PyMethod kwargs 전달 버그 테스트");
            Console.WriteLine("=" + new string('=', 59));

            int passed = 0;
            int failed = 0;

            // 테스트 1: getattr + kwargs + generator (핵심 버그 재현)
            if (Test1_GetattrKwargsGenerator()) passed++; else failed++;

            // 테스트 2: 직접 메서드 호출 (정상 작동해야 함)
            if (Test2_DirectMethodCall()) passed++; else failed++;

            // 테스트 3: 클로저 내부에서 kwargs 전달
            if (Test3_ClosureKwargs()) passed++; else failed++;

            // 테스트 4: morld 정확한 패턴 (클로저 + 파라미터)
            if (Test4_MorldExactPattern()) passed++; else failed++;

            // 테스트 5: 일반 함수 (Generator 아님) - 정상 작동해야 함
            if (Test5_NonGeneratorMethod()) passed++; else failed++;

            Console.WriteLine();
            Console.WriteLine("=" + new string('=', 59));
            Console.WriteLine($"테스트 결과: {passed} PASSED, {failed} FAILED");
            Console.WriteLine("=" + new string('=', 59));

            if (failed > 0)
            {
                Console.WriteLine(">>> 버그 재현됨! PyVM.CallWithKeywords()에서 PyMethod 처리 필요");
            }
            else
            {
                Console.WriteLine(">>> 모든 테스트 통과");
            }
        }

        /// <summary>
        /// 테스트 1: getattr + kwargs + generator (핵심 버그 재현)
        ///
        /// Python 코드:
        ///   obj = TestClass()
        ///   method = getattr(obj, "generator_method")
        ///   gen = method(equipment={"key": "value"})
        ///   result = next(gen)
        /// </summary>
        private static bool Test1_GetattrKwargsGenerator()
        {
            Console.WriteLine("\n[TEST 1] getattr + kwargs + generator (핵심 버그)");

            try
            {
                var interpreter = new IntegratedPythonInterpreter();

                // Python 코드로 테스트 (VM의 CallWithKeywords 경로 사용)
                interpreter.Execute(@"
class TestClass:
    def generator_method(self, equipment=None):
        yield equipment

obj = TestClass()
method = getattr(obj, 'generator_method')
gen = method(equipment={'key': 'value', 'num': 123})
result = next(gen)
", "<test>", false, false, false);

                var result = interpreter.GetGlobalVariable("result");

                if (result is PyDict resultDict)
                {
                    var keyValue = resultDict.GetItem(new PyString("key"));
                    if (keyValue is PyString keyStr && keyStr.Value == "value")
                    {
                        Console.WriteLine("  PASS");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"  FAIL: key 값이 예상과 다름. 실제: {keyValue}");
                        return false;
                    }
                }
                else if (result is PyNone)
                {
                    Console.WriteLine("  FAIL: equipment가 None으로 손실됨! (버그 재현)");
                    return false;
                }
                else
                {
                    Console.WriteLine($"  FAIL: 예상치 못한 결과. 타입: {result?.GetType().Name}, 값: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAIL: 예외 발생 - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 테스트 2: 직접 메서드 호출 (obj.method() 형태)
        /// 이 경우는 정상 작동해야 함
        /// </summary>
        private static bool Test2_DirectMethodCall()
        {
            Console.WriteLine("\n[TEST 2] 직접 메서드 호출 (obj.method())");

            try
            {
                var interpreter = new IntegratedPythonInterpreter();

                interpreter.Execute(@"
class TestClass:
    def generator_method(self, equipment=None):
        yield equipment

obj = TestClass()
gen = obj.generator_method(equipment={'key': 'direct'})
result = next(gen)
", "<test>", false, false, false);

                var result = interpreter.GetGlobalVariable("result");

                if (result is PyDict resultDict)
                {
                    var keyValue = resultDict.GetItem(new PyString("key"));
                    if (keyValue is PyString keyStr && keyStr.Value == "direct")
                    {
                        Console.WriteLine("  PASS");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"  FAIL: key 값이 예상과 다름. 실제: {keyValue}");
                        return false;
                    }
                }
                else if (result is PyNone)
                {
                    Console.WriteLine("  FAIL: equipment가 None으로 손실됨!");
                    return false;
                }
                else
                {
                    Console.WriteLine($"  FAIL: 예상치 못한 결과. 타입: {result?.GetType().Name}, 값: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAIL: 예외 발생 - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 테스트 3: 클로저 내부에서 kwargs 전달 (morld의 실제 패턴)
        /// </summary>
        private static bool Test3_ClosureKwargs()
        {
            Console.WriteLine("\n[TEST 3] 클로저 내부에서 kwargs 전달");

            try
            {
                var interpreter = new IntegratedPythonInterpreter();

                interpreter.Execute(@"
class TestClass:
    def generator_method(self, equipment=None):
        yield equipment

def call_with_closure(equipment=None):
    obj = TestClass()
    method = getattr(obj, 'generator_method')

    def _call_method():
        return method(equipment=equipment)

    return _call_method()

gen = call_with_closure(equipment={'key': 'closure'})
result = next(gen)
", "<test>", false, false, false);

                var result = interpreter.GetGlobalVariable("result");

                if (result is PyDict resultDict)
                {
                    var keyValue = resultDict.GetItem(new PyString("key"));
                    if (keyValue is PyString keyStr && keyStr.Value == "closure")
                    {
                        Console.WriteLine("  PASS");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"  FAIL: key 값이 예상과 다름. 실제: {keyValue}");
                        return false;
                    }
                }
                else if (result is PyNone)
                {
                    Console.WriteLine("  FAIL: equipment가 None으로 손실됨! (버그 재현)");
                    return false;
                }
                else
                {
                    Console.WriteLine($"  FAIL: 예상치 못한 결과. 타입: {result?.GetType().Name}, 값: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAIL: 예외 발생 - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 테스트 4: morld 정확한 패턴 재현 (클로저 + 파라미터 있음)
        /// </summary>
        private static bool Test4_MorldExactPattern()
        {
            Console.WriteLine("\n[TEST 4] morld 정확한 패턴 (클로저 + 파라미터)");

            try
            {
                var interpreter = new IntegratedPythonInterpreter();

                // morld의 assets/__init__.py와 동일한 패턴
                interpreter.Execute(@"
class TestClass:
    def generator_method(self, equipment=None):
        print(f'generator_method called: equipment={equipment}')
        yield equipment

def call_instance_method(instance_id, method_name, args=None, equipment=None):
    if args is None:
        args = []
    print(f'call_instance_method: equipment={equipment}')

    def _call_method(instance, method):
        # equipment는 클로저 변수
        print(f'_call_method: equipment={equipment}')
        if equipment is not None:
            return method(*args, equipment=equipment)
        else:
            return method(*args)

    instance = TestClass()
    method = getattr(instance, method_name)
    return _call_method(instance, method)

gen = call_instance_method(123, 'generator_method', equipment={'key': 'morld'})
result = next(gen)
print(f'result={result}')
", "<test>", false, false, false);

                var result = interpreter.GetGlobalVariable("result");

                if (result is PyDict resultDict)
                {
                    var keyValue = resultDict.GetItem(new PyString("key"));
                    if (keyValue is PyString keyStr && keyStr.Value == "morld")
                    {
                        Console.WriteLine("  PASS");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"  FAIL: key 값이 예상과 다름. 실제: {keyValue}");
                        return false;
                    }
                }
                else if (result is PyNone)
                {
                    Console.WriteLine("  FAIL: equipment가 None으로 손실됨! (버그 재현)");
                    return false;
                }
                else
                {
                    Console.WriteLine($"  FAIL: 예상치 못한 결과. 타입: {result?.GetType().Name}, 값: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAIL: 예외 발생 - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 테스트 5: 일반 함수 (Generator 아님)
        /// Generator가 아닌 경우도 동일한 문제가 있는지 확인
        /// </summary>
        private static bool Test5_NonGeneratorMethod()
        {
            Console.WriteLine("\n[TEST 4] 일반 함수 (Generator 아님)");

            try
            {
                var interpreter = new IntegratedPythonInterpreter();

                interpreter.Execute(@"
class TestClass:
    def normal_method(self, equipment=None):
        return equipment

obj = TestClass()
method = getattr(obj, 'normal_method')
result = method(equipment={'key': 'normal'})
", "<test>", false, false, false);

                var result = interpreter.GetGlobalVariable("result");

                if (result is PyDict resultDict)
                {
                    var keyValue = resultDict.GetItem(new PyString("key"));
                    if (keyValue is PyString keyStr && keyStr.Value == "normal")
                    {
                        Console.WriteLine("  PASS");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"  FAIL: key 값이 예상과 다름. 실제: {keyValue}");
                        return false;
                    }
                }
                else if (result is PyNone)
                {
                    Console.WriteLine("  FAIL: equipment가 None으로 손실됨!");
                    return false;
                }
                else
                {
                    Console.WriteLine($"  FAIL: 예상치 못한 결과. 타입: {result?.GetType().Name}, 값: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAIL: 예외 발생 - {ex.Message}");
                return false;
            }
        }
    }
}
