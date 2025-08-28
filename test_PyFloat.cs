using System;
using SharpPy;

namespace SharpPy.Tests
{
    /// <summary>
    /// PyFloat 기능 테스트 및 기존 시스템과의 통합 검증
    /// </summary>
    public class PyFloatTest
    {
        public static void RunAllTests()
        {
            Console.WriteLine("🧪 PyFloat 통합 테스트 시작...\n");

            TestBasicCreation();
            TestStringRepresentation();
            TestArithmeticOperations();
            TestComparisonOperations();
            TestTypeConversion();
            TestSpecialValues();
            TestMathMethods();
            TestIntegrationWithExistingTypes();

            Console.WriteLine("✅ 모든 PyFloat 테스트 완료!\n");
        }

        static void TestBasicCreation()
        {
            Console.WriteLine("📝 기본 생성 테스트:");

            var f1 = new PyFloat(3.14);
            var f2 = new PyFloat(-2.5);
            var f3 = new PyFloat(0.0);

            Console.WriteLine($"  PyFloat(3.14) = {f1} (타입: {f1.GetTypeName()})");
            Console.WriteLine($"  PyFloat(-2.5) = {f2} (타입: {f2.GetTypeName()})");
            Console.WriteLine($"  PyFloat(0.0) = {f3} (타입: {f3.GetTypeName()})");
            Console.WriteLine();
        }

        static void TestStringRepresentation()
        {
            Console.WriteLine("📝 문자열 표현 테스트:");

            var f1 = new PyFloat(3.14159);
            var f2 = new PyFloat(10.0);
            var f3 = new PyFloat(double.PositiveInfinity);
            var f4 = new PyFloat(double.NaN);

            Console.WriteLine($"  PyFloat(3.14159).ToStr() = {f1.ToStr()}");
            Console.WriteLine($"  PyFloat(10.0).ToStr() = {f2.ToStr()}");
            Console.WriteLine($"  PyFloat(+∞).ToRepr() = {f3.ToRepr()}");
            Console.WriteLine($"  PyFloat(NaN).ToRepr() = {f4.ToRepr()}");
            Console.WriteLine();
        }

        static void TestArithmeticOperations()
        {
            Console.WriteLine("🧮 산술 연산 테스트:");

            var f1 = new PyFloat(5.5);
            var f2 = new PyFloat(2.0);
            var i1 = new PyInt(3);

            // PyFloat + PyFloat
            var add1 = f1.Add(f2) as PyFloat;
            Console.WriteLine($"  {f1} + {f2} = {add1}");

            // PyFloat + PyInt
            var add2 = f1.Add(i1) as PyFloat;
            Console.WriteLine($"  {f1} + {i1} = {add2}");

            // 나눗셈
            var div1 = f1.Divide(f2) as PyFloat;
            Console.WriteLine($"  {f1} / {f2} = {div1}");

            // 거듭제곱
            var pow1 = f1.Power(f2) as PyFloat;
            Console.WriteLine($"  {f1} ** {f2} = {pow1}");

            Console.WriteLine();
        }

        static void TestComparisonOperations()
        {
            Console.WriteLine("⚖️ 비교 연산 테스트:");

            var f1 = new PyFloat(3.14);
            var f2 = new PyFloat(2.71);
            var i1 = new PyInt(3);

            var eq1 = f1.RichCompare(new PyFloat(3.14), PyObject.CompareOp.EQ) as PyBool;
            var lt1 = f1.RichCompare(f2, PyObject.CompareOp.LT) as PyBool;
            var gt1 = f1.RichCompare(i1, PyObject.CompareOp.GT) as PyBool;

            Console.WriteLine($"  {f1} == 3.14: {eq1}");
            Console.WriteLine($"  {f1} < {f2}: {lt1}");
            Console.WriteLine($"  {f1} > {i1}: {gt1}");
            Console.WriteLine();
        }

        static void TestTypeConversion()
        {
            Console.WriteLine("🔄 타입 변환 테스트:");

            var f1 = new PyFloat(3.14);
            var f2 = new PyFloat(5.0);
            var f3 = new PyFloat(0.0);

            Console.WriteLine($"  int({f1}) = {f1.ToInt()}");
            Console.WriteLine($"  int({f2}) = {f2.ToInt()}");
            Console.WriteLine($"  bool({f1}) = {f1.PyBoolValue()}");
            Console.WriteLine($"  bool({f3}) = {f3.PyBoolValue()}");
            Console.WriteLine();
        }

        static void TestSpecialValues()
        {
            Console.WriteLine("✨ 특수 값 테스트:");

            var inf = PyFloat.PositiveInfinity;
            var negInf = PyFloat.NegativeInfinity;
            var nan = PyFloat.NaN;

            Console.WriteLine($"  +∞: {inf} (is_infinite: {inf.IsInfinite()})");
            Console.WriteLine($"  -∞: {negInf} (is_infinite: {negInf.IsInfinite()})");
            Console.WriteLine($"  NaN: {nan} (is_nan: {nan.IsNaN()})");

            // 문자열에서 생성
            try
            {
                var fromStr1 = PyFloat.FromString("3.14");
                var fromStr2 = PyFloat.FromString("inf");
                Console.WriteLine($"  FromString('3.14') = {fromStr1}");
                Console.WriteLine($"  FromString('inf') = {fromStr2}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"  문자열 파싱 오류: {e.Message}");
            }

            Console.WriteLine();
        }

        static void TestMathMethods()
        {
            Console.WriteLine("📐 수학 메서드 테스트:");

            var f1 = new PyFloat(3.14);
            var f2 = new PyFloat(5.0);
            var f3 = new PyFloat(double.PositiveInfinity);

            Console.WriteLine($"  {f1}.is_integer() = {f1.IsInteger()}");
            Console.WriteLine($"  {f2}.is_integer() = {f2.IsInteger()}");
            Console.WriteLine($"  {f3}.is_finite() = {f3.IsFinite()}");

            var abs1 = new PyFloat(-3.14).Absolute() as PyFloat;
            Console.WriteLine($"  abs(-3.14) = {abs1}");

            Console.WriteLine();
        }

        static void TestIntegrationWithExistingTypes()
        {
            Console.WriteLine("🔗 기존 타입과의 통합 테스트:");

            // PyObject 베이스 클래스 기능 테스트
            PyObject obj1 = new PyFloat(3.14);
            PyObject obj2 = new PyInt(3);

            Console.WriteLine($"  PyObject로 업캐스트: {obj1} (타입: {obj1.GetTypeName()})");
            Console.WriteLine($"  타입 검사: obj1.IsInstance<PyFloat>() = {obj1.IsInstance<PyFloat>()}");

            // 기존 PyInt와의 상호작용
            var result1 = ((PyFloat)obj1).Add(obj2);
            Console.WriteLine($"  PyFloat(3.14) + PyInt(3) = {result1}");

            // PyBool과의 상호작용
            var boolTrue = PyBool.True;
            var result2 = ((PyFloat)obj1).Multiply(boolTrue);
            Console.WriteLine($"  PyFloat(3.14) * PyBool(True) = {result2}");

            // 해시 테스트 (딕셔너리 키로 사용 가능한지)
            var hash1 = obj1.ToHash();
            var hash2 = new PyFloat(3.14).ToHash();
            Console.WriteLine($"  해시 일관성: {hash1} == {hash2} = {hash1 == hash2}");

            Console.WriteLine();
        }

        /// <summary>
        /// 에러 케이스 테스트
        /// </summary>
        static void TestErrorCases()
        {
            Console.WriteLine("❌ 에러 케이스 테스트:");

            try
            {
                var f1 = new PyFloat(5.0);
                var f2 = new PyFloat(0.0);
                var result = f1.Divide(f2); // 0으로 나누기
            }
            catch (Exception e)
            {
                Console.WriteLine($"  0으로 나누기 예외: {e.Message}");
            }

            try
            {
                var overflow = new PyFloat(double.MaxValue);
                var intResult = overflow.ToInt(); // 오버플로우
            }
            catch (Exception e)
            {
                Console.WriteLine($"  정수 변환 오버플로우: {e.Message}");
            }

            try
            {
                var invalidStr = PyFloat.FromString("not_a_number");
            }
            catch (Exception e)
            {
                Console.WriteLine($"  잘못된 문자열 파싱: {e.Message}");
            }

            Console.WriteLine();
        }
    }

    /// <summary>
    /// PyFloat 실용 예제 실행
    /// </summary>
    public class PyFloatDemo
    {
        public static void RunDemo()
        {
            try
            {
                PyFloatTest.RunAllTests();

                Console.WriteLine("🎯 추가 실용 예제:");
                
                // 실제 사용 시나리오
                var pi = new PyFloat(Math.PI);
                var radius = new PyFloat(5.0);
                
                var area = pi.Multiply(radius.Power(new PyFloat(2.0))) as PyFloat;
                Console.WriteLine($"  원의 넓이 (π × r²): π × 5² = {area}");

                var temperature_c = new PyFloat(25.0);
                var temperature_f = temperature_c.Multiply(new PyFloat(9.0/5.0)) as PyFloat;
                temperature_f = temperature_f.Add(new PyFloat(32.0)) as PyFloat;
                Console.WriteLine($"  섭씨 → 화씨: 25°C = {temperature_f}°F");

            }
            catch (Exception e)
            {
                Console.WriteLine($"테스트 실행 중 오류: {e.Message}");
                Console.WriteLine(e.StackTrace);
            }
        }
    }
}