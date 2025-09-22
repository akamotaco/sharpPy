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

    // CPython 3.12 완전 호환 바이트코드 명령어
    // 출처: CPython 3.12.0 dis.opname
    public enum ByteCodeOp : int
    {
        // CPython 3.12 정확한 opcode 번호 매핑
        CACHE = 0,
        POP_TOP = 1,
        PUSH_NULL = 2,
        INTERPRETER_EXIT = 3,
        END_FOR = 4,
        END_SEND = 5,
        
        // 6-8: Reserved for specialized opcodes in CPython 3.12
        
        NOP = 9,
        
        // 10: Reserved
        
        UNARY_NEGATIVE = 11,
        UNARY_NOT = 12,
        
        // 13: UNARY_POSITIVE
        UNARY_POSITIVE = 13,
        
        // 14: Reserved
        
        UNARY_INVERT = 15,
        
        // 16: Reserved
        
        RESERVED = 17,
        
        // 18-24: Reserved for specialized opcodes
        
        BINARY_SUBSCR = 25,
        BINARY_SLICE = 26,
        STORE_SLICE = 27,
        
        // 28-29: Reserved
        
        GET_LEN = 30,
        MATCH_MAPPING = 31,
        MATCH_SEQUENCE = 32,
        MATCH_KEYS = 33,
        
        // 34: Reserved
        
        PUSH_EXC_INFO = 35,
        CHECK_EXC_MATCH = 36,
        CHECK_EG_MATCH = 37,
        
        // 38-48: Reserved
        
        WITH_EXCEPT_START = 49,
        GET_AITER = 50,
        GET_ANEXT = 51,
        BEFORE_ASYNC_WITH = 52,
        BEFORE_WITH = 53,
        END_ASYNC_FOR = 54,
        CLEANUP_THROW = 55,
        
        // 56-59: Reserved
        
        STORE_SUBSCR = 60,
        DELETE_SUBSCR = 61,
        
        // 62-67: Reserved
        
        GET_ITER = 68,
        GET_YIELD_FROM_ITER = 69,
        
        // 70: Reserved
        
        LOAD_BUILD_CLASS = 71,
        
        // 72-73: Reserved
        
        LOAD_ASSERTION_ERROR = 74,
        RETURN_GENERATOR = 75,
        
        // 76-82: Reserved
        
        RETURN_VALUE = 83,
        
        // 84: Reserved
        
        SETUP_ANNOTATIONS = 85,
        
        // 86: Reserved
        
        LOAD_LOCALS = 87,
        
        // 88: Reserved
        
        POP_EXCEPT = 89,
        STORE_NAME = 90,
        DELETE_NAME = 91,
        UNPACK_SEQUENCE = 92,
        FOR_ITER = 93,
        UNPACK_EX = 94,
        STORE_ATTR = 95,
        DELETE_ATTR = 96,
        STORE_GLOBAL = 97,
        DELETE_GLOBAL = 98,
        SWAP = 99,
        LOAD_CONST = 100,
        LOAD_NAME = 101,
        BUILD_TUPLE = 102,
        BUILD_LIST = 103,
        BUILD_SET = 104,
        BUILD_MAP = 105,
        LOAD_ATTR = 106,
        COMPARE_OP = 107,
        IMPORT_NAME = 108,
        IMPORT_FROM = 109,
        JUMP_FORWARD = 110,
        
        // 111-113: Reserved
        
        POP_JUMP_IF_FALSE = 114,
        POP_JUMP_IF_TRUE = 115,
        LOAD_GLOBAL = 116,
        IS_OP = 117,
        CONTAINS_OP = 118,
        RERAISE = 119,
        COPY = 120,
        RETURN_CONST = 121,
        BINARY_OP = 122,
        SEND = 123,
        LOAD_FAST = 124,
        STORE_FAST = 125,
        DELETE_FAST = 126,
        LOAD_FAST_CHECK = 127,
        POP_JUMP_IF_NOT_NONE = 128,
        POP_JUMP_IF_NONE = 129,
        RAISE_VARARGS = 130,
        GET_AWAITABLE = 131,
        MAKE_FUNCTION = 132,
        BUILD_SLICE = 133,
        JUMP_BACKWARD_NO_INTERRUPT = 134,
        MAKE_CELL = 135,
        LOAD_CLOSURE = 136,
        LOAD_DEREF = 137,
        STORE_DEREF = 138,
        DELETE_DEREF = 139,
        JUMP_BACKWARD = 140,
        LOAD_SUPER_ATTR = 141,
        CALL_FUNCTION_EX = 142,
        LOAD_FAST_AND_CLEAR = 143,
        EXTENDED_ARG = 144,
        LIST_APPEND = 145,
        SET_ADD = 146,
        MAP_ADD = 147,
        
        // 148: Reserved
        
        COPY_FREE_VARS = 149,
        YIELD_VALUE = 150,
        RESUME = 151,
        MATCH_CLASS = 152,
        
        // 153-154: Reserved
        
        FORMAT_VALUE = 155,
        BUILD_CONST_KEY_MAP = 156,
        BUILD_STRING = 157,
        
        // 158-161: Reserved
        
        LIST_EXTEND = 162,
        SET_UPDATE = 163,
        DICT_MERGE = 164,
        DICT_UPDATE = 165,
        
        // 166-170: Reserved
        
        CALL = 171,
        KW_NAMES = 172,
        CALL_INTRINSIC_1 = 173,
        CALL_INTRINSIC_2 = 174,
        LOAD_FROM_DICT_OR_GLOBALS = 175,
        LOAD_FROM_DICT_OR_DEREF = 176,
        
        // 177-236: Reserved (CPython uses these for specialized/instrumented opcodes)
        
        // Instrumented opcodes (237-254)
        INSTRUMENTED_LOAD_SUPER_ATTR = 237,
        INSTRUMENTED_POP_JUMP_IF_NONE = 238,
        INSTRUMENTED_POP_JUMP_IF_NOT_NONE = 239,
        INSTRUMENTED_RESUME = 240,
        INSTRUMENTED_CALL = 241,
        INSTRUMENTED_RETURN_VALUE = 242,
        INSTRUMENTED_YIELD_VALUE = 243,
        INSTRUMENTED_CALL_FUNCTION_EX = 244,
        INSTRUMENTED_JUMP_FORWARD = 245,
        INSTRUMENTED_JUMP_BACKWARD = 246,
        INSTRUMENTED_RETURN_CONST = 247,
        INSTRUMENTED_FOR_ITER = 248,
        INSTRUMENTED_POP_JUMP_IF_FALSE = 249,
        INSTRUMENTED_POP_JUMP_IF_TRUE = 250,
        INSTRUMENTED_END_FOR = 251,
        INSTRUMENTED_END_SEND = 252,
        INSTRUMENTED_INSTRUCTION = 253,
        INSTRUMENTED_LINE = 254,
        
        // 255: Reserved
        
        // =============================================================================
        // CPython 3.12 Adaptive Specialization - Specialized Instructions
        // Range: 300-399 (SharpPy specific range for specialized operations)
        // =============================================================================
        
        // Binary Operation Specializations (300-309)
        BINARY_ADD_INT = 300,
        BINARY_ADD_FLOAT = 301,
        BINARY_ADD_UNICODE = 302,
        BINARY_MULTIPLY_INT = 303,
        BINARY_MULTIPLY_FLOAT = 304,
        BINARY_SUBTRACT_INT = 305,
        BINARY_SUBTRACT_FLOAT = 306,
        BINARY_TRUE_DIVIDE_FLOAT = 307,
        BINARY_FLOOR_DIVIDE_INT = 308,
        BINARY_MODULO_INT = 309,
        
        // Method Call Specializations (310-319)
        CALL_LIST_APPEND = 310,
        CALL_DICT_GET = 311,
        CALL_STR_UPPER = 312,
        CALL_STR_LOWER = 313,
        CALL_STR_STRIP = 314,
        CALL_LEN_LIST = 315,
        CALL_LEN_STR = 316,
        CALL_TYPE_1 = 317,
        CALL_ISINSTANCE = 318,
        CALL_BUILTIN_FAST = 319,
        
        // Global/Attribute Access Specializations (320-329)
        LOAD_GLOBAL_BUILTIN = 320,
        LOAD_GLOBAL_MODULE = 321,
        STORE_ATTR_INSTANCE_VALUE = 322,
        LOAD_ATTR_INSTANCE_VALUE = 323,
        STORE_SUBSCR_LIST_INT = 324,
        LOAD_SUBSCR_LIST_INT = 325,
        STORE_SUBSCR_DICT_STR = 326,
        LOAD_SUBSCR_DICT_STR = 327,
        FOR_ITER_LIST = 328,
        FOR_ITER_TUPLE = 329
        
        // =============================================================================  
        // 이 enum은 CPython 3.12.0과 100% 호환됩니다 (기본 범위)
        // 특수화된 명령어는 SharpPy 확장 (300+)
        // =============================================================================
    }

    // CPython 3.12 정확한 BINARY_OP 연산 타입 (CPython 순서와 100% 일치)
    public enum BinaryOpType : byte
    {
        // CPython 3.12 정확한 순서 (Python/bytecodes.c BINARY_OP_* 순서)
        ADD = 0,                  // +  (덧셈) ✅ 이미 정확
        SUBTRACT = 1,             // -  (뺄셈) - was 10
        MULTIPLY = 2,             // *  (곱셈) - was 5
        TRUE_DIVIDE = 3,          // /  (나눗셈) - was 11
        FLOOR_DIVIDE = 4,         // // (바닥 나눗셈) - was 2
        MODULO = 5,               // %  (모듈로) - was 6
        POWER = 6,                // ** (거듭제곱) - was 8
        LSHIFT = 7,               // << (좌시프트) - was 3
        RSHIFT = 8,               // >> (우시프트) - was 9
        OR = 9,                   // |  (비트 OR) - was 7
        XOR = 10,                 // ^  (비트 XOR) - was 12
        AND = 11,                 // &  (비트 AND) - was 1
        MATRIX_MULTIPLY = 12      // @  (행렬 곱셈) - was 4
    }

    // 바이트코드 명령 구조체
    public struct ByteCodeInstruction
    {
        public ByteCodeOp OpCode { get; }
        public int Argument { get; }
        public int LineNumber { get; }      // Source line number (1-based)
        public int ColumnOffset { get; }    // Source column offset (0-based)
        public string? FileName { get; }    // Source file name
        
        public ByteCodeInstruction(ByteCodeOp opCode, int argument = 0, int lineNumber = -1, int columnOffset = -1, string? fileName = null)
        {
            OpCode = opCode;
            Argument = argument;
            LineNumber = lineNumber;
            ColumnOffset = columnOffset;
            FileName = fileName;
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
        public const int CO_ASYNC_GENERATOR = 0x200; // 비동기 제너레이터 (async def + yield)
        
        public string Name { get; }
        public List<ByteCodeInstruction> Instructions { get; }
        public List<PyObject> Constants { get; }      // co_consts
        public List<string> Names { get; }            // co_names (변수명들)
        public List<string> VarNames { get; }         // co_varnames (지역변수명들)
        public int ArgCount { get; }                  // 매개변수 개수
        public int PosonlyArgCount { get; }           // co_posonlyargcount (positional-only 매개변수 개수)
        public int Flags { get; }                     // co_flags (CPython 호환)
        public bool IsOptimized { get; }              // 바이트코드 최적화 여부
        public string? FileName { get; }              // co_filename (CPython 호환)
        
        // CPython 스타일 에러 보고를 위한 소스 라인 정보
        public List<string>? SourceLines { get; }     // 원본 소스 코드 라인들 (에러 표시용)
        
        // CPython 호환 클로저 지원 (Phase 2)
        public List<string> FreeVars { get; set; } = new List<string>();   // co_freevars - 자유 변수
        public List<string> CellVars { get; set; } = new List<string>();   // co_cellvars - 셀 변수
        
        // CPython 3.12 추가 CO_* 플래그 상수들
        public const int CO_OPTIMIZED = 0x0001;         // 지역 변수 최적화
        public const int CO_NEWLOCALS = 0x0002;         // 새로운 지역 변수 네임스페이스
        public const int CO_NESTED = 0x0010;            // 중첩된 함수
        public const int CO_ITERABLE_COROUTINE = 0x0100; // 반복 가능한 코루틴
        
        // CPython 호환 매개변수 기본값 지원 (Phase 3)
        public List<PyObject> DefaultValues { get; set; } = new List<PyObject>(); // 매개변수 기본값들 (NULL이면 기본값 없음)
        
        // CPython 3.12 Exception Table 지원
        public List<ExceptionTableEntry> ExceptionTable { get; set; } = new List<ExceptionTableEntry>();
        
        // Line Number Table: instruction offset → source line number mapping
        public Dictionary<int, int> LineNumberTable { get; set; } = new Dictionary<int, int>();
        
        public PyCodeObject(string name, List<ByteCodeInstruction> instructions,
                        List<PyObject> constants, List<string> names,
                        List<string> varNames, int argCount = 0, int posonlyArgCount = 0,
                        List<string> freeVars = null, List<string> cellVars = null,
                        List<PyObject> defaultValues = null, int flags = 0, string fileName = null,
                        List<string> sourceLines = null, bool isOptimized = false,
                        Dictionary<int, int> lineNumberTable = null,
                        List<ExceptionTableEntry> exceptionTable = null)
        {
            Name = name;
            Instructions = instructions;
            Constants = constants;
            Names = names;
            VarNames = varNames;
            ArgCount = argCount;
            PosonlyArgCount = posonlyArgCount;
            Flags = flags;
            IsOptimized = isOptimized;
            FileName = fileName;
            SourceLines = sourceLines;
            FreeVars = freeVars ?? new List<string>();
            CellVars = cellVars ?? new List<string>();
            DefaultValues = defaultValues ?? new List<PyObject>();
            ExceptionTable = exceptionTable ?? new List<ExceptionTableEntry>(); // 전달된 exception table 보존
            LineNumberTable = lineNumberTable ?? new Dictionary<int, int>();
        }
        
        
        public override string GetTypeName() => "code";

        // CPython 3.12 호환: co_* 속성들 지원
        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "co_flags" => new PyInt(Flags),
                "co_name" => new PyString(Name),
                "co_argcount" => new PyInt(ArgCount),
                "co_varnames" => new PyTuple(VarNames.Select(n => new PyString(n) as PyObject).ToArray()),
                "co_names" => new PyTuple(Names.Select(n => new PyString(n) as PyObject).ToArray()),
                "co_consts" => new PyTuple(Constants.ToArray()),
                "co_freevars" => new PyTuple(FreeVars.Select(n => new PyString(n) as PyObject).ToArray()),
                "co_cellvars" => new PyTuple(CellVars.Select(n => new PyString(n) as PyObject).ToArray()),
                "co_filename" => new PyString(FileName ?? "<unknown>"),
                _ => base.GetAttribute(name)
            };
        }
        
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
                        extra = $"({FormatConstantForDisplay(Constants[inst.Argument])})";
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
                
                Console.WriteLine($"  {i*2,3}: {inst,-25} {extra}");
            }

            // Display Exception Table if present (CPython 3.12 compatible format)
            if (ExceptionTable.Count > 0)
            {
                Console.WriteLine("ExceptionTable:");
                foreach (var entry in ExceptionTable)
                {
                    var lastiFlag = entry.Lasti ? " lasti" : "";
                    Console.WriteLine($"  {entry.StartOffset} to {entry.EndOffset} -> {entry.HandlerOffset} [{entry.Depth}]{lastiFlag}");
                }
            }
        }

        /// <summary>
        /// CPython 3.12 호환 상수 표시 형식
        /// </summary>
        private string FormatConstantForDisplay(PyObject constant)
        {
            if (constant == null)
                return "None";

            switch (constant)
            {
                case PyString pyStr:
                    // 문자열은 따옴표로 감싸기 (CPython 3.12 스타일)
                    return $"'{pyStr.Value}'";

                case PyInt pyInt:
                    return pyInt.Value.ToString();

                case PyFloat pyFloat:
                    return pyFloat.Value.ToString();

                case PyBool pyBool:
                    return pyBool.Value ? "True" : "False";

                case PyNone:
                    return "None";

                case PyList pyList:
                    return "[...]"; // 간략히 표시

                case PyDict pyDict:
                    return "{...}"; // 간략히 표시

                case PyTuple pyTuple:
                    return "(...)"; // 간략히 표시

                default:
                    // 기타 객체들 (함수, 클래스 등)
                    return constant.ToString();
            }
        }

        public override string ToString() => $"<code object {Name}>";
        
        /// <summary>
        /// 제너레이터 함수인지 확인 (yield 또는 yield from 명령어 포함 여부)
        /// </summary>
        public bool IsGenerator()
        {
            return Instructions.Any(inst => 
                inst.OpCode == ByteCodeOp.YIELD_VALUE); // CPython 3.12: YIELD_FROM removed
        }
        
        /// <summary>
        /// 코루틴 함수인지 확인 (CO_COROUTINE 플래그 또는 await 명령어 포함 여부)
        /// </summary>
        public bool IsCoroutine()
        {
            return (Flags & CO_COROUTINE) != 0;
        }
        
        /// <summary>
        /// 비동기 제너레이터 함수인지 확인 (CO_ASYNC_GENERATOR 플래그)
        /// </summary>
        public bool IsAsyncGenerator()
        {
            return (Flags & CO_ASYNC_GENERATOR) != 0;
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
            return new PyCodeObject(name, instructions, constants, names, varNames, argCount, 0);
        }
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("ByteCodeBuilder.Evaluate() - 나중에 구현예정");
        }
    }
    
    // CPython 3.12 정확한 내장 함수 열거형 (pycore_intrinsics.h 호환)
    public enum IntrinsicFunction : byte
    {
        INTRINSIC_1_INVALID = 0,            // CPython 3.12: INTRINSIC_1_INVALID
        INTRINSIC_PRINT = 1,                // CPython 3.12: INTRINSIC_PRINT (was PRINT = 0)
        INTRINSIC_IMPORT_STAR = 2,          // CPython 3.12: INTRINSIC_IMPORT_STAR (was IMPORT_STAR = 1)
        INTRINSIC_STOPITERATION_ERROR = 3,  // CPython 3.12: INTRINSIC_STOPITERATION_ERROR (was STOPITERATION_ERROR = 2)
        INTRINSIC_ASYNC_GEN_WRAP = 4,       // CPython 3.12: INTRINSIC_ASYNC_GEN_WRAP (was ASYNC_GEN_WRAP = 3)
        INTRINSIC_UNARY_POSITIVE = 5,       // CPython 3.12: INTRINSIC_UNARY_POSITIVE (was UNARY_POSITIVE = 4)
        INTRINSIC_LIST_TO_TUPLE = 6,        // CPython 3.12: INTRINSIC_LIST_TO_TUPLE (was LIST_TO_TUPLE = 5)
        INTRINSIC_TYPEVAR = 7,              // CPython 3.12: INTRINSIC_TYPEVAR ✅ 이미 정확
        INTRINSIC_PARAMSPEC = 8,            // CPython 3.12: INTRINSIC_PARAMSPEC ✅ 이미 정확
        INTRINSIC_TYPEVARTUPLE = 9,         // CPython 3.12: INTRINSIC_TYPEVARTUPLE ✅ 이미 정확
        INTRINSIC_SUBSCRIPT_GENERIC = 10,   // CPython 3.12: INTRINSIC_SUBSCRIPT_GENERIC ✅ 이미 정확
        INTRINSIC_TYPEALIAS = 11            // CPython 3.12: INTRINSIC_TYPEALIAS ✅ 이미 정확
    }
    
    // CPython 3.12 정확한 비교 연산자 열거형 (복잡한 바이트 인코딩 사용)
    public enum CompareOp : byte
    {
        // CPython 3.12 실제 바이트코드 인코딩 값들
        LT = 2,        // <  (CPython: 0x02, was 0)
        IS_NOT = 3,    // is not (CPython: 0x03) - Exception Groups에서 사용
        LE = 26,       // <= (CPython: 0x1A, was 1)  
        EQ = 40,       // == (CPython: 0x28, was 2)
        NE = 55,       // != (CPython: 0x37, was 3)
        GT = 68,       // >  (CPython: 0x44, was 4)
        GE = 92,       // >= (CPython: 0x5C, was 5)
        // 주의: is/is not은 CPython 3.12에서 IS_OP로 이동됨
        EXC_MATCH = 8  // exception match (예외 매칭용)
    }

    // CPython 3.12: 멤버십 테스트 연산자 열거형
    public enum ContainsOp : byte
    {
        IN = 0,        // in
        NOT_IN = 1     // not in
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
                var extra = GetInstructionExtra(inst, code, i);
                Console.WriteLine($"  {i,3}: {inst.OpCode,-25} {inst.Argument,3} {extra}");
            }
        }
        
        private static string GetInstructionExtra(ByteCodeInstruction inst, PyCodeObject code, int instructionIndex)
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
                    var compareOps = new[] { "<", "<=", "==", "!=", ">", ">=", "is", "is not", "exception match" };
                    if (inst.Argument < compareOps.Length)
                        return $"({compareOps[inst.Argument]})";
                    break;
                    
                case ByteCodeOp.CONTAINS_OP:
                    var containsOps = new[] { "in", "not in" };
                    if (inst.Argument < containsOps.Length)
                        return $"({containsOps[inst.Argument]})";
                    break;
                    
                case ByteCodeOp.CALL_INTRINSIC_1:
                case ByteCodeOp.CALL_INTRINSIC_2:
                    // CPython 3.12 정확한 intrinsic function 이름들
                    var intrinsics = new[] {
                        "INVALID",              // 0: INTRINSIC_1_INVALID
                        "PRINT",                // 1: INTRINSIC_PRINT
                        "IMPORT_STAR",          // 2: INTRINSIC_IMPORT_STAR
                        "STOPITERATION_ERROR",  // 3: INTRINSIC_STOPITERATION_ERROR
                        "ASYNC_GEN_WRAP",       // 4: INTRINSIC_ASYNC_GEN_WRAP
                        "UNARY_POSITIVE",       // 5: INTRINSIC_UNARY_POSITIVE
                        "LIST_TO_TUPLE",        // 6: INTRINSIC_LIST_TO_TUPLE
                        "TYPEVAR",              // 7: INTRINSIC_TYPEVAR
                        "PARAMSPEC",            // 8: INTRINSIC_PARAMSPEC
                        "TYPEVARTUPLE",         // 9: INTRINSIC_TYPEVARTUPLE
                        "SUBSCRIPT_GENERIC",    // 10: INTRINSIC_SUBSCRIPT_GENERIC
                        "TYPEALIAS"             // 11: INTRINSIC_TYPEALIAS
                    };
                    if (inst.Argument < intrinsics.Length)
                        return $"({intrinsics[inst.Argument]})";
                    break;

                // Jump instructions - calculate actual target instruction index
                case ByteCodeOp.POP_JUMP_IF_FALSE:
                case ByteCodeOp.POP_JUMP_IF_TRUE:
                case ByteCodeOp.POP_JUMP_IF_NOT_NONE:
                case ByteCodeOp.POP_JUMP_IF_NONE:
                case ByteCodeOp.JUMP_FORWARD:
                case ByteCodeOp.JUMP_BACKWARD:
                case ByteCodeOp.JUMP_BACKWARD_NO_INTERRUPT:
                    // Use VM's exact calculation: currentPosJump + 1 + relativeOffset
                    // Match VM's InstructionPointer calculation for consistency
                    var currentPosJump = instructionIndex;
                    var targetInstructionIndex = currentPosJump + 1 + inst.Argument;
                    var targetByteOffset = targetInstructionIndex * 2; // Each instruction is 2 bytes
                    return $"(to {targetByteOffset})";
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
            // CPython 3.12 기본 연산들
            { ByteCodeOp.POP_TOP, (1, 0) },
            { ByteCodeOp.COPY, (1, 2) },     // CPython 3.12: COPY N (스택에서 N번째 요소 복사)
            { ByteCodeOp.SWAP, (2, 2) },     // CPython 3.12: SWAP N (상위 N개 요소 교환)
            
            // CPython 3.12 통합 연산 (모든 binary ops는 BINARY_OP로 통합됨)
            { ByteCodeOp.BINARY_OP, (2, 1) },
            { ByteCodeOp.UNARY_NEGATIVE, (1, 1) },
            { ByteCodeOp.UNARY_NOT, (1, 1) },
            { ByteCodeOp.UNARY_INVERT, (1, 1) },
            
            // 비교 연산
            { ByteCodeOp.COMPARE_OP, (2, 1) },
            { ByteCodeOp.CONTAINS_OP, (2, 1) },
            
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
            
            // 인덱싱 - CPython 3.12: BINARY_SUBSCR
            { ByteCodeOp.BINARY_SUBSCR, (2, 1) },
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
            // CPython 3.12: 조건부 점프 (legacy opcodes removed)
            
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
                // CPython 3.12: CALL_FUNCTION_KW removed, use KW_NAMES + CALL
                ByteCodeOp.CALL_FUNCTION_EX => ((arg & 1) != 0 ? 3 : 2, 1), // func + args + (kwargs?) -> result (legacy)
                
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
            // CPython 3.12: JUMP_IF_*_OR_POP opcodes removed
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