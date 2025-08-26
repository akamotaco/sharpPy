namespace SharpPy
{
    #region Interpreter Integration (기존 시스템과 통합)

    // 통합 Python 인터프리터 (기존 시스템 활용)
    public class IntegratedPythonInterpreter
    {
        private readonly SimpleParser _parser;
        private readonly PythonCompiler _compiler;
        private readonly PythonVM _vm;
        private readonly PyScopeChain _globalScope;
        
        public IntegratedPythonInterpreter()
        {
            _parser = new SimpleParser();
            _compiler = new PythonCompiler();
            _vm = PythonVM.Instance;
            _globalScope = new PyScopeChain(); // 기존 LEGB 시스템!
            
            SetupBuiltinHelpers();
        }
        
        // 기존 시스템과 연동을 위한 도우미 함수들 추가
        private void SetupBuiltinHelpers()
        {
            // VM에서 사용할 도우미 함수들
            var makeFunctionHelper = new PyBuiltinFunction("__make_function__");
            _globalScope.AssignVariable("__make_function__", makeFunctionHelper);
        }
        
        // 전체 실행 파이프라인 (기존 시스템과 완전 통합)
        public PyObject Execute(string sourceCode)
        {
            Console.WriteLine("🐍 통합 Python 인터프리터 실행");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"소스:\n{sourceCode}");
            
            try
            {
                // 1단계: 파싱 (소스 → AST)
                Console.WriteLine("\n" + new string('=',30));
                Console.WriteLine("1️⃣ 파싱: 소스 → AST");
                Console.WriteLine(new string('=', 30));
                var statements = _parser.Parse(sourceCode);
                
                // 2단계: 컴파일 (AST → 바이트코드)
                Console.WriteLine("\n" + new string('=', 30));
                Console.WriteLine("2️⃣ 컴파일: AST → 바이트코드");
                Console.WriteLine(new string('=', 30));
                var codeObject = _compiler.Compile(statements);
                
                // 3단계: 디스어셈블리
                Console.WriteLine("\n" + new string('=', 30));
                Console.WriteLine("3️⃣ 바이트코드 확인");
                Console.WriteLine(new string('=', 30));
                codeObject.Disassemble();
                
                // 4단계: VM 실행 (기존 시스템들과 연동)
                Console.WriteLine("\n" + new string('=', 30));
                Console.WriteLine("4️⃣ VM 실행 (기존 LEGB 시스템 사용)");
                Console.WriteLine(new string('=', 30));
                var result = _vm.ExecuteModule(codeObject);
                
                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine($"🎉 최종 결과: {result}");
                Console.WriteLine(new string('=', 60));
                
                return result;
            }
            catch (Exception e)
            {
                Console.WriteLine($"\n💥 실행 오류: {e.Message}");
                throw;
            }
        }
        
        // 대화형 실행 (REPL 스타일)
        public void Interactive()
        {
            Console.WriteLine("🐍 Python 대화형 모드 (기존 시스템 통합)");
            Console.WriteLine("종료하려면 'exit' 입력");
            
            while (true)
            {
                Console.Write(">>> ");
                var input = Console.ReadLine();
                
                if (string.IsNullOrEmpty(input) || input == "exit")
                    break;
                    
                try
                {
                    var result = Execute(input);
                    if (result != PyNone.Instance)
                    {
                        Console.WriteLine($"출력: {result}");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"오류: {e.Message}");
                }
            }
        }
        
        // 현재 전역 스코프 상태 출력
        public void PrintGlobalState()
        {
            Console.WriteLine("\n📊 전역 스코프 상태:");
            _globalScope.PrintSystemState();
        }
    }

    #endregion
}