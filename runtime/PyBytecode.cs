using System.Linq;

namespace SharpPy
{
    
#region Exception Table System (CPython 3.12)

    // CPython 3.12 Exception Table Entry
    public class ExceptionTableEntry 
    {
        public int StartOffset { get; set; }      // 보호 구간 시작
        public int EndOffset { get; set; }        // 보호 구간 끝
        public int HandlerOffset { get; set; }    // 핸들러 시작 위치 (해석된 값)
        public int Depth { get; set; }            // 스택 depth 
        public bool Lasti { get; set; }           // last instruction 플래그
        
        // 라벨 기반 생성자 (컴파일 시점)
        public string? HandlerLabelName { get; set; }  // 핸들러 라벨 이름 (해석 전)
        
        public ExceptionTableEntry(int start, int end, int handler, int depth, bool lasti = true)
        {
            StartOffset = start;
            EndOffset = end;
            HandlerOffset = handler;
            Depth = depth;
            Lasti = lasti;
        }
        
        // 라벨 기반 생성자 (CPython 3.12 호환)
        public ExceptionTableEntry(int start, int end, string handlerLabel, int depth, bool lasti = true)
        {
            StartOffset = start;
            EndOffset = end;
            HandlerLabelName = handlerLabel;
            HandlerOffset = -1; // 아직 해석되지 않음
            Depth = depth;
            Lasti = lasti;
        }
        
        public bool IsResolved => HandlerOffset >= 0;
    }

#endregion
    
#region Bytecode System Extension

    // 바이트코드 명령어들 (Python 3.12 기준)
    public enum ByteCodeOp : byte
    {
        // 스택 조작 기본
        LOAD_CONST = 1,       // 상수를 스택에 로드
        LOAD_NAME = 2,        // 변수를 스택에 로드 (LEGB 탐색)
        STORE_NAME = 3,       // 스택 top을 변수에 저장
        DELETE_NAME = 4,      // 이름 삭제
        
        // 전역/지역 변수
        LOAD_GLOBAL = 5,      // 전역 변수 로드
        STORE_GLOBAL = 6,     // 전역 변수 저장
        DELETE_GLOBAL = 7,    // 전역 변수 삭제
        LOAD_FAST = 8,        // 지역 변수 로드 (빠름)
        STORE_FAST = 9,       // 지역 변수 저장 (빠름)  
        DELETE_FAST = 10,     // 지역 변수 삭제
        
        // CPython 3.12+ Superinstructions (fused operations)
        LOAD_FAST_LOAD_FAST = 11,    // LOAD_FAST + LOAD_FAST (연속 로드 최적화)
        LOAD_CONST_LOAD_FAST = 12,   // LOAD_CONST + LOAD_FAST 
        STORE_FAST_LOAD_FAST = 13,   // STORE_FAST + LOAD_FAST
        STORE_FAST_STORE_FAST = 14,  // STORE_FAST + STORE_FAST
        LOAD_FAST_AND_CLEAR = 15,    // CPython 3.12 PEP 709: 변수 로드 후 클리어 (컴프리헨션용)
        
        // 이항 연산 (CPython 3.12+ 스타일 통합)
        BINARY_OP = 20,       // 통합 이항 연산 (argument로 연산 타입 구분)
        
        // Legacy 개별 이항 연산들 (단계적 제거 예정)
        BINARY_ADD = 21,      // 이항 덧셈 (deprecated - use BINARY_OP)
        BINARY_SUBTRACT = 22, // 이항 뺄셈 (deprecated - use BINARY_OP)
        BINARY_MULTIPLY = 23, // 이항 곱셈 (deprecated - use BINARY_OP)
        BINARY_DIVIDE = 24, // 이항 나눗셈 (/) (deprecated - use BINARY_OP)
        BINARY_FLOOR_DIVIDE = 25, // 바닥 나눗셈 (//) (deprecated - use BINARY_OP)
        BINARY_MODULO = 26,   // 모듈로 연산 (%) (deprecated - use BINARY_OP)
        BINARY_POWER = 27,    // 거듭제곱 (**) (deprecated - use BINARY_OP)
        BINARY_LSHIFT = 28,   // 비트 좌시프트 (<<) (deprecated - use BINARY_OP)
        BINARY_RSHIFT = 29,   // 비트 우시프트 (>>) (deprecated - use BINARY_OP)
        BINARY_OR = 30,       // 비트 OR (|) (deprecated - use BINARY_OP)
        BINARY_XOR = 31,      // 비트 XOR (^) (deprecated - use BINARY_OP)
        BINARY_AND = 32,      // 비트 AND (&) (deprecated - use BINARY_OP)
        BINARY_MATRIX_MULTIPLY = 33, // 행렬 곱셈 (@) (deprecated - use BINARY_OP)
        
        // 비교 연산
        COMPARE_OP = 40,      // 비교 연산 (<, <=, ==, !=, >, >=, in, not in, is, is not)
        
        // 일항 연산
        UNARY_POSITIVE = 41,  // +x
        UNARY_NEGATIVE = 42,  // -x
        UNARY_NOT = 43,       // not x
        UNARY_INVERT = 44,    // ~x
        
        // 복합 할당 연산
        INPLACE_ADD = 50,     // +=
        INPLACE_SUBTRACT = 51, // -=
        INPLACE_MULTIPLY = 52, // *=
        INPLACE_DIVIDE = 53, // /=
        INPLACE_FLOOR_DIVIDE = 54, // //=
        INPLACE_MODULO = 55,  // %=
        INPLACE_POWER = 56,   // **=
        INPLACE_LSHIFT = 57,  // <<=
        INPLACE_RSHIFT = 58,  // >>=
        INPLACE_OR = 59,      // |=
        INPLACE_XOR = 60,     // ^=
        INPLACE_AND = 61,     // &=
        INPLACE_MATRIX_MULTIPLY = 62, // @=
        
        // 함수 및 호출
        CALL_FUNCTION = 70,   // 함수 호출
        CALL_FUNCTION_KW = 71, // 키워드 인수로 함수 호출
        CALL_FUNCTION_EX = 72, // *args, **kwargs 확장된 함수 호출
        MAKE_FUNCTION = 73,   // 함수 객체 생성
        
        // 제어 흐름
        RETURN_VALUE = 80,    // 함수에서 값 반환
        YIELD_VALUE = 81,     // 제너레이터에서 값 yield
        YIELD_FROM = 82,      // yield from
        POP_JUMP_IF_TRUE = 83, // 참이면 점프
        POP_JUMP_IF_FALSE = 84, // 거짓이면 점프
        JUMP_IF_TRUE_OR_POP = 85, // 참이면 점프, 아니면 pop
        JUMP_IF_FALSE_OR_POP = 86, // 거짓이면 점프, 아니면 pop
        JUMP_FORWARD = 87,    // 무조건 점프
        JUMP_BACKWARD = 88,   // 뒤로 점프
        
        // 객체 조작
        LOAD_ATTR = 90,       // 속성 로드 (obj.attr)
        STORE_ATTR = 91,      // 속성 저장 (obj.attr = value)
        DELETE_ATTR = 92,     // 속성 삭제 (del obj.attr)
        LOAD_SUBSCR = 93,     // 인덱싱 (obj[key])
        STORE_SUBSCR = 94,    // 인덱싱 할당 (obj[key] = value)
        DELETE_SUBSCR = 95,   // 인덱싱 삭제 (del obj[key])
        
        // 컨테이너 생성
        BUILD_TUPLE = 100,    // 튜플 생성
        BUILD_LIST = 101,     // 리스트 생성
        BUILD_SET = 102,      // 세트 생성
        BUILD_MAP = 103,      // 딕셔너리 생성
        BUILD_SLICE = 104,    // 슬라이스 생성
        BUILD_STRING = 105,   // 문자열 연결
        
        // 언패킹
        UNPACK_SEQUENCE = 110, // 시퀀스 언패킹
        UNPACK_EX = 111,      // 확장된 언패킹 (*args)
        
        // 루프 제어
        GET_ITER = 120,       // 이터레이터 획득
        FOR_ITER = 121,       // for 루프 이터레이션
        BREAK_LOOP = 122,     // 루프 탈출
        CONTINUE_LOOP = 123,  // 루프 계속
        
        // 예외 처리
        SETUP_FINALLY = 130,  // finally 블록 설정
        SETUP_EXCEPT = 131,   // except 블록 설정
        END_FINALLY = 133,    // finally 블록 종료
        RERAISE = 134,        // 예외 재발생
        RAISE_VARARGS = 135,  // 예외 발생
        PUSH_EXC_INFO = 137,  // CPython 3.12: 예외 정보를 스택에 푸시 (exc_type, exc_value, exc_traceback, lasti)
        POP_EXCEPT = 138,     // CPython 3.12: 예외 핸들러 정리
        
        // with 문 관련 (CPython 3.12 호환)
        BEFORE_WITH = 132,    // with 블록 시작 전 준비 (__exit__ 로드, __enter__ 호출)
        WITH_EXCEPT_START = 136,  // with 블록에서 예외 발생시 __exit__ 호출
        
        // 스코프 및 클로저
        LOAD_CLOSURE = 140,   // 클로저 변수 로드
        LOAD_DEREF = 141,     // 자유 변수 로드
        STORE_DEREF = 142,    // 자유 변수 저장
        DELETE_DEREF = 143,   // 자유 변수 삭제
        MAKE_CELL = 144,      // 지역 변수를 셀로 변환
        
        // import 문
        IMPORT_NAME = 150,    // 모듈 임포트
        IMPORT_FROM = 151,    // 모듈에서 객체 임포트
        IMPORT_STAR = 152,    // 모듈에서 모든 객체 임포트 (*)
        
        // 전역 함수
        LOAD_GLOBAL_BUILTIN = 160, // 내장 함수 로드
        
        // match 문 (Python 3.10+)
        MATCH_MAPPING = 170,  // 매핑 패턴 매치
        MATCH_SEQUENCE = 171, // 시퀀스 패턴 매치
        MATCH_KEYS = 172,     // 키 패턴 매치
        MATCH_CLASS = 173,    // 클래스 패턴 매치
        
        // 컴프리헨션
        LIST_APPEND = 180,    // 리스트에 요소 추가 (컴프리헨션용)
        SET_ADD = 181,        // 세트에 요소 추가 (컴프리헨션용)
        MAP_ADD = 182,        // 맵에 키-값 추가 (컴프리헨션용)
        
        // 기타 스택 조작
        POP_TOP = 190,        // 스택 top 제거
        ROT_TWO = 191,        // 상위 2개 요소 회전
        ROT_THREE = 192,      // 상위 3개 요소 회전
        DUP_TOP = 193,        // 스택 top 복사
        DUP_TOP_TWO = 194,    // 상위 2개 요소 복사
        COPY = 195,           // CPython 3.12: 스택에서 N번째 요소 복사
        
        // Python 3.12 새로운 옵코드들
        RESUME = 200,         // 코루틴 재개
        PRECALL = 201,        // 호출 전 준비
        CALL = 202,           // 새로운 호출 옵코드
        KW_NAMES = 203,       // 키워드 인수 이름들
        PUSH_NULL = 204,      // NULL 값 푸시
        
        // Python 3.12 추가된 옵코드들
        END_FOR = 205,        // 루프 종료 시 스택 정리
        END_SEND = 206,       // 제너레이터 종료 시 스택 정리
        CLEANUP_THROW = 207,  // 예외 처리 중 정리
        CALL_INTRINSIC_1 = 208, // 내장 함수 호출 (1 인수)
        CALL_INTRINSIC_2 = 209, // 내장 함수 호출 (2 인수)
        RETURN_CONST = 210,   // 상수 값 반환
        
        // Type alias 관련 (Python 3.12)
        LOAD_FROM_DICT_OR_DEREF = 211,
        LOAD_FROM_DICT_OR_GLOBALS = 212,
        LOAD_LOCALS = 213,
        LOAD_SUPER_ATTR = 214,
        BINARY_SLICE = 215,       // CPython 3.12: 슬라이싱 연산 (obj[start:stop])
        STORE_SLICE = 216,        // CPython 3.12: 슬라이스 할당 (obj[start:stop] = value)
        
        // 캐시 및 최적화
        CACHE = 210,          // 캐시 엔트리
        LOAD_METHOD = 211,    // 메서드 로드 (최적화됨)
        CALL_METHOD = 212,    // 메서드 호출 (최적화됨)
        
        // 기타
        EXTENDED_ARG = 220,   // 확장된 인수
        FORMAT_VALUE = 221,   // f-string 값 포매팅
        BUILD_CONST_KEY_MAP = 222, // 상수 키 맵 생성
        LOAD_ASSERTION_ERROR = 223, // AssertionError 로드
        
        // 추가 예외 처리 opcodes  
        EXCEPT_MATCH = 231,   // 예외 타입 매칭
        CHECK_EG_MATCH = 232, // ExceptionGroup 매칭 (PEP 654)
        
        // 비동기 관련
        GET_AWAITABLE = 240,  // awaitable 객체 획득
        GET_AITER = 241,      // async iterator 획득
        GET_ANEXT = 242,      // async next 획득
        BEFORE_ASYNC_WITH = 243, // async with 전 준비
        
        NOP = 255             // 아무 작업 안 함
    }

    // CPython 3.12+ BINARY_OP 연산 타입 (argument로 사용)
    public enum BinaryOpType : byte
    {
        // CPython 3.12 호환 순서
        ADD = 0,                  // +  (덧셈)
        AND = 1,                  // &  (비트 AND)
        FLOOR_DIVIDE = 2,         // // (바닥 나눗셈)
        LSHIFT = 3,               // << (좌시프트)
        MATRIX_MULTIPLY = 4,      // @  (행렬 곱셈)
        MULTIPLY = 5,             // *  (곱셈)
        MODULO = 6,               // %  (모듈로 - Python style)
        REMAINDER = 6,            // %  (모듈로 - alias for MODULO)
        OR = 7,                   // |  (비트 OR)
        POWER = 8,                // ** (거듭제곱)
        RSHIFT = 9,               // >> (우시프트)
        SUBTRACT = 10,            // -  (뺄셈)
        TRUE_DIVIDE = 11,         // /  (나눗셈)
        XOR = 12                  // ^  (비트 XOR)
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
        // CPython 호환 플래그 시스템
        public const int CO_VARARGS = 0x04;        // *args 매개변수 존재
        public const int CO_VARKEYWORDS = 0x08;    // **kwargs 매개변수 존재
        public const int CO_GENERATOR = 0x20;      // 제너레이터 함수
        public const int CO_COROUTINE = 0x80;      // 네이티브 코루틴 (async def)
        
        public string Name { get; }
        public List<ByteCodeInstruction> Instructions { get; }
        public List<PyObject> Constants { get; }      // co_consts
        public List<string> Names { get; }            // co_names (변수명들)
        public List<string> VarNames { get; }         // co_varnames (지역변수명들)
        public int ArgCount { get; }                  // 매개변수 개수
        public int Flags { get; }                     // co_flags (CPython 호환)
        
        // CPython 호환 클로저 지원 (Phase 2)
        public List<string> FreeVars { get; set; } = new List<string>();   // co_freevars - 자유 변수
        public List<string> CellVars { get; set; } = new List<string>();   // co_cellvars - 셀 변수
        
        // CPython 호환 매개변수 기본값 지원 (Phase 3)
        public List<PyObject> DefaultValues { get; set; } = new List<PyObject>(); // 매개변수 기본값들 (NULL이면 기본값 없음)
        
        // CPython 3.12 Exception Table 지원
        public List<ExceptionTableEntry> ExceptionTable { get; set; } = new List<ExceptionTableEntry>();
        
        public PyCodeObject(string name, List<ByteCodeInstruction> instructions, 
                        List<PyObject> constants, List<string> names, 
                        List<string> varNames, int argCount = 0,
                        List<string> freeVars = null, List<string> cellVars = null,
                        List<PyObject> defaultValues = null, int flags = 0)
        {
            Name = name;
            Instructions = instructions;
            Constants = constants;
            Names = names;
            VarNames = varNames;
            ArgCount = argCount;
            Flags = flags;
            FreeVars = freeVars ?? new List<string>();
            CellVars = cellVars ?? new List<string>();
            DefaultValues = defaultValues ?? new List<PyObject>();
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
        
        /// <summary>
        /// 제너레이터 함수인지 확인 (yield 또는 yield from 명령어 포함 여부)
        /// </summary>
        public bool IsGenerator()
        {
            return Instructions.Any(inst => 
                inst.OpCode == ByteCodeOp.YIELD_VALUE || 
                inst.OpCode == ByteCodeOp.YIELD_FROM);
        }
        
        /// <summary>
        /// 코루틴 함수인지 확인 (CO_COROUTINE 플래그 또는 await 명령어 포함 여부)
        /// </summary>
        public bool IsCoroutine()
        {
            return (Flags & CO_COROUTINE) != 0;
        }
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("PyCodeObject.Evaluate() - 나중에 구현예정");
        }
    }

    // 추가 바이트코드 도구들
    
    // 바이트코드 빌더 (헬퍼 클래스)
    public class ByteCodeBuilder
    {
        private List<ByteCodeInstruction> instructions = new List<ByteCodeInstruction>();
        private List<PyObject> constants = new List<PyObject>();
        private List<string> names = new List<string>();
        private List<string> varNames = new List<string>();
        
        public ByteCodeBuilder AddInstruction(ByteCodeOp opCode, int arg = 0)
        {
            instructions.Add(new ByteCodeInstruction(opCode, arg));
            return this;
        }
        
        public ByteCodeBuilder LoadConst(PyObject value)
        {
            var index = constants.Count;
            constants.Add(value);
            return AddInstruction(ByteCodeOp.LOAD_CONST, index);
        }
        
        public ByteCodeBuilder LoadName(string name)
        {
            var index = GetOrAddName(name);
            return AddInstruction(ByteCodeOp.LOAD_NAME, index);
        }
        
        public ByteCodeBuilder StoreName(string name)
        {
            var index = GetOrAddName(name);
            return AddInstruction(ByteCodeOp.STORE_NAME, index);
        }
        
        private int GetOrAddName(string name)
        {
            var index = names.IndexOf(name);
            if (index == -1)
            {
                index = names.Count;
                names.Add(name);
            }
            return index;
        }
        
        public PyCodeObject Build(string name = "<module>", int argCount = 0)
        {
            return new PyCodeObject(name, instructions, constants, names, varNames, argCount);
        }
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("ByteCodeBuilder.Evaluate() - 나중에 구현예정");
        }
    }
    
    // Python 3.12 내장 함수 열거형
    public enum IntrinsicFunction : byte
    {
        PRINT = 0,
        IMPORT_STAR = 1,
        STOPITERATION_ERROR = 2,
        ASYNC_GEN_WRAP = 3,
        UNARY_POSITIVE = 4,
        LIST_TO_TUPLE = 5,
        TYPEVAR = 6,
        PARAMSPEC = 7,
        TYPEVARTUPLE = 8,
        SUBSCRIPT_GENERIC = 9,
        TYPEALIAS = 10
    }
    
    // 바이트코드 비교 연산자 열거형
    public enum CompareOp : byte
    {
        LT = 0,        // <
        LE = 1,        // <=
        EQ = 2,        // ==
        NE = 3,        // !=
        GT = 4,        // >
        GE = 5,        // >=
        IN = 6,        // in
        NOT_IN = 7,    // not in
        IS = 8,        // is
        IS_NOT = 9,    // is not
        EXC_MATCH = 10 // exception match
    }
    
    // 바이트코드 비역어거 도구
    public class ByteCodeDisassembler
    {
        public static void Disassemble(PyCodeObject code)
        {
            Console.WriteLine($"\n📄 Disassembly of {code.Name}:");
            Console.WriteLine($"  Instructions: {code.Instructions.Count}");
            Console.WriteLine($"  Constants: {code.Constants.Count}");
            Console.WriteLine($"  Names: {code.Names.Count}");
            Console.WriteLine($"  VarNames: {code.VarNames.Count}");
            Console.WriteLine($"  ArgCount: {code.ArgCount}");
            Console.WriteLine();
            
            for (int i = 0; i < code.Instructions.Count; i++)
            {
                var inst = code.Instructions[i];
                var extra = GetInstructionExtra(inst, code);
                Console.WriteLine($"  {i,3}: {inst.OpCode,-25} {inst.Argument,3} {extra}");
            }
        }
        
        private static string GetInstructionExtra(ByteCodeInstruction inst, PyCodeObject code)
        {
            switch (inst.OpCode)
            {
                case ByteCodeOp.LOAD_CONST:
                    if (inst.Argument < code.Constants.Count)
                        return $"({code.Constants[inst.Argument]})";
                    break;
                    
                case ByteCodeOp.LOAD_NAME:
                case ByteCodeOp.STORE_NAME:
                case ByteCodeOp.DELETE_NAME:
                case ByteCodeOp.LOAD_GLOBAL:
                case ByteCodeOp.STORE_GLOBAL:
                case ByteCodeOp.DELETE_GLOBAL:
                case ByteCodeOp.LOAD_ATTR:
                case ByteCodeOp.STORE_ATTR:
                case ByteCodeOp.DELETE_ATTR:
                case ByteCodeOp.IMPORT_NAME:
                case ByteCodeOp.IMPORT_FROM:
                    if (inst.Argument < code.Names.Count)
                        return $"({code.Names[inst.Argument]})";
                    break;
                    
                case ByteCodeOp.LOAD_FAST:
                case ByteCodeOp.STORE_FAST:
                case ByteCodeOp.DELETE_FAST:
                    if (inst.Argument < code.VarNames.Count)
                        return $"({code.VarNames[inst.Argument]})";
                    break;
                    
                case ByteCodeOp.COMPARE_OP:
                    var compareOps = new[] { "<", "<=", "==", "!=", ">", ">=", "in", "not in", "is", "is not", "exception match" };
                    if (inst.Argument < compareOps.Length)
                        return $"({compareOps[inst.Argument]})";
                    break;
                    
                case ByteCodeOp.CALL_INTRINSIC_1:
                case ByteCodeOp.CALL_INTRINSIC_2:
                    var intrinsics = new[] { "PRINT", "IMPORT_STAR", "STOPITERATION_ERROR", "ASYNC_GEN_WRAP", "UNARY_POSITIVE", "LIST_TO_TUPLE", "TYPEVAR", "PARAMSPEC", "TYPEVARTUPLE", "SUBSCRIPT_GENERIC", "TYPEALIAS" };
                    if (inst.Argument < intrinsics.Length)
                        return $"({intrinsics[inst.Argument]})";
                    break;
            }
            return "";
        }
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("ByteCodeDisassembler.Evaluate() - 나중에 구현예정");
        }
    }
    
    // 점프 타겟 정보
    public class JumpTarget
    {
        public int Offset { get; set; } = -1;
        public string Label { get; }
        
        public JumpTarget(string label)
        {
            Label = label;
        }
        
        public override string ToString() => $"Label({Label})";
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("JumpTarget.Evaluate() - 나중에 구현예정");
        }
    }

    #endregion

    // CPython 3.12: 스택 효과 분석 시스템
    public static class StackEffectAnalyzer
    {
        // 각 바이트코드의 스택 효과 (pop_count, push_count)
        private static readonly Dictionary<ByteCodeOp, (int pop, int push)> _fixedStackEffects = new()
        {
            // 기본 연산들
            { ByteCodeOp.POP_TOP, (1, 0) },
            { ByteCodeOp.ROT_TWO, (2, 2) },
            { ByteCodeOp.ROT_THREE, (3, 3) },
            { ByteCodeOp.DUP_TOP, (1, 2) },
            { ByteCodeOp.DUP_TOP_TWO, (2, 4) },
            
            // 산술 연산
            { ByteCodeOp.BINARY_ADD, (2, 1) },
            { ByteCodeOp.BINARY_SUBTRACT, (2, 1) },
            { ByteCodeOp.BINARY_MULTIPLY, (2, 1) },
            { ByteCodeOp.BINARY_DIVIDE, (2, 1) },
            { ByteCodeOp.BINARY_FLOOR_DIVIDE, (2, 1) },
            { ByteCodeOp.BINARY_MODULO, (2, 1) },
            { ByteCodeOp.BINARY_POWER, (2, 1) },
            { ByteCodeOp.UNARY_POSITIVE, (1, 1) },
            { ByteCodeOp.UNARY_NEGATIVE, (1, 1) },
            { ByteCodeOp.UNARY_NOT, (1, 1) },
            { ByteCodeOp.UNARY_INVERT, (1, 1) },
            
            // 비교 연산
            { ByteCodeOp.COMPARE_OP, (2, 1) },
            
            // 로드/저장
            { ByteCodeOp.LOAD_CONST, (0, 1) },
            { ByteCodeOp.LOAD_NAME, (0, 1) },
            { ByteCodeOp.LOAD_GLOBAL, (0, 1) },
            { ByteCodeOp.LOAD_FAST, (0, 1) },
            { ByteCodeOp.STORE_NAME, (1, 0) },
            { ByteCodeOp.STORE_GLOBAL, (1, 0) },
            { ByteCodeOp.STORE_FAST, (1, 0) },
            
            // 속성 조작
            { ByteCodeOp.LOAD_ATTR, (1, 1) },
            { ByteCodeOp.STORE_ATTR, (2, 0) },
            { ByteCodeOp.DELETE_ATTR, (1, 0) },
            
            // 인덱싱
            { ByteCodeOp.LOAD_SUBSCR, (2, 1) },
            { ByteCodeOp.STORE_SUBSCR, (3, 0) },
            { ByteCodeOp.DELETE_SUBSCR, (2, 0) },
            
            // 반환/yield
            { ByteCodeOp.RETURN_VALUE, (1, 0) },
            { ByteCodeOp.YIELD_VALUE, (1, 1) },
            
            // 점프 (스택에 영향 없음)
            { ByteCodeOp.JUMP_FORWARD, (0, 0) },
            { ByteCodeOp.JUMP_BACKWARD, (0, 0) },
            { ByteCodeOp.POP_JUMP_IF_TRUE, (1, 0) },
            { ByteCodeOp.POP_JUMP_IF_FALSE, (1, 0) },
            { ByteCodeOp.JUMP_IF_TRUE_OR_POP, (0, 0) }, // 조건부
            { ByteCodeOp.JUMP_IF_FALSE_OR_POP, (0, 0) }, // 조건부
            
            // CPython 3.12: with 문  
            { ByteCodeOp.BEFORE_WITH, (1, 2) }, // context_manager -> __exit__, result
            // CPython 3.12 호환: WITH_EXCEPT_START는 __exit__만 pop하고 boolean push
            // PUSH_EXC_INFO 항목들은 그대로 두고 나중에 POP_EXCEPT에서 처리
            { ByteCodeOp.WITH_EXCEPT_START, (1, 1) }, // __exit__ -> suppress_boolean
            
            // CPython 3.12: 예외 처리
            { ByteCodeOp.PUSH_EXC_INFO, (1, 4) }, // exception -> exc_type, exc_value, exc_tb, lasti
            { ByteCodeOp.POP_EXCEPT, (4, 0) }, // exc_type, exc_value, exc_tb, lasti -> (nothing)
            
            // 이터레이션
            { ByteCodeOp.GET_ITER, (1, 1) },
            { ByteCodeOp.FOR_ITER, (1, 2) }, // iter -> iter, value (성공시) 또는 iter -> (실패시)
            
            // CPython 3.12 새로운 호출 시스템
            { ByteCodeOp.PUSH_NULL, (0, 1) },
            { ByteCodeOp.RESUME, (0, 0) },
            
            // CPython 3.12: 예외 그룹 처리
            { ByteCodeOp.CHECK_EG_MATCH, (2, 2) }, // exception_group, match_type -> matched, remainder
        };

        // 인수에 따라 달라지는 스택 효과
        public static (int pop, int push) GetStackEffect(ByteCodeOp op, int arg = 0)
        {
            // 고정 스택 효과가 있는 경우
            if (_fixedStackEffects.TryGetValue(op, out var effect))
                return effect;

            // 인수에 따라 달라지는 경우들
            return op switch
            {
                // 컨테이너 생성
                ByteCodeOp.BUILD_TUPLE => (arg, 1),
                ByteCodeOp.BUILD_LIST => (arg, 1),
                ByteCodeOp.BUILD_SET => (arg, 1),
                ByteCodeOp.BUILD_MAP => (2 * arg, 1), // key-value 쌍들
                ByteCodeOp.BUILD_SLICE => (arg switch { 2 => 2, 3 => 3, _ => 2 }, 1),
                
                // 언패킹
                ByteCodeOp.UNPACK_SEQUENCE => (1, arg),
                ByteCodeOp.UNPACK_EX => (1, (arg & 0xFF) + (arg >> 8) + 1),
                
                // 함수 생성 및 호출
                ByteCodeOp.MAKE_FUNCTION => (1 + GetMakeFunctionExtraArgs(arg), 1),
                ByteCodeOp.CALL_FUNCTION => (1 + arg, 1), // func + args -> result
                ByteCodeOp.CALL_FUNCTION_KW => (2 + arg, 1), // func + args + kwargs -> result
                ByteCodeOp.CALL_FUNCTION_EX => ((arg & 1) != 0 ? 3 : 2, 1), // func + args + (kwargs?) -> result
                
                // CPython 3.12: 새로운 CALL
                ByteCodeOp.CALL => (1 + arg, 1), // func + args -> result
                
                // 컴프리헨션
                ByteCodeOp.LIST_APPEND => (1, 0), // arg는 리스트 위치 (상대적)
                ByteCodeOp.SET_ADD => (1, 0),
                ByteCodeOp.MAP_ADD => (2, 0), // key, value
                
                // 예외 발생
                ByteCodeOp.RAISE_VARARGS => (arg, 0),
                
                // 복사
                ByteCodeOp.COPY => (0, 1), // N번째 요소 복사 (arg = N)
                
                _ => (0, 0) // 알 수 없는 경우 안전한 기본값
            };
        }

        // MAKE_FUNCTION의 추가 인수 개수 계산
        private static int GetMakeFunctionExtraArgs(int flags)
        {
            int extra = 0;
            if ((flags & 0x01) != 0) extra++; // defaults
            if ((flags & 0x02) != 0) extra++; // kwdefaults  
            if ((flags & 0x04) != 0) extra++; // annotations
            if ((flags & 0x08) != 0) extra++; // closure
            return extra;
        }

        // 특정 명령어의 최소 스택 요구사항 계산
        public static int GetMinStackRequirement(ByteCodeOp op, int arg = 0)
        {
            var (pop, _) = GetStackEffect(op, arg);
            return pop;
        }

        // 명령어 시퀀스의 스택 효과 분석
        public static int AnalyzeStackSequence(List<ByteCodeInstruction> instructions, int startIndex, int count)
        {
            int netEffect = 0;
            int minStackRequired = 0;
            int currentStack = 0;

            for (int i = 0; i < count && startIndex + i < instructions.Count; i++)
            {
                var instruction = instructions[startIndex + i];
                var (pop, push) = GetStackEffect(instruction.OpCode, instruction.Argument);
                
                // 이 명령어 실행 전에 필요한 스택 크기 확인
                if (currentStack < pop)
                    minStackRequired = Math.Max(minStackRequired, pop - currentStack);
                
                currentStack -= pop;
                currentStack += push;
                netEffect = currentStack;
            }

            return netEffect;
        }

        // CPython 3.12 호환: WITH_EXCEPT_START 스택 요구사항
        public static class WithStatementStack
        {
            public const int BEFORE_WITH_MIN_STACK = 1;      // context manager
            public const int BEFORE_WITH_RESULT_STACK = 2;   // __exit__, result
            
            public const int WITH_EXCEPT_START_MIN_STACK = 6; // [...prev_stack, __exit__, exc, exc_type, exc_value, exc_tb, lasti] - __exit__만 pop
            public const int WITH_EXCEPT_START_RESULT_STACK = 1; // suppress boolean
            
            public const int PUSH_EXC_INFO_MIN_STACK = 1;    // exception
            public const int PUSH_EXC_INFO_RESULT_STACK = 4; // exc_type, exc_value, exc_tb, lasti
            
            public const int POP_EXCEPT_MIN_STACK = 4;       // exc_type, exc_value, exc_tb, lasti
            public const int POP_EXCEPT_RESULT_STACK = 0;    // (nothing)
            
            // CPython 3.12 호환: with문 정리 시 __exit__ 호출 인수 개수
            public const int EXIT_METHOD_ARGS_COUNT = 3;     // __exit__(exc_type, exc_value, exc_tb)
        }

        // CPython 3.12 호환: MAKE_FUNCTION 플래그 상수
        public static class MakeFunctionFlags
        {
            public const int DEFAULTS = 0x01;        // 기본값 있음
            public const int KWDEFAULTS = 0x02;      // 키워드 기본값 있음
            public const int ANNOTATIONS = 0x04;     // 타입 어노테이션 있음
            public const int CLOSURE = 0x08;         // 클로저 있음
        }
    }

    // CPython 3.12: 점프 명령어 통합 관리 시스템
    public static class JumpInstructionManager
    {
        // 점프 명령어 타입 분류
        public enum JumpType
        {
            Forward,        // 앞으로 점프
            Backward,       // 뒤로 점프  
            Conditional,    // 조건부 점프
            Absolute        // 절대 점프
        }

        // 점프 명령어 분류
        private static readonly Dictionary<ByteCodeOp, JumpType> _jumpTypes = new()
        {
            { ByteCodeOp.JUMP_FORWARD, JumpType.Forward },
            { ByteCodeOp.JUMP_BACKWARD, JumpType.Backward },
            { ByteCodeOp.POP_JUMP_IF_TRUE, JumpType.Conditional },
            { ByteCodeOp.POP_JUMP_IF_FALSE, JumpType.Conditional },
            { ByteCodeOp.JUMP_IF_TRUE_OR_POP, JumpType.Conditional },
            { ByteCodeOp.JUMP_IF_FALSE_OR_POP, JumpType.Conditional },
        };

        // CPython 3.12 호환: 점프 오프셋 계산 (하드코딩 제거)
        public static int CalculateJumpOffset(ByteCodeOp jumpOp, int currentIP, int targetOffset, bool hasMainLoopIncrement = true)
        {
            if (!_jumpTypes.ContainsKey(jumpOp))
                return targetOffset; // 점프 명령어가 아닌 경우

            var jumpType = _jumpTypes[jumpOp];
            int adjustedOffset = targetOffset;

            // CPython 3.12: 메인 루프가 IP를 자동 증가시키는 경우 보정
            if (hasMainLoopIncrement)
            {
                switch (jumpType)
                {
                    case JumpType.Forward:
                    case JumpType.Conditional:
                    case JumpType.Absolute:
                        // 절대 오프셋은 -1 보정 필요 (메인 루프에서 +1 될 예정)
                        adjustedOffset = targetOffset - 1;
                        break;
                    case JumpType.Backward:
                        // 상대 오프셋은 보정 없음
                        adjustedOffset = targetOffset;
                        break;
                }
            }
            else
            {
                // 메인 루프 자동 증가 없는 경우 그대로 사용
                adjustedOffset = targetOffset;
            }

            return adjustedOffset;
        }

        // 점프 명령어인지 확인
        public static bool IsJumpInstruction(ByteCodeOp op)
        {
            return _jumpTypes.ContainsKey(op);
        }

        // 점프 타입 반환
        public static JumpType GetJumpType(ByteCodeOp op)
        {
            return _jumpTypes.TryGetValue(op, out var type) ? type : JumpType.Absolute;
        }
    }
}