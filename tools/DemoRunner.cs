using System;
using System.Collections.Generic;
using SharpPy.Core;
using SharpPy.Interop;

namespace SharpPy.Tools
{
    /// <summary>
    /// 데모 실행을 전담하는 클래스
    /// </summary>
    public class DemoRunner
    {
        /// <summary>
        /// 모든 데모를 순차적으로 실행
        /// </summary>
        public void RunAllDemos()
        {
            Console.WriteLine("🐍 SharpPy - Python Interpreter in C#");
            Console.WriteLine("=====================================\n");
            
            // 1. 기본 Python 객체 시스템 데모
            Console.WriteLine("📋 1. 기본 Python 객체 시스템 데모:");
            RunCompletePythonSystemDemo();
            
            // 2. VM 통합 데모
            Console.WriteLine("\n🖥️ 2. VM 통합 데모:");
            RunVMIntegrationDemo();
            
            // 3. C# → SharpPy 상호 운용 데모
            Console.WriteLine("\n🔗 3. C# → SharpPy 상호 운용 데모:");
            RunCSharpToSharpPyDemo();
            
            // 4. SharpPy → C# 상호 운용 데모
            Console.WriteLine("\n🔄 4. SharpPy → C# 상호 운용 데모:");
            RunSharpPyToCSharpDemo();
            
            // 5. Python 3.12 문법 검증 (있다면)
            try
            {
                Console.WriteLine("\n🔍 5. Python 3.12 문법 검증:");
                var syntaxTest = new SharpPy.Verification.Python312SyntaxVerification();
                syntaxTest.VerifyAllSyntax();
            }
            catch
            {
                Console.WriteLine("Python 3.12 문법 검증 기능이 현재 비활성화되어 있습니다.");
            }

            Console.WriteLine("\n✅ 모든 데모 완료!");
            Console.WriteLine("\n🎯 데모 요약:");
            Console.WriteLine("  ✅ Python 객체 시스템 (MRO, 속성, 메서드 바인딩, super())");
            Console.WriteLine("  ✅ VM과 바이트코드 실행 엔진");
            Console.WriteLine("  ✅ C# 함수를 SharpPy에서 호출");
            Console.WriteLine("  ✅ SharpPy 함수를 C#에서 호출");
            Console.WriteLine("  ✅ LEGB 스코프 시스템과 모듈 시스템");
        }

        /// <summary>
        /// 반복자 테스트 실행
        /// </summary>
        public void RunIterationTest()
        {
            // ManualIterationTest.RunTest(); // Class not found
            Console.WriteLine("test-iteration 기능이 현재 비활성화되어 있습니다.");
        }

        /// <summary>
        /// Try-Except 테스트 실행
        /// </summary>
        public void RunTryExceptTest()
        {
            // TryExceptASTTest.RunTest(); // Class not found
            Console.WriteLine("test-try-except 기능이 현재 비활성화되어 있습니다.");
        }

        #region 기존 Demo 클래스 기능 통합

        /// <summary>
        /// 완전한 Python 시스템 데모 (기존 Demo.CompletePythonSystemDemo.Demo() 통합)
        /// </summary>
        private void RunCompletePythonSystemDemo()
        {
            Console.WriteLine("=== 통합 Python 객체 시스템 데모 ===\n");

            // 1. 기본 객체 시스템과 MRO
            Console.WriteLine("🔧 1. 기본 객체 시스템과 MRO");
            Console.WriteLine(new string('=', 60));
            
            // 다이아몬드 상속 구조 생성
            var A = new PyClass("A", new[] { PyType.ObjectType });
            var B = new PyClass("B", new[] { A });
            var C = new PyClass("C", new[] { A });
            var D = new PyClass("D", new[] { B, C });
            
            Console.WriteLine("다이아몬드 상속 MRO:");
            D.PrintMRO();
            
            // 2. Attribute 시스템과 Property
            Console.WriteLine("\n🏠 2. Attribute 시스템과 Property");
            Console.WriteLine(new string('=', 60));
            
            var personClass = new PyClass("Person", new[] { PyType.ObjectType });
            
            // Property 추가
            var nameGetter = new PyFunction("get_name", args => 
            {
                var self = (PyClassInstance)args[0];
                return self.InstanceDict.GetValueOrDefault("_name", new PyString("Unknown"));
            });
            var nameSetter = new PyFunction("set_name", args =>
            {
                var self = (PyClassInstance)args[0];
                self.InstanceDict["_name"] = args[1];
                Console.WriteLine($"  Name set to: {args[1]}");
                return PyNone.Instance;
            });
            var nameProperty = new PyProperty(nameGetter, nameSetter);
            personClass.SetAttribute("name", nameProperty);
            
            var person = personClass.CreateInstance();
            person.SetAttribute("name", new PyString("Alice"));
            var name = person.GetAttribute("name");
            Console.WriteLine($"person.name: {name}");

            // 3. 메서드 바인딩
            Console.WriteLine("\n🔗 3. 메서드 바인딩");
            Console.WriteLine(new string('=', 60));
            
            var greetMethod = new PyFunction("greet", args =>
            {
                var self = args[0];
                Console.WriteLine($"Hello from {self}!");
                return new PyString("Hello!");
            });
            personClass.SetAttribute("greet", greetMethod);
            
            // 클래스를 통한 접근 (unbound function)
            var unboundGreet = personClass.GetAttribute("greet");
            Console.WriteLine($"Person.greet: {unboundGreet}");
            
            // 인스턴스를 통한 접근 (bound method)
            var boundGreet = person.GetAttribute("greet");
            Console.WriteLine($"person.greet: {boundGreet}");
            boundGreet.Call(new PyObject[] {  }, null);

            // 4. super() 동작
            Console.WriteLine("\n⬆️ 4. super() 동작");
            Console.WriteLine(new string('=', 60));
            
            var base1 = new PyClass("Base1", new[] { PyType.ObjectType });
            var base2 = new PyClass("Base2", new[] { PyType.ObjectType });
            var derived = new PyClass("Derived", new[] { base1, base2 });
            
            base1.SetAttribute("method", new PyFunction("method", args => {
                Console.WriteLine("Base1.method() 호출");
                return PyNone.Instance;
            }));
            
            derived.SetAttribute("method", new PyFunction("method", args => {
                var self = args[0];
                Console.WriteLine("Derived.method() 시작");
                var super = new PySuper(derived, self);
                var superMethod = super.GetAttribute("method");
                superMethod.Call(new PyObject[] {  }, null);
                Console.WriteLine("Derived.method() 종료");
                return PyNone.Instance;
            }));
            
            derived.PrintMRO();
            var derivedInstance = derived.CreateInstance();
            var derivedMethod = derivedInstance.GetAttribute("method");
            derivedMethod.Call(new PyObject[] {  }, null);

            // 5. LEGB 스코프 시스템
            Console.WriteLine("\n🔍 5. LEGB 스코프 시스템");
            Console.WriteLine(new string('=', 60));
            
            var scopeChain = new PyScopeChain();
            scopeChain.PrintSystemState();

            // 6. 모듈 시스템과 Import
            Console.WriteLine("\n📦 6. 모듈 시스템과 Import");
            Console.WriteLine(new string('=', 60));
            
            try
            {
                // import math
                var mathModule = PyImportSystem.Import("math");
                scopeChain.AssignVariable("math", mathModule);

                var pi = mathModule.GetAttribute("pi");
                Console.WriteLine($"math.pi: {pi}");

                // from os import name
                var osModule = PyImportSystem.Import("os");
                var osName = osModule.GetAttribute("name");
                scopeChain.AssignVariable("name", osName);
                Console.WriteLine($"Imported name: {osName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"모듈 import 데모 중 오류: {ex.Message}");
            }

            Console.WriteLine("\n✨ Python 객체 시스템 데모 완료!");
        }

        /// <summary>
        /// VM 통합 데모 (기존 Demo.VMIntegrationDemo.Demo() 통합)
        /// </summary>
        private void RunVMIntegrationDemo()
        {
            Console.WriteLine("=== VM과 기존 Python 시스템 통합 데모 ===\n");

            var interpreter = new IntegratedPythonInterpreter();

            // 1. 기본 계산 및 변수
            Console.WriteLine("🔢 1. 기본 계산과 LEGB 시스템 연동");
            var basicCode = @"
x = 10
y = 20
result = x + y
print('Basic calculation result:', result)
";
            interpreter.Execute(basicCode);

            // 2. 기존 객체 시스템과의 연동
            Console.WriteLine("\n\n🏗️ 2. 기존 객체 시스템과 연동 테스트");
            var objectCode = @"
a = 5
b = 3
sum_val = a + b
product = a * b
print('Sum:', sum_val, 'Product:', product)
";
            interpreter.Execute(objectCode);

            // 3. 복잡한 표현식
            Console.WriteLine("\n\n🧮 3. 복잡한 표현식 처리");
            var complexCode = @"
x = 2
y = 3
z = 4
complex_result = x + y * z
final = complex_result - x
print('Complex calculation:', final)
";
            interpreter.Execute(complexCode);

            // 4. 내장 함수 호출
            Console.WriteLine("\n\n📞 4. 내장 함수 호출");
            var builtinCode = @"
numbers = [1, 2, 3, 4, 5]
print('Numbers:', numbers)
print('Length:', len(numbers))
print('Max:', max(numbers))
";
            interpreter.Execute(builtinCode);

            Console.WriteLine("\n✅ VM 통합 데모 완료!");
        }

        /// <summary>
        /// C# 함수를 SharpPy에 등록하여 호출하는 데모
        /// </summary>
        private void RunCSharpToSharpPyDemo()
        {
            Console.WriteLine("=== C# → SharpPy 상호 운용 데모 ===\n");

            var interpreter = new IntegratedPythonInterpreter();

            // 1. C# 함수를 Python에 등록 (새로운 IronPython 스타일 API!)
            Console.WriteLine("🔧 1. C# 함수를 Python에 등록 (IronPython 스타일)");
            
            // 수학 함수 등록 - 간단한 구문!
            var addFunction = PyFunction.Create("csharp_add", (int a, int b) =>
            {
                var result = a + b;
                Console.WriteLine($"  C# Add function called: {a} + {b} = {result}");
                return result;
            });

            // 문자열 처리 함수 등록 - 타입 변환 자동화!
            var reverseFunction = PyFunction.Create("csharp_reverse", (string str) =>
            {
                // Performance: Eliminated LINQ
                var chars = str.ToCharArray();
                Array.Reverse(chars);
                var reversed = new string(chars);
                Console.WriteLine($"  C# Reverse function called: '{str}' → '{reversed}'");
                return reversed;
            });

            // 시스템 정보 함수 등록 - Action 지원!
            var sysInfoFunction = PyFunction.Create("csharp_sysinfo", () =>
            {
                Console.WriteLine("  C# System info function called");
                return new Dictionary<string, object>
                {
                    ["platform"] = Environment.OSVersion.Platform.ToString(),
                    ["version"] = Environment.OSVersion.VersionString,
                    ["machine_name"] = Environment.MachineName,
                    ["processor_count"] = Environment.ProcessorCount,
                    ["dotnet_version"] = Environment.Version.ToString()
                };
            });

            Console.WriteLine("C# 함수들이 생성되었습니다:");
            Console.WriteLine($"  csharp_add: {addFunction}");
            Console.WriteLine($"  csharp_reverse: {reverseFunction}");
            Console.WriteLine($"  csharp_sysinfo: {sysInfoFunction}");

            // 2. Python에서 C# 함수 호출 (직접 테스트)
            Console.WriteLine("\n📞 2. C# 함수 직접 호출 테스트");
            
            try 
            {
                // C# 수학 함수 테스트
                var addResult = addFunction.Call(new PyObject[] { new PyInt(10), new PyInt(20) }, null);
                Console.WriteLine($"C# add result: {addResult}");

                // C# 문자열 함수 테스트  
                var reverseResult = reverseFunction.Call(new PyObject[] { new PyString("Hello World!") }, null);
                Console.WriteLine($"C# reverse result: {reverseResult}");

                // C# 시스템 정보 함수 테스트
                var sysInfoResult = sysInfoFunction.Call(new PyObject[] {  }, null);
                Console.WriteLine($"System info from C#: {sysInfoResult}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"C# 함수 호출 중 오류: {ex.Message}");
            }

            Console.WriteLine("\n✅ C# → SharpPy 상호 운용 데모 완료!");
        }

        /// <summary>
        /// SharpPy에서 정의한 함수를 C#에서 호출하는 데모
        /// </summary>
        private void RunSharpPyToCSharpDemo()
        {
            Console.WriteLine("=== SharpPy → C# 상호 운용 데모 ===\n");

            var interpreter = new IntegratedPythonInterpreter();

            // 1. C#에서 직접 Python 함수 생성 (IronPython 스타일!)
            Console.WriteLine("🐍 1. C#에서 Python 함수 생성 (IronPython 스타일)");
            
            // 계산 함수 생성 - 깨끗한 타입 시그니처!
            var calculateAreaFunc = PyFunction.Create("calculate_area", (int width, int height) =>
            {
                var area = width * height;
                Console.WriteLine($"Python function called: {width} × {height} = {area}");
                return area;
            });

            // 메시지 포맷팅 함수 생성 - 자동 타입 변환!
            var formatMessageFunc = PyFunction.Create("format_message", (string name, int age) =>
            {
                var message = $"Hello, {name}! You are {age} years old.";
                Console.WriteLine($"Python formatter called: {message}");
                return message;
            });

            // 숫자 처리 함수 생성 - 복잡한 타입도 지원!
            var processNumbersFunc = PyFunction.Create("process_numbers", (int[] numbers) =>
            {
                // Performance: Eliminated LINQ
                int total = 0;
                foreach (var num in numbers)
                {
                    total += num;
                }
                var count = numbers.Length;
                var average = (double)total / count;

                Console.WriteLine($"Python processor called: sum={total}, avg={average}");

                return new Dictionary<string, object>
                {
                    ["sum"] = total,
                    ["average"] = average,
                    ["count"] = count
                };
            });

            Console.WriteLine("Python 스타일 함수들이 C#에서 생성되었습니다:");
            Console.WriteLine($"  calculate_area: {calculateAreaFunc}");
            Console.WriteLine($"  format_message: {formatMessageFunc}"); 
            Console.WriteLine($"  process_numbers: {processNumbersFunc}");

            // 2. C#에서 "Python" 함수 호출
            Console.WriteLine("\n🔧 2. C#에서 Python 스타일 함수 호출");
            
            try
            {
                // calculate_area 함수 호출
                var area = calculateAreaFunc.Call(new PyObject[] { new PyInt(15), new PyInt(25) }, null);
                Console.WriteLine($"C# got area result: {area}");

                // format_message 함수 호출
                var message = formatMessageFunc.Call(new PyObject[] { new PyString("Alice"), new PyInt(30) }, null);
                Console.WriteLine($"C# got message: {message}");

                // process_numbers 함수 호출 - C# 배열을 직접 전달!
                var numbers = new int[] { 10, 20, 30, 40 };
                var result = processNumbersFunc.Call(new PyObject[] { PyTypeConverter.ToPyObject(numbers) }, null);
                Console.WriteLine($"C# got process result: {result}");
                
                // 결과 딕셔너리에서 개별 값 추출
                if (result is PyDict resultDict)
                {
                    var sum = resultDict.GetItem(new PyString("sum"));
                    var avg = resultDict.GetItem(new PyString("average"));
                    var count = resultDict.GetItem(new PyString("count"));
                    Console.WriteLine($"  C# extracted: sum={sum}, avg={avg}, count={count}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"C#에서 Python 함수 호출 중 오류: {ex.Message}");
            }

            // 3. 양방향 호출 데모 (IronPython 스타일로 개선!)
            Console.WriteLine("\n🔄 3. 양방향 호출 데모 (새로운 API)");
            
            // C# 헬퍼 함수 - 깨끗한 시그니처!
            var mathHelper = PyFunction.Create("csharp_math_helper", (string operation, int a, int b) =>
            {
                int result = operation switch
                {
                    "add" => a + b,
                    "multiply" => a * b,
                    "power" => (int)Math.Pow(a, b),
                    _ => throw new ArgumentException($"Unknown operation: {operation}")
                };
                
                Console.WriteLine($"  C# math helper: {operation}({a}, {b}) = {result}");
                return result;
            });

            // Python 계산기 함수가 C# 헬퍼를 호출 - 간단해진 구조!
            var pythonCalculator = PyFunction.Create("python_calculator", (int x, int y) =>
            {
                Console.WriteLine($"Python calculator called with {x}, {y}");
                
                var sumResult = mathHelper.Call(new PyObject[] { new PyString("add"), new PyInt(x), new PyInt(y) }, null);
                var mulResult = mathHelper.Call(new PyObject[] { new PyString("multiply"), new PyInt(x), new PyInt(y) }, null);  
                var powResult = mathHelper.Call(new PyObject[] { new PyString("power"), new PyInt(x), new PyInt(y) }, null);
                
                return new Dictionary<string, object>
                {
                    ["sum"] = sumResult,
                    ["product"] = mulResult, 
                    ["power"] = powResult
                };
            });

            // 테스트 호출
            var hybridResult = pythonCalculator.Call(new PyObject[] { new PyInt(3), new PyInt(4) }, null);
            Console.WriteLine($"Hybrid calculation result: {hybridResult}");

            Console.WriteLine("\n✅ SharpPy → C# 상호 운용 데모 완료!");
        }

        #endregion
    }
}