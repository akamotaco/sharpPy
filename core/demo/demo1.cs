namespace SharpPy.Demo
{
    public class CompletePythonSystemDemo
    {
        public static void Demo()
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
            boundGreet.Call();

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
                superMethod.Call();
                Console.WriteLine("Derived.method() 종료");
                return PyNone.Instance;
            }));
            
            derived.PrintMRO();
            var derivedInstance = derived.CreateInstance();
            var derivedMethod = derivedInstance.GetAttribute("method");
            derivedMethod.Call();

            // 4.5. __call__ 시스템 테스트
            Console.WriteLine("\n📞 4.5. __call__ 시스템");
            Console.WriteLine(new string('=', 60));
            
            // 함수 호출 (두 가지 방식)
            var testFunc = new PyFunction("test_func", args => {
                Console.WriteLine($"Function called with {args.Length} args");
                return new PyString("result");
            });
            
            Console.WriteLine("함수 호출 방법들:");
            var result1 = testFunc.Call(new PyInt(1), new PyString("test"));  // 직접 Call 메서드
            Console.WriteLine($"testFunc.Call() result: {result1}");
            
            // __call__ attribute 접근으로 시연
            var callAttr = testFunc.GetAttribute("__call__");
            Console.WriteLine($"testFunc.__call__: {callAttr}");
            Console.WriteLine($"Same object? {ReferenceEquals(testFunc, callAttr)}");
            
            // callable() 내장 함수 테스트
            var scopeChain2 = new PyScopeChain();
            var callableFunc = scopeChain2.LookupVariable("callable");
            
            Console.WriteLine("\ncallable() 테스트:");
            var callableResult1 = callableFunc.Call(testFunc);          // 함수 → True
            var callableResult2 = callableFunc.Call(personClass);       // 클래스 → True  
            var callableResult3 = callableFunc.Call(new PyInt(42));     // 정수 → False
            
            Console.WriteLine($"callable(function): {callableResult1}");
            Console.WriteLine($"callable(class): {callableResult2}");
            Console.WriteLine($"callable(int): {callableResult3}");
            
            // 클래스 호출 (인스턴스 생성)
            Console.WriteLine("\n클래스 호출 (인스턴스 생성):");
            var newPerson = personClass.Call();  // Person() 호출
            Console.WriteLine($"Person(): {newPerson}");
            
            // 사용자 정의 __call__ 메서드
            Console.WriteLine("\n사용자 정의 __call__:");
            var callableClass = new PyClass("CallableClass", new[] { PyType.ObjectType });
            callableClass.SetAttribute("__call__", new PyFunction("__call__", args => {
                var self = args[0];
                Console.WriteLine($"{self} was called!");
                return new PyString("called");
            }));
            
            var callableInstance = callableClass.CreateInstance();
            Console.WriteLine($"callable(callableInstance): {callableFunc.Call(callableInstance)}");
            
            var callResult = callableInstance.Call(new PyString("arg"));
            Console.WriteLine($"callableInstance() result: {callResult}");

            // 5. LEGB 스코프 시스템 (Builtin 특별 관리)
            Console.WriteLine("\n🔍 5. LEGB 스코프 시스템 (Builtin 특별 관리)");
            Console.WriteLine(new string('=', 60));
            
            var scopeChain = new PyScopeChain();
            scopeChain.PrintSystemState();
            
            // Builtin 스코프 생성 시도 (실패해야 함)
            Console.WriteLine("\n❌ Builtin 스코프 직접 생성 시도:");
            try
            {
                scopeChain.PushScope(ScopeType.Builtin, "builtin_attempt");
            }
            catch (InvalidOperationException e)
            {
                Console.WriteLine($"  예외: {e.Message}");
            }
            
            // Global 변수 설정
            scopeChain.AssignVariable("x", new PyString("global"));
            
            // 함수 스코프들
            scopeChain.PushScope(ScopeType.Local, "outer_func");
            scopeChain.AssignVariable("x", new PyString("enclosing"));
            
            scopeChain.PushScope(ScopeType.Local, "inner_func");  
            scopeChain.AssignVariable("y", new PyString("local"));
            
            Console.WriteLine("\nLEGB 탐색 테스트:");
            
            // Local에서 찾아지는 경우
            var yValue = scopeChain.LookupVariable("y", null, true);
            
            // Enclosing에서 찾아지는 경우  
            var xValue = scopeChain.LookupVariable("x", null, true);
            
            // Builtin에서 찾아지는 경우 (특별 관리!)
            var printValue = scopeChain.LookupVariable("print", null, true);
            
            scopeChain.PrintSystemState();
            
            // Builtin 모듈 직접 접근
            Console.WriteLine("\n⭐ Builtin 모듈 특별 관리:");
            var builtinModule = scopeChain.BuiltinModule;
            Console.WriteLine($"  Builtin 모듈: {builtinModule}");
            Console.WriteLine($"  전역 싱글톤 확인: {ReferenceEquals(builtinModule, PyBuiltinsModule.Instance)}");
            
            // __builtins__ 참조 확인
            var builtinsRef = scopeChain.GlobalScope.GetVariable("__builtins__");
            Console.WriteLine($"  globals()['__builtins__']: {builtinsRef}");
            Console.WriteLine($"  같은 객체: {ReferenceEquals(builtinsRef, builtinModule)}");
            
            // Builtin 수정 위험성 시연
            Console.WriteLine("\n⚠️ Builtin 수정의 전역적 영향:");
            var originalLen = builtinModule.GetBuiltin("len");
            builtinModule.SetAttribute("len", new PyString("망가진 len"));
            
            // 다른 스코프 체인에서도 영향 확인
            var anotherScopeChain = new PyScopeChain();
            var corruptedLen = anotherScopeChain.LookupVariable("len", null, false);
            Console.WriteLine($"  다른 스코프체인에서 len: {corruptedLen}");
            Console.WriteLine($"  → Builtin은 모든 곳에서 공유됨!");
            
            // 복원
            builtinModule.SetAttribute("len", originalLen);
            Console.WriteLine($"  len 함수 복원: {anotherScopeChain.LookupVariable("len", null, false)}");
            
            scopeChain.PopScope();
            scopeChain.PopScope();

            // 6. 모듈 시스템과 Import
            Console.WriteLine("\n📦 6. 모듈 시스템과 Import");
            Console.WriteLine(new string('=', 60));
            
            // import math
            var mathModule = PyImportSystem.Import("math");
            scopeChain.AssignVariable("math", mathModule);
            
            var pi = mathModule.GetAttribute("PI");
            Console.WriteLine($"math.PI: {pi}");
            
            // from os import name
            var osItems = PyImportSystem.FromImport("os", "name");
            foreach (var item in osItems)
            {
                scopeChain.AssignVariable(item.Key, item.Value);
                Console.WriteLine($"Imported {item.Key}: {item.Value}");
            }

            // 7. Variable vs Attribute 비교
            Console.WriteLine("\n🔄 7. Variable vs Attribute");
            Console.WriteLine(new string('=', 60));
            
            // 모듈에서는 Variable == Attribute
            var mathAsVar = mathModule.GetVariable("PI");     // Variable 접근
            var mathAsAttr = mathModule.GetAttribute("PI");   // Attribute 접근
            Console.WriteLine($"모듈 Variable과 Attribute 동일? {ReferenceEquals(mathAsVar, mathAsAttr)}");
            
            // 인스턴스에서는 Attribute만
            var personAge = person.InstanceDict.GetValueOrDefault("age", new PyInt(0));  // 직접 접근
            person.SetAttribute("age", new PyInt(25));  // Attribute 시스템
            var ageAttr = person.GetAttribute("age");
            Console.WriteLine($"person.age: {ageAttr}");

            // 8. 전체 시스템 통합 테스트
            Console.WriteLine("\n🌟 8. 전체 시스템 통합");
            Console.WriteLine(new string('=', 60));
            
            // 복잡한 클래스 생성
            var animalClass = new PyClass("Animal", new[] { PyType.ObjectType });
            var mammalClass = new PyClass("Mammal", new[] { animalClass });
            var dogClass = new PyClass("Dog", new[] { mammalClass });
            
            // 각 레벨에 메서드 추가
            animalClass.SetAttribute("speak", new PyFunction("speak", args => {
                Console.WriteLine("Animal makes a sound");
                return PyNone.Instance;
            }));
            
            mammalClass.SetAttribute("speak", new PyFunction("speak", args => {
                Console.WriteLine("Mammal speaks");
                return PyNone.Instance;
            }));
            
            dogClass.SetAttribute("speak", new PyFunction("speak", args => {
                var self = args[0];
                Console.WriteLine("Dog barks!");
                // super() 사용
                var super = new PySuper(dogClass, self);
                var superSpeak = super.GetAttribute("speak");
                superSpeak.Call();
                return PyNone.Instance;
            }));
            
            Console.WriteLine("Dog 클래스 MRO:");
            dogClass.PrintMRO();
            
            var dog = dogClass.CreateInstance();
            var dogSpeak = dog.GetAttribute("speak");
            Console.WriteLine("\ndog.speak() 호출:");
            dogSpeak.Call();

            // 9. sys.modules 상태 출력
            Console.WriteLine("\n📚 9. sys.modules 캐시");
            Console.WriteLine(new string('=', 60));
            
            Console.WriteLine("로드된 모듈들:");
            foreach (var module in PyImportSystem.SysModules)
            {
                Console.WriteLine($"  {module.Key}: {module.Value}");
            }

            Console.WriteLine("\n✨ 통합 시스템 데모 완료!");
            Console.WriteLine("\n📋 구현된 시스템들:");
            Console.WriteLine("  ✅ 기본 객체 모델 (object, type)");
            Console.WriteLine("  ✅ MRO (C3 선형화)");
            Console.WriteLine("  ✅ Attribute 시스템 (Descriptor 프로토콜)");
            Console.WriteLine("  ✅ Property, Method 바인딩");
            Console.WriteLine("  ✅ super() 구현");
            Console.WriteLine("  ✅ __call__ 시스템 (callable 객체)");
            Console.WriteLine("  ✅ LEGB 스코프 시스템 (Builtin 특별 관리!)");
            Console.WriteLine("  ✅ 모듈과 Import 시스템");
            Console.WriteLine("  ✅ Variable vs Attribute 구분");
            Console.WriteLine("  ✅ 내장 함수 (print, len, abs, callable)");
            Console.WriteLine("  ✅ Builtin 전역 싱글톤 모듈 관리");
            Console.WriteLine("\n🎯 Python의 핵심 객체 시스템을 C#으로 완전히 재현했습니다!");
            Console.WriteLine("\n🔥 특별 구현 사항:");
            Console.WriteLine("  • Builtin Scope = 전역 싱글톤 모듈 (일반 스코프와 다름!)");
            Console.WriteLine("  • sys.modules['builtins']와 __builtins__ 참조 시스템");
            Console.WriteLine("  • 모든 모듈에서 동일한 builtin 인스턴스 공유");
            Console.WriteLine("  • Builtin 수정 시 전역적 영향 (monkey patching 가능)");
        }
    }
}