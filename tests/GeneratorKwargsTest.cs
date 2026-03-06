using System;
using SharpPy;

namespace SharpPy.Tests
{
    /// <summary>
    /// Generator 키워드 인자 손실 버그 테스트
    ///
    /// 이슈: C#에서 PyGenerator.Next()를 직접 호출할 때 키워드 인자가 None으로 손실됨
    /// Python 내부에서 next()를 호출하면 정상 작동하지만,
    /// C#에서 직접 호출하면 문제 발생
    /// </summary>
    public static class GeneratorKwargsTest
    {
        public static void RunAllTests()
        {
            Console.WriteLine("=" + new string('=', 59));
            Console.WriteLine("C# Generator 키워드 인자 테스트");
            Console.WriteLine("=" + new string('=', 59));

            int passed = 0;
            int failed = 0;

            // 테스트 1: 기본 Generator 키워드 인자
            if (Test1_BasicGeneratorKwargs()) passed++; else failed++;

            // 테스트 2: 클래스 메서드 Generator 키워드 인자
            if (Test2_ClassMethodGeneratorKwargs()) passed++; else failed++;

            // 테스트 3: Eval을 통한 Generator 생성 후 C#에서 Next 호출
            if (Test3_EvalGeneratorThenCSharpNext()) passed++; else failed++;

            // 테스트 4: 복잡한 키워드 인자 (딕셔너리)
            if (Test4_ComplexKwargsDict()) passed++; else failed++;

            Console.WriteLine();
            Console.WriteLine("=" + new string('=', 59));
            Console.WriteLine($"테스트 결과: {passed} PASSED, {failed} FAILED");
            Console.WriteLine("=" + new string('=', 59));

            if (failed > 0)
            {
                Console.WriteLine(">>> 버그 재현됨! 수정 필요");
            }
            else
            {
                Console.WriteLine(">>> 모든 테스트 통과 - 버그가 현재 코드에서 재현되지 않음");
            }
        }

        /// <summary>
        /// 테스트 1: 기본 Generator 함수에 키워드 인자 전달
        /// </summary>
        private static bool Test1_BasicGeneratorKwargs()
        {
            Console.WriteLine("\n[TEST 1] 기본 Generator 키워드 인자");

            try
            {
                var interpreter = new IntegratedPythonInterpreter();

                // Generator 함수 정의
                interpreter.Execute(@"
def gen_func(a, b=None):
    yield {'a': a, 'b': b}
", "<test>", false, false, false);

                // Generator 함수 가져오기
                var genFunc = interpreter.GetGlobalVariable("gen_func") as PyFunction;
                if (genFunc == null)
                {
                    Console.WriteLine("  FAIL: gen_func를 찾을 수 없음");
                    return false;
                }

                // 키워드 인자와 함께 호출
                var kwargs = new PyDict();
                kwargs.SetItem(new PyStr("b"), new PyStr("test_value"));

                var generator = genFunc.Call(new PyObject[] { new PyStr("hello") }, kwargs);

                if (generator is not PyGenerator gen)
                {
                    Console.WriteLine($"  FAIL: Generator가 아님, 타입: {generator?.GetType().Name}");
                    return false;
                }

                // C#에서 직접 Next() 호출
                var result = gen.Next();

                if (result is not PyDict resultDict)
                {
                    Console.WriteLine($"  FAIL: 결과가 dict가 아님, 타입: {result?.GetType().Name}");
                    return false;
                }

                var bValue = resultDict.GetItem(new PyStr("b"));
                if (bValue is PyStr bStr && bStr.Value == "test_value")
                {
                    Console.WriteLine("  PASS");
                    return true;
                }
                else
                {
                    Console.WriteLine($"  FAIL: b 값이 예상과 다름. 예상: 'test_value', 실제: {bValue}");
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
        /// 테스트 2: 클래스 메서드 Generator에 키워드 인자 전달
        /// </summary>
        private static bool Test2_ClassMethodGeneratorKwargs()
        {
            Console.WriteLine("\n[TEST 2] 클래스 메서드 Generator 키워드 인자");

            try
            {
                var interpreter = new IntegratedPythonInterpreter();

                // 클래스와 Generator 메서드 정의
                interpreter.Execute(@"
class TestClass:
    def generator_method(self, equipment=None):
        yield equipment
", "<test>", false, false, false);

                // 클래스와 인스턴스 생성
                var testClass = interpreter.GetGlobalVariable("TestClass");
                if (testClass == null)
                {
                    Console.WriteLine("  FAIL: TestClass를 찾을 수 없음");
                    return false;
                }

                // 인스턴스 생성
                var instance = testClass.Call(new PyObject[0], null);

                // generator_method 가져오기
                var method = instance.GetAttribute("generator_method");

                // 키워드 인자 준비
                var kwargs = new PyDict();
                var equipmentDict = new PyDict();
                equipmentDict.SetItem(new PyStr("item_id"), new PyInt(54));
                equipmentDict.SetItem(new PyStr("name"), new PyStr("axe"));
                kwargs.SetItem(new PyStr("equipment"), equipmentDict);

                // 메서드 호출 (self는 이미 바인딩됨)
                var generator = method.Call(new PyObject[0], kwargs);

                if (generator is not PyGenerator gen)
                {
                    Console.WriteLine($"  FAIL: Generator가 아님, 타입: {generator?.GetType().Name}");
                    return false;
                }

                // C#에서 직접 Next() 호출
                var result = gen.Next();

                if (result is PyDict resultEquipment)
                {
                    var itemId = resultEquipment.GetItem(new PyStr("item_id"));
                    if (itemId is PyInt itemIdInt && itemIdInt.ToLong() == 54)
                    {
                        Console.WriteLine("  PASS");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"  FAIL: item_id가 예상과 다름. 예상: 54, 실제: {itemId}");
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
                    Console.WriteLine($"  FAIL: 예상치 못한 결과 타입: {result?.GetType().Name}, 값: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAIL: 예외 발생 - {ex.Message}");
                Console.WriteLine($"        {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// 테스트 3: Eval로 Generator 생성 후 C#에서 Next 호출
        /// (제보된 버그의 정확한 재현 경로)
        /// </summary>
        private static bool Test3_EvalGeneratorThenCSharpNext()
        {
            Console.WriteLine("\n[TEST 3] Eval로 Generator 생성 후 C#에서 Next 호출");

            try
            {
                var interpreter = new IntegratedPythonInterpreter();

                // 클래스 정의
                interpreter.Execute(@"
class Tree:
    def chop(self, equipment=None):
        print(f'[Tree.chop] equipment={equipment}')
        yield equipment

tree = Tree()
test_equipment = {'item_id': 54, 'unique_id': 'axe'}
", "<test>", false, false, false);

                // Eval로 Generator 생성 (C#에서 호출하는 방식 재현)
                var generator = interpreter.ExecuteEval("tree.chop(equipment=test_equipment)");

                if (generator is not PyGenerator gen)
                {
                    Console.WriteLine($"  FAIL: Generator가 아님, 타입: {generator?.GetType().Name}");
                    return false;
                }

                Console.WriteLine($"  Generator 생성됨: {gen}");

                // C#에서 직접 Next() 호출 (이 부분에서 버그 발생)
                var result = gen.Next();

                Console.WriteLine($"  Next() 결과: {result}");

                if (result is PyDict resultDict)
                {
                    var itemId = resultDict.GetItem(new PyStr("item_id"));
                    if (itemId is PyInt itemIdInt && itemIdInt.ToLong() == 54)
                    {
                        Console.WriteLine("  PASS");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"  FAIL: item_id가 예상과 다름. 예상: 54, 실제: {itemId}");
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
                Console.WriteLine($"        {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// 테스트 4: 복잡한 키워드 인자 (딕셔너리 포함)
        /// </summary>
        private static bool Test4_ComplexKwargsDict()
        {
            Console.WriteLine("\n[TEST 4] 복잡한 키워드 인자 (중첩 딕셔너리)");

            try
            {
                var interpreter = new IntegratedPythonInterpreter();

                interpreter.Execute(@"
def process_data(data=None, config=None):
    yield {'data': data, 'config': config}
", "<test>", false, false, false);

                var func = interpreter.GetGlobalVariable("process_data") as PyFunction;
                if (func == null)
                {
                    Console.WriteLine("  FAIL: process_data를 찾을 수 없음");
                    return false;
                }

                // 복잡한 키워드 인자 구성
                var kwargs = new PyDict();

                var dataDict = new PyDict();
                dataDict.SetItem(new PyStr("id"), new PyInt(100));
                dataDict.SetItem(new PyStr("name"), new PyStr("test"));

                var configDict = new PyDict();
                configDict.SetItem(new PyStr("enabled"), PyBool.True);
                configDict.SetItem(new PyStr("level"), new PyInt(5));

                kwargs.SetItem(new PyStr("data"), dataDict);
                kwargs.SetItem(new PyStr("config"), configDict);

                var generator = func.Call(new PyObject[0], kwargs);

                if (generator is not PyGenerator gen)
                {
                    Console.WriteLine($"  FAIL: Generator가 아님, 타입: {generator?.GetType().Name}");
                    return false;
                }

                var result = gen.Next();

                if (result is PyDict resultDict)
                {
                    var data = resultDict.GetItem(new PyStr("data"));
                    var config = resultDict.GetItem(new PyStr("config"));

                    if (data is PyDict && config is PyDict)
                    {
                        Console.WriteLine("  PASS");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"  FAIL: data 또는 config가 None. data={data}, config={config}");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine($"  FAIL: 결과가 dict가 아님. 타입: {result?.GetType().Name}");
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
