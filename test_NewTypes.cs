using System;
using System.Linq;
using SharpPy;

namespace SharpPy.Tests
{
    /// <summary>
    /// 새롭게 구현된 Python 타입들의 테스트 클래스
    /// </summary>
    public class NewTypesTest
    {
        public static void RunAllTests()
        {
            Console.WriteLine("🧪 새로운 Python 타입 테스트 시작...\n");

            TestPySet();
            TestPyFrozenSet();
            TestPyRange();
            TestPySlice();
            TestPyComplex();
            TestPyIterator();
            TestPyGenerator();

            Console.WriteLine("✅ 모든 새로운 타입 테스트 완료!\n");
        }

        static void TestPySet()
        {
            Console.WriteLine("🔶 PySet 테스트:");
            
            // 기본 생성 및 추가
            var set1 = new PySet();
            set1.Add(new PyInt(1));
            set1.Add(new PyInt(2));
            set1.Add(new PyInt(3));
            set1.Add(new PyInt(2)); // 중복 무시
            Console.WriteLine($"  set1 = {set1} (길이: {set1.Length()})");

            // 다른 집합 생성
            var set2 = new PySet(new PyObject[] { new PyInt(2), new PyInt(3), new PyInt(4) });
            Console.WriteLine($"  set2 = {set2}");

            // 집합 연산
            var union = set1.Union(set2);
            var intersection = set1.Intersection(set2);
            var difference = set1.Difference(set2);
            
            Console.WriteLine($"  합집합: {union}");
            Console.WriteLine($"  교집합: {intersection}");
            Console.WriteLine($"  차집합: {difference}");

            // 포함 확인
            var contains = set1.Contains(new PyInt(2));
            Console.WriteLine($"  2 in set1: {contains}");

            Console.WriteLine();
        }

        static void TestPyFrozenSet()
        {
            Console.WriteLine("❄️ PyFrozenSet 테스트:");
            
            var frozenSet = new PyFrozenSet(new PyObject[] { new PyInt(1), new PyInt(2), new PyInt(3) });
            Console.WriteLine($"  frozenset = {frozenSet}");
            Console.WriteLine($"  해시값: {frozenSet.ToHash()}");
            
            // 불변 집합 연산
            var union = frozenSet.Union(new PyFrozenSet(new PyObject[] { new PyInt(4), new PyInt(5) }));
            Console.WriteLine($"  합집합: {union}");

            Console.WriteLine();
        }

        static void TestPyRange()
        {
            Console.WriteLine("📏 PyRange 테스트:");
            
            // 다양한 range 생성
            var range1 = new PyRange(5);           // range(5)
            var range2 = new PyRange(2, 8);        // range(2, 8)
            var range3 = new PyRange(0, 10, 2);    // range(0, 10, 2)
            var range4 = new PyRange(10, 0, -1);   // range(10, 0, -1)

            Console.WriteLine($"  range(5) = {range1} (길이: {range1.Length()})");
            Console.WriteLine($"  range(2, 8) = {range2} (길이: {range2.Length()})");
            Console.WriteLine($"  range(0, 10, 2) = {range3} (길이: {range3.Length()})");
            Console.WriteLine($"  range(10, 0, -1) = {range4} (길이: {range4.Length()})");

            // 인덱싱
            Console.WriteLine($"  range1[2] = {range1.GetItem(2)}");
            Console.WriteLine($"  range3[3] = {range3.GetItem(3)}");

            // 포함 확인
            Console.WriteLine($"  4 in range1: {range1.Contains(new PyInt(4))}");
            Console.WriteLine($"  6 in range3: {range3.Contains(new PyInt(6))}");

            // 리스트 변환
            var list = range2.ToList();
            Console.WriteLine($"  list(range2) = {list}");

            Console.WriteLine();
        }

        static void TestPySlice()
        {
            Console.WriteLine("✂️ PySlice 테스트:");
            
            // 다양한 슬라이스 생성
            var slice1 = new PySlice(new PyInt(2), new PyInt(8));           // [2:8]
            var slice2 = new PySlice(PyNone.Instance, new PyInt(5));        // [:5]
            var slice3 = new PySlice(new PyInt(1), new PyInt(10), new PyInt(2)); // [1:10:2]

            Console.WriteLine($"  slice(2, 8) = {slice1}");
            Console.WriteLine($"  slice(None, 5) = {slice2}");
            Console.WriteLine($"  slice(1, 10, 2) = {slice3}");

            // 인덱스 계산
            var (start1, stop1, step1) = slice1.Indices(10);
            Console.WriteLine($"  slice1.indices(10) = ({start1}, {stop1}, {step1})");

            var length = slice3.GetLength(10);
            Console.WriteLine($"  slice3 길이 (시퀀스 길이 10): {length}");

            // 실제 인덱스들
            var indices = slice3.GetIndices(10);
            Console.WriteLine($"  slice3 인덱스들: [{string.Join(", ", indices)}]");

            Console.WriteLine();
        }

        static void TestPyComplex()
        {
            Console.WriteLine("🔢 PyComplex 테스트:");
            
            // 복소수 생성
            var c1 = new PyComplex(3, 4);      // 3+4j
            var c2 = new PyComplex(1, -2);     // 1-2j
            var c3 = new PyComplex(5, 0);      // 5+0j

            Console.WriteLine($"  c1 = {c1}");
            Console.WriteLine($"  c2 = {c2}");
            Console.WriteLine($"  c3 = {c3}");

            // 산술 연산
            var add = c1.Add(c2) as PyComplex;
            var mul = c1.Multiply(c2) as PyComplex;
            var abs_c1 = c1.Absolute() as PyFloat;

            Console.WriteLine($"  c1 + c2 = {add}");
            Console.WriteLine($"  c1 * c2 = {mul}");
            Console.WriteLine($"  abs(c1) = {abs_c1}");

            // 복소수 메서드
            var conjugate = c1.Conjugate();
            var magnitude = c1.Magnitude();
            var phase = c1.Phase();

            Console.WriteLine($"  c1.conjugate() = {conjugate}");
            Console.WriteLine($"  c1 크기 = {magnitude}");
            Console.WriteLine($"  c1 위상 = {phase}");

            // 특수 값
            var inf = new PyComplex(double.PositiveInfinity, 0);
            var nan = new PyComplex(double.NaN, 1);
            Console.WriteLine($"  복소수 무한대: {inf}");
            Console.WriteLine($"  복소수 NaN: {nan}");

            Console.WriteLine();
        }

        static void TestPyIterator()
        {
            Console.WriteLine("🔄 PyIterator 테스트:");
            
            // 리스트 이터레이터
            var list = new PyList(new PyObject[] { new PyInt(1), new PyInt(2), new PyInt(3) });
            var listIter = new PyListIterator(list);
            
            Console.WriteLine("  리스트 이터레이터:");
            try
            {
                while (true)
                {
                    var value = listIter.Next();
                    Console.WriteLine($"    next() = {value}");
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                Console.WriteLine("    StopIteration 발생");
            }

            // 문자열 이터레이터
            var str = new PyString("hello");
            var strIter = new PyStringIterator(str);
            
            Console.WriteLine("  문자열 이터레이터:");
            try
            {
                while (true)
                {
                    var value = strIter.Next();
                    Console.WriteLine($"    next() = {value}");
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                Console.WriteLine("    StopIteration 발생");
            }

            // Range 이터레이터
            var range = new PyRange(3);
            var rangeIter = new PyRangeIterator(range);
            
            Console.WriteLine("  Range 이터레이터:");
            try
            {
                while (true)
                {
                    var value = rangeIter.Next();
                    Console.WriteLine($"    next() = {value}");
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                Console.WriteLine("    StopIteration 발생");
            }

            Console.WriteLine();
        }

        static void TestPyGenerator()
        {
            Console.WriteLine("⚡ PyGenerator 테스트:");
            
            // 시퀀스로부터 제너레이터 생성
            var sequence = new PyObject[] { new PyInt(1), new PyInt(2), new PyInt(3) };
            var gen = PyGenerator.FromSequence(sequence, "test_generator");
            
            Console.WriteLine($"  제너레이터: {gen}");
            Console.WriteLine("  제너레이터 값들:");
            
            try
            {
                while (!gen.IsFinished)
                {
                    var value = gen.Next();
                    Console.WriteLine($"    next() = {value}");
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                Console.WriteLine("    StopIteration 발생");
            }

            // Range 제너레이터
            var rangeGen = GeneratorHelpers.RangeGenerator(0, 5, 2);
            Console.WriteLine("  Range 제너레이터 (0, 5, 2):");
            
            try
            {
                while (!rangeGen.IsFinished)
                {
                    var value = rangeGen.Next();
                    Console.WriteLine($"    next() = {value}");
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                Console.WriteLine("    StopIteration 발생");
            }

            // Send 테스트
            var sendGen = PyGenerator.FromSequence(new PyObject[] { new PyInt(10), new PyInt(20) }, "send_test");
            Console.WriteLine("  Send 테스트:");
            try
            {
                var val1 = sendGen.Send(new PyString("첫 번째"));
                Console.WriteLine($"    send('첫 번째') = {val1}");
                var val2 = sendGen.Send(new PyString("두 번째"));
                Console.WriteLine($"    send('두 번째') = {val2}");
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                Console.WriteLine("    StopIteration 발생");
            }

            Console.WriteLine();
        }
    }

    /// <summary>
    /// 새로운 타입들의 통합 테스트 실행
    /// </summary>
    public class NewTypesDemo
    {
        public static void RunDemo()
        {
            try
            {
                NewTypesTest.RunAllTests();

                Console.WriteLine("🎯 추가 통합 테스트:");
                
                // 타입 간 상호작용 테스트
                TestTypeInteractions();
                
                // 실용적 사용 예제
                TestPracticalUsage();
                
            }
            catch (Exception e)
            {
                Console.WriteLine($"테스트 실행 중 오류: {e.Message}");
                Console.WriteLine(e.StackTrace);
            }
        }

        static void TestTypeInteractions()
        {
            Console.WriteLine("🔗 타입 간 상호작용 테스트:");

            // Set과 Range 조합
            var range = new PyRange(1, 6);
            var set = new PySet(range.GetValues().Cast<PyObject>());
            Console.WriteLine($"  set(range(1, 6)) = {set}");

            // Complex와 다른 숫자 타입
            var complex1 = new PyComplex(3, 4);
            var int1 = new PyInt(2);
            var result = complex1.Add(int1);
            Console.WriteLine($"  (3+4j) + 2 = {result}");

            // Slice와 실제 시퀀스
            var list = new PyList(new PyObject[] { 
                new PyInt(0), new PyInt(1), new PyInt(2), new PyInt(3), new PyInt(4), new PyInt(5) 
            });
            var slice = new PySlice(new PyInt(1), new PyInt(5), new PyInt(2));
            var indices = slice.GetIndices(list.Length());
            Console.WriteLine($"  list[1:5:2] 인덱스들: [{string.Join(", ", indices)}]");

            Console.WriteLine();
        }

        static void TestPracticalUsage()
        {
            Console.WriteLine("🛠️ 실용적 사용 예제:");

            // 집합을 사용한 중복 제거
            var numbers = new PyObject[] { 
                new PyInt(1), new PyInt(2), new PyInt(2), new PyInt(3), new PyInt(1), new PyInt(4) 
            };
            var uniqueSet = new PySet(numbers);
            Console.WriteLine($"  중복 제거: {uniqueSet}");

            // 복소수 계산
            var impedance1 = new PyComplex(100, 50);   // 100 + 50j 옴
            var impedance2 = new PyComplex(80, -30);   // 80 - 30j 옴
            var totalImpedance = impedance1.Add(impedance2);
            Console.WriteLine($"  임피던스 합계: {totalImpedance} 옴");

            // Range를 사용한 수열 생성
            var evenNumbers = new PyRange(0, 21, 2);
            Console.WriteLine($"  0-20 짝수: {evenNumbers} → {evenNumbers.ToList()}");

            // 이터레이터 체인
            var str = new PyString("Python");
            var strIter = new PyStringIterator(str);
            Console.WriteLine("  문자별 반복:");
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    Console.WriteLine($"    문자 {i+1}: {strIter.Next()}");
                }
            }
            catch (PythonException ex) when (ex.PyException is PyStopIteration)
            {
                Console.WriteLine("    문자열 끝");
            }

            Console.WriteLine();
        }
    }
}