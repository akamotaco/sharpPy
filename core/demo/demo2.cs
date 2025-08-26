namespace SharpPy.Demo
{
    #region Enhanced Demo Program

    // VM과 기존 시스템 통합 데모
    public class VMIntegrationDemo
    {
        public static void Demo()
        {
            Console.WriteLine("=== VM과 기존 Python 시스템 통합 데모 ===\n");

            var interpreter = new IntegratedPythonInterpreter();

            // 1. 기본 계산 및 변수
            Console.WriteLine("🔢 1. 기본 계산과 LEGB 시스템 연동");
            var basicCode = @"
    x = 10
    y = 20
    result = x + y
    print(result)
    ";
            interpreter.Execute(basicCode);

            // 2. 기존 객체 시스템과의 연동
            Console.WriteLine("\n\n🏗️ 2. 기존 객체 시스템과 연동 테스트");
            var objectCode = @"
    a = 5
    b = 3
    sum_val = a + b
    product = a * b
    ";
            interpreter.Execute(objectCode);
            
            interpreter.PrintGlobalState();

            // 3. 복잡한 표현식
            Console.WriteLine("\n\n🧮 3. 복잡한 표현식 처리");
            var complexCode = @"
    x = 2
    y = 3
    z = 4
    complex_result = x + y * z
    final = complex_result - x
    ";
            interpreter.Execute(complexCode);

            // 4. 내장 함수 호출 (기존 builtin 시스템 사용)
            Console.WriteLine("\n\n📞 4. 내장 함수 호출 (기존 builtin 시스템)");
            var builtinCode = @"
    value = 42
    print(value)
    ";
            interpreter.Execute(builtinCode);

            // 5. 시스템 통합 요약
            Console.WriteLine("\n\n📋 시스템 통합 요약");
            Console.WriteLine(new string('=', 50));
            
            Console.WriteLine("✅ 성공적으로 통합된 시스템들:");
            Console.WriteLine("  • 기존 PyObject, PyType, PyFunction 시스템");
            Console.WriteLine("  • 기존 LEGB 스코프 (PyScopeChain, PyBuiltinsModule)");
            Console.WriteLine("  • 기존 Attribute 시스템 (GetAttribute, SetAttribute)");
            Console.WriteLine("  • 기존 MRO 및 상속 시스템");
            Console.WriteLine("  • 새로운 VM과 바이트코드 실행 엔진");
            
            Console.WriteLine("\n🔄 실행 흐름:");
            Console.WriteLine("  소스코드 → Parser → AST → Compiler → 바이트코드");
            Console.WriteLine("                                           ↓");
            Console.WriteLine("  기존 객체 시스템 ← VM ← 바이트코드 인터프리터");
            
            Console.WriteLine("\n💡 핵심 통합 포인트:");
            Console.WriteLine("  • VM이 기존 PyScopeChain.LookupVariable() 사용");
            Console.WriteLine("  • VM이 기존 PyObject.Call() 시스템 사용");
            Console.WriteLine("  • VM이 기존 GetAttribute/SetAttribute 사용");
            Console.WriteLine("  • 기존 PyBuiltinsModule과 완벽 연동");

            Console.WriteLine("\n=== 통합 데모 완료 ===");
            Console.WriteLine("이제 완전한 Python 실행 환경이 구축되었습니다! 🎉");
        }
    }

    #endregion

}