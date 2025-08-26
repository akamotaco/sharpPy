namespace SharpPy
{
    
#region Bytecode System Extension

    // 바이트코드 명령어들
    public enum ByteCodeOp : byte
    {
        // 스택 조작
        LOAD_CONST = 1,      // 상수를 스택에 로드
        LOAD_NAME = 2,       // 변수를 스택에 로드 (LEGB 탐색)
        STORE_NAME = 3,      // 스택 top을 변수에 저장
        LOAD_GLOBAL = 4,     // 전역 변수 로드
        STORE_GLOBAL = 5,    // 전역 변수 저장
        
        // 연산
        BINARY_ADD = 10,     // 이항 덧셈
        BINARY_SUBTRACT = 11, // 이항 뺄셈
        BINARY_MULTIPLY = 12, // 이항 곱셈
        BINARY_DIVIDE = 13,   // 이항 나눗셈
        
        // 함수 및 호출
        CALL_FUNCTION = 20,  // 함수 호출
        MAKE_FUNCTION = 21,  // 함수 객체 생성
        
        // 제어 흐름
        RETURN_VALUE = 30,   // 함수에서 값 반환
        JUMP_FORWARD = 31,   // 무조건 점프
        JUMP_IF_FALSE = 32,  // 거짓이면 점프
        
        // 객체 조작
        LOAD_ATTR = 40,      // 속성 로드 (obj.attr)
        STORE_ATTR = 41,     // 속성 저장 (obj.attr = value)
        
        // 기타
        POP_TOP = 50,        // 스택 top 제거
        DUP_TOP = 51,        // 스택 top 복사
        NOP = 60             // 아무 작업 안 함
    }

    // 바이트코드 명령 구조체
    public struct ByteCodeInstruction
    {
        public ByteCodeOp OpCode { get; }
        public int Argument { get; }
        
        public ByteCodeInstruction(ByteCodeOp opCode, int argument = 0)
        {
            OpCode = opCode;
            Argument = argument;
        }
        
        public override string ToString()
        {
            return Argument == 0 ? OpCode.ToString() : $"{OpCode} {Argument}";
        }
    }

    // 확장된 PyCodeObject (기존 시스템과 연동)
    public class PyCodeObject : PyObject
    {
        public string Name { get; }
        public List<ByteCodeInstruction> Instructions { get; }
        public List<PyObject> Constants { get; }      // co_consts
        public List<string> Names { get; }            // co_names (변수명들)
        public List<string> VarNames { get; }         // co_varnames (지역변수명들)
        public int ArgCount { get; }                  // 매개변수 개수
        
        public PyCodeObject(string name, List<ByteCodeInstruction> instructions, 
                        List<PyObject> constants, List<string> names, 
                        List<string> varNames, int argCount = 0)
        {
            Name = name;
            Instructions = instructions;
            Constants = constants;
            Names = names;
            VarNames = varNames;
            ArgCount = argCount;
        }
        
        public override string GetTypeName() => "code";
        
        public void Disassemble()
        {
            Console.WriteLine($"\n📄 Disassembly of {Name}:");
            for (int i = 0; i < Instructions.Count; i++)
            {
                var inst = Instructions[i];
                var extra = "";
                
                switch (inst.OpCode)
                {
                    case ByteCodeOp.LOAD_CONST:
                        extra = $"({Constants[inst.Argument]})";
                        break;
                    case ByteCodeOp.LOAD_NAME:
                    case ByteCodeOp.STORE_NAME:
                    case ByteCodeOp.LOAD_GLOBAL:
                    case ByteCodeOp.STORE_GLOBAL:
                        extra = $"({Names[inst.Argument]})";
                        break;
                    case ByteCodeOp.LOAD_ATTR:
                    case ByteCodeOp.STORE_ATTR:
                        extra = $"({Names[inst.Argument]})";
                        break;
                }
                
                Console.WriteLine($"  {i,3}: {inst,-25} {extra}");
            }
        }
        
        public override string ToString() => $"<code object {Name}>";
    }

    #endregion
}