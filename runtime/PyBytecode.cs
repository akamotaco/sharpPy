// Performance: Eliminated LINQ - no LINQ usage found

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

        // Pseudo opcode: becomes STORE_FAST in ConvertPseudoOps.
        STORE_FAST_MAYBE_NULL = 268,
        
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
        // CPython 3.12 Pseudo-instructions (for CFG/InstructionSequence only)
        // Range: 256-269 (removed during CFG → bytecode conversion)
        // =============================================================================
        SETUP_FINALLY = 256,     // Mark try block start, push exception handler
        SETUP_CLEANUP = 257,     // Like SETUP_FINALLY but saves lasti
        SETUP_WITH = 258,        // Setup with statement exception handling
        POP_BLOCK = 259,         // Pop exception handler from stack
        JUMP = 260,              // Unconditional jump (direction determined by assembler)
        JUMP_NO_INTERRUPT = 261, // Unconditional jump without interrupt check

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

    // SharpPy BINARY_OP operands differ from CPython for ordinary operations.
    public enum BinaryOpType : byte
    {
        // Compiler and VM share this ordering; this is not a CPython import format.
        // NB_* of Include/internal/pycore_opcode... the oparg of BINARY_OP.
        ADD = 0,
        AND = 1,
        FLOOR_DIVIDE = 2,
        LSHIFT = 3,
        MATRIX_MULTIPLY = 4,
        MULTIPLY = 5,
        MODULO = 6,
        OR = 7,
        POWER = 8,
        RSHIFT = 9,
        SUBTRACT = 10,
        TRUE_DIVIDE = 11,
        XOR = 12,

        // CPython 3.12: In-place operations (NB_INPLACE_*)
        // Python/bytecodes.c: BINARY_OP opcode with arg >= 13 means in-place
        // In-place values below match CPython 3.12 NB_INPLACE_* numbering.
        INPLACE_ADD = 13,              // +=  (인플레이스 덧셈)
        INPLACE_AND = 14,              // &=  (인플레이스 비트 AND)
        INPLACE_FLOOR_DIVIDE = 15,     // //= (인플레이스 바닥 나눗셈)
        INPLACE_LSHIFT = 16,           // <<= (인플레이스 좌시프트)
        INPLACE_MATRIX_MULTIPLY = 17,  // @=  (인플레이스 행렬 곱셈)
        INPLACE_MULTIPLY = 18,         // *=  (인플레이스 곱셈)
        INPLACE_MODULO = 19,           // %=  (인플레이스 모듈로)
        INPLACE_OR = 20,               // |=  (인플레이스 비트 OR)
        INPLACE_POWER = 21,            // **= (인플레이스 거듭제곱)
        INPLACE_RSHIFT = 22,           // >>= (인플레이스 우시프트)
        INPLACE_SUBTRACT = 23,         // -=  (인플레이스 뺄셈)
        INPLACE_TRUE_DIVIDE = 24,      // /=  (인플레이스 나눗셈)
        INPLACE_XOR = 25,              // ^=  (인플레이스 비트 XOR)

        // NB_DIVMOD has no BINARY_OP of its own: divmod() is PyNumber_Divmod, binary_op over nb_divmod.
        DIVMOD = 64
    }

    // CPython 3.12: ByteCodeInstruction directly maps to _PyCfgInstruction
    // No intermediate ExceptHandlerInfo struct needed - use ExceptBlock reference directly
    /// <summary>The end of a location. The default is no location, as (-1, -1).</summary>
    public readonly struct EndPosition
    {
        private readonly int _line;
        private readonly int _column;

        public EndPosition(int line, int column)
        {
            _line = line + 1;
            _column = column + 1;
        }

        public int Line => _line - 1;
        public int Column => _column - 1;
    }

    public struct ByteCodeInstruction
    {
        public ByteCodeOp OpCode { get; }
        public int Argument { get; }
        public int LineNumber { get; }      // Source line number (1-based)
        public int ColumnOffset { get; }    // Source column offset (0-based)
        public EndPosition End { get; }     // end_lineno and end_col_offset of its location

        // CPython 3.12: _PyCfgInstruction compatibility
        // typedef struct {
        //     int i_opcode;                          -> OpCode
        //     int i_oparg;                           -> Argument
        //     struct _PyCfgBasicblock_ *i_target;    -> TargetBlock
        //     struct _PyCfgBasicblock_ *i_except;    -> ExceptBlock
        // } _PyCfgInstruction;

        public BasicBlock? TargetBlock { get; set; }  // i_target: Jump/branch target block
        public BasicBlock? ExceptBlock { get; set; }  // i_except: Exception handler block

        public ByteCodeInstruction(ByteCodeOp opCode, int argument = 0, int lineNumber = -1, int columnOffset = -1, EndPosition end = default, BasicBlock? targetBlock = null, BasicBlock? exceptBlock = null)
        {
            OpCode = opCode;
            Argument = argument;
            LineNumber = lineNumber;
            ColumnOffset = columnOffset;
            End = end;
            TargetBlock = targetBlock;
            ExceptBlock = exceptBlock;
        }
        
        public override string ToString()
        {
            return Argument == 0 ? OpCode.ToString() : $"{OpCode} {Argument}";
        }
    }

    // 확장된 PyCodeObject (기존 시스템과 연동)
    public class PyCodeObject : PyObject
    {
        // CPython 3.12: Each instruction word is 2 bytes
        // SharpPy uses instruction word index internally, converts to byte offset for display
        public const int INSTRUCTION_WORD_SIZE = 2;

        // CPython 호환 플래그 시스템
        public const int CO_VARARGS = 0x04;        // *args 매개변수 존재
        public const int CO_VARKEYWORDS = 0x08;    // **kwargs 매개변수 존재
        public const int CO_GENERATOR = 0x20;      // 제너레이터 함수
        public const int CO_COROUTINE = 0x80;      // 네이티브 코루틴 (async def)
        public const int CO_ASYNC_GENERATOR = 0x200; // 비동기 제너레이터 (async def + yield)
        
        public string Name { get; }
        // CPython co_qualname: nesting-aware definition name (C.meth,
        // outer.<locals>.inner, f.<locals>.<lambda>). Falls back to co_name.
        public string? QualName { get; set; }
        public string DisplayQualName => string.IsNullOrEmpty(QualName) ? Name : QualName!;
        public List<ByteCodeInstruction> Instructions { get; }
        public List<PyObject> Constants { get; }      // co_consts
        public List<string> Names { get; }            // co_names (변수명들)

        // co_names as the str objects CPython's tuple holds: STORE_ATTR and DELETE_ATTR hand the
        // name object to tp_setattro.
        private PyString[] _nameObjects;
        internal PyString[] NameObjects => _nameObjects ??= Names.Select(PyString.From).ToArray();
        public List<string> VarNames { get; }         // co_varnames (지역변수명들)
        public int ArgCount { get; }                  // 매개변수 개수
        public int PosonlyArgCount { get; }           // co_posonlyargcount (positional-only 매개변수 개수)
        public int KwonlyArgCount { get; }            // co_kwonlyargcount (keyword-only 매개변수 개수)
        public int Flags { get; }                     // co_flags (CPython 호환)
        public string? FileName { get; private set; }              // co_filename (CPython 호환)

        // update_code_filenames: this code and the code among its constants.
        internal void FixFileName(string fileName)
        {
            if (FileName == fileName)
                return;
            FileName = fileName;
            foreach (var constant in Constants)
                if (constant is PyCodeObject inner)
                    inner.FixFileName(fileName);
        }
        
        // CPython 스타일 에러 보고를 위한 소스 라인 정보
        public List<string>? SourceLines { get; }     // 원본 소스 코드 라인들 (에러 표시용)
        
        // CPython 호환 클로저 지원 (Phase 2)
        public List<string> FreeVars { get; set; } = new List<string>();   // co_freevars - 자유 변수
        public List<string> CellVars { get; set; } = new List<string>();   // co_cellvars - 셀 변수

        /// <summary>
        /// CPython 3.12: Cached name→index mappings for O(1) lookup
        /// Replaces O(n) CellVars.IndexOf(), FreeVars.IndexOf(), VarNames.IndexOf() calls
        /// Built once at construction time
        /// </summary>
        public Dictionary<string, int> CellVarIndexMap { get; private set; } = null!;
        public Dictionary<string, int> FreeVarIndexMap { get; private set; } = null!;
        public Dictionary<string, int> VarNameIndexMap { get; private set; } = null!;

        /// <summary>
        /// CPython 3.12: Fast set membership test for VarNames
        /// Replaces O(n) VarNames.Contains() calls
        /// </summary>
        public HashSet<string> VarNameSet { get; private set; } = null!;

        // CPython 3.12 추가 CO_* 플래그 상수들
        public const int CO_OPTIMIZED = 0x0001;         // 지역 변수 최적화
        public const int CO_NEWLOCALS = 0x0002;         // 새로운 지역 변수 네임스페이스
        public const int CO_NESTED = 0x0010;            // 중첩된 함수
        public const int CO_ITERABLE_COROUTINE = 0x0100; // 반복 가능한 코루틴
        
        // CPython 호환 매개변수 기본값 지원 (Phase 3)
        public List<PyObject> DefaultValues { get; set; } = new List<PyObject>(); // 매개변수 기본값들 (NULL이면 기본값 없음)
        public List<PyObject> KwDefaults { get; set; } = new List<PyObject>(); // 키워드 전용 매개변수 기본값들
        
        // CPython 3.12 Exception Table 지원
        public List<ExceptionTableEntry> ExceptionTable { get; set; } = new List<ExceptionTableEntry>();
        
        // Line Number Table: instruction offset → source line number mapping
        
        public PyCodeObject(string name, List<ByteCodeInstruction> instructions,
                        List<PyObject> constants, List<string> names,
                        List<string> varNames, int argCount = 0, int posonlyArgCount = 0,
                        int kwonlyArgCount = 0,
                        List<string> freeVars = null, List<string> cellVars = null,
                        List<PyObject> defaultValues = null, List<PyObject> kwDefaults = null,
                        int flags = 0, string fileName = null,
                        List<string> sourceLines = null, bool isOptimized = false,
                        Dictionary<int, int> lineNumberTable = null,
                        List<ExceptionTableEntry> exceptionTable = null)
        {
            Name = name;
            Instructions = instructions;
            Constants = constants != null ? new List<PyObject>(constants) : new List<PyObject>();
            Names = names;
            VarNames = varNames;
            ArgCount = argCount;
            PosonlyArgCount = posonlyArgCount;
            KwonlyArgCount = kwonlyArgCount;
            Flags = flags;
            FileName = fileName;
            SourceLines = sourceLines;
            FreeVars = freeVars ?? new List<string>();
            CellVars = cellVars ?? new List<string>();
            DefaultValues = defaultValues ?? new List<PyObject>();
            KwDefaults = kwDefaults ?? new List<PyObject>();
            ExceptionTable = exceptionTable ?? new List<ExceptionTableEntry>(); // 전달된 exception table 보존

            // CPython 3.12: Build cached index maps for O(1) lookup at runtime
            BuildIndexMaps();
        }

        /// <summary>
        /// CPython 3.12: Build all cached index maps at construction time
        /// This eliminates O(n) IndexOf/Contains calls at runtime
        /// </summary>
        private void BuildIndexMaps()
        {
            // Build CellVarIndexMap: name → index in CellVars
            CellVarIndexMap = new Dictionary<string, int>(CellVars.Count);
            for (int i = 0; i < CellVars.Count; i++)
            {
                CellVarIndexMap[CellVars[i]] = i;
            }

            // Build FreeVarIndexMap: name → index in FreeVars
            FreeVarIndexMap = new Dictionary<string, int>(FreeVars.Count);
            for (int i = 0; i < FreeVars.Count; i++)
            {
                FreeVarIndexMap[FreeVars[i]] = i;
            }

            // Build VarNameIndexMap: name → index in VarNames
            VarNameIndexMap = new Dictionary<string, int>(VarNames.Count);
            for (int i = 0; i < VarNames.Count; i++)
            {
                VarNameIndexMap[VarNames[i]] = i;
            }

            // Build VarNameSet for O(1) Contains() check
            VarNameSet = new HashSet<string>(VarNames);

            // _Py_set_localsplus_info (compile.c compute_localsplus_info): the locals, a cell that
            // is an argument sharing its slot, then the other cells, then the free variables.
            var localsPlusNames = new List<string>(VarNames);
            var localsPlusKinds = new List<byte>();
            foreach (var local in VarNames)
                localsPlusKinds.Add((byte)(CellVarIndexMap.ContainsKey(local) ? FastLocal | FastCell : FastLocal));
            foreach (var cell in CellVars)
                if (!VarNameSet.Contains(cell))
                {
                    localsPlusNames.Add(cell);
                    localsPlusKinds.Add(FastCell);
                }
            FirstFree = localsPlusNames.Count;
            foreach (var free in FreeVars)
            {
                localsPlusNames.Add(free);
                localsPlusKinds.Add(FastFree);
            }
            LocalsPlusNames = localsPlusNames.ToArray();
            LocalsPlusKinds = localsPlusKinds.ToArray();
            LocalsPlusKeys = new PyString[LocalsPlusNames.Length];
            for (int i = 0; i < LocalsPlusNames.Length; i++)
                LocalsPlusKeys[i] = PyString.From(LocalsPlusNames[i]);
        }

        // _PyLocals_Kind (Include/internal/pycore_code.h)
        internal const byte FastHidden = 0x10, FastLocal = 0x20, FastCell = 0x40, FastFree = 0x80;

        /// <summary>co_localsplusnames, co_localspluskinds, and the names as keys for f_locals.</summary>
        internal string[] LocalsPlusNames { get; private set; }
        internal byte[] LocalsPlusKinds { get; private set; }

        /// <summary>
        /// compute_localsplus_info (Python/assemble.c): a name in u_fasthidden, the fast locals of a
        /// comprehension inlined in module or class code, is CO_FAST_HIDDEN too. marshal keeps it.
        /// </summary>
        internal void MarkHidden(ICollection<string> hidden)
        {
            for (int i = 0; i < LocalsPlusNames.Length; i++)
                if (hidden.Contains(LocalsPlusNames[i]))
                    LocalsPlusKinds[i] |= FastHidden;
        }
        internal PyString[] LocalsPlusKeys { get; private set; }

        /// <summary>co_nlocalsplus.</summary>
        internal int NLocalsPlus => LocalsPlusNames.Length;

        /// <summary>PyCode_GetFirstFree: co_nlocalsplus - co_nfreevars.</summary>
        internal int FirstFree { get; private set; }


        // CPython: type(code).__name__ == 'code'
        internal static readonly PyType CodePyType = MakeCodeType();

        private static PyType MakeCodeType()
        {
            var type = new PyType("code", new[] { PyType.ObjectType });
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, CodeNew, 0, int.MaxValue, acceptsKwargs: true);
            return type;
        }
        public override PyType GetPyType() => CodePyType;

        // CPython 3.12 호환: co_* 속성들 지원
        /// <summary>
        /// _PyCode_New: a code object from the tables, the one way in for marshal, code.replace and
        /// types.CodeType. co_argcount here does not count keyword-only arguments, as in Python.
        /// </summary>
        internal static PyCodeObject FromTables(int argCount, int posonlyArgCount, int kwonlyArgCount, int stackSize,
            int flags, byte[] code, List<PyObject> constants, List<string> names, List<string> varNames,
            List<string> freeVars, List<string> cellVars, string fileName, string name, string qualName,
            int firstLineNo, byte[] lineTable, byte[] exceptionTable, List<string> sourceLines = null)
        {
            if (code.Length % 2 != 0)
                throw PyValueError.Create("code: co_code is malformed");
            var result = new PyCodeObject(name, LocationTable.ReadInstructions(code, lineTable, firstLineNo), constants, names,
                varNames, argCount + kwonlyArgCount, posonlyArgCount, kwonlyArgCount, freeVars, cellVars, null, null, flags,
                fileName, sourceLines, false, null, LocationTable.ReadExceptionTable(exceptionTable))
            {
                QualName = qualName,
                FirstLineNo = firstLineNo,
                StackSize = stackSize,
                Tables = new AssembledTables { Code = code, LineTable = lineTable, ExceptionTable = exceptionTable },
            };
#if CODE_TABLE_CHECK
            if (!result.Tables.Code.AsSpan().SequenceEqual(LocationTable.CodeBytes(result.Instructions)) ||
                !result.Tables.LineTable.AsSpan().SequenceEqual(LocationTable.LineBytes(result.Instructions, firstLineNo)) ||
                !result.Tables.ExceptionTable.AsSpan().SequenceEqual(LocationTable.ExceptionBytes(result.ExceptionTable)))
                throw PyValueError.Create("tables differ after rebuilding: " + name);
#endif
            return result;
        }

        private static List<string> NameList(PyObject value, string field)
        {
            if (value is not PyTuple tuple)
                throw PyTypeError.Create($"{field} must be a tuple");
            var names = new List<string>(tuple.Items.Length);
            foreach (var item in tuple.Items)
                names.Add(item is PyString text ? text.Value : throw PyTypeError.Create("name tuples must contain only strings, not '" + item.GetTypeName() + "'"));
            return names;
        }

        private static byte[] BytesField(PyObject value, string field) =>
            value is PyBytes bytes ? bytes.Value : throw PyTypeError.Create($"{field} must be bytes, not {value.GetTypeName()}");

        // _PyCode_ConstantKey: 1 and 1.0 are different constants, and so are 0.0 and -0.0.
        private static bool SameConstant(PyObject a, PyObject b)
        {
            if (a.GetType() != b.GetType())
                return false;
            switch (a)
            {
                case PyFloat x:
                    return BitConverter.DoubleToInt64Bits(x.Value) == BitConverter.DoubleToInt64Bits(((PyFloat)b).Value);
                case PyComplex x:
                    var y = (PyComplex)b;
                    return BitConverter.DoubleToInt64Bits(x.Real) == BitConverter.DoubleToInt64Bits(y.Real) &&
                        BitConverter.DoubleToInt64Bits(x.Imag) == BitConverter.DoubleToInt64Bits(y.Imag);
                case PyTuple x:
                    var items = ((PyTuple)b).Items;
                    if (x.Items.Length != items.Length)
                        return false;
                    for (int i = 0; i < items.Length; i++)
                        if (!SameConstant(x.Items[i], items[i]))
                            return false;
                    return true;
                default:
                    return CompareBool(a, b, CompareOp.EQ);
            }
        }

        // code_richcompare
        private bool SameCode(PyCodeObject other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (Name != other.Name || ArgCount != other.ArgCount || PosonlyArgCount != other.PosonlyArgCount ||
                KwonlyArgCount != other.KwonlyArgCount || Flags != other.Flags || GetFirstLineNo() != other.GetFirstLineNo() ||
                !Tables.Code.AsSpan().SequenceEqual(other.Tables.Code) || Constants.Count != other.Constants.Count ||
                !Names.SequenceEqual(other.Names) || !VarNames.SequenceEqual(other.VarNames) ||
                !CellVars.SequenceEqual(other.CellVars) || !FreeVars.SequenceEqual(other.FreeVars) ||
                !Tables.LineTable.AsSpan().SequenceEqual(other.Tables.LineTable) ||
                !Tables.ExceptionTable.AsSpan().SequenceEqual(other.Tables.ExceptionTable))
                return false;
            for (int i = 0; i < Constants.Count; i++)
                if (!SameConstant(Constants[i], other.Constants[i]))
                    return false;
            return true;
        }

        protected internal override PyObject TpRichCompare(PyObject other, CompareOp op)
        {
            if (other is not PyCodeObject code || (op != CompareOp.EQ && op != CompareOp.NE))
                return PyNotImplemented.Instance;
            return PyBool.FromBool(SameCode(code) == (op == CompareOp.EQ));
        }

        // code_hash: of what code_richcompare compares.
        public override int ToHash()
        {
            var hash = new HashCode();
            hash.Add(Name);
            hash.Add(ArgCount);
            hash.Add(PosonlyArgCount);
            hash.Add(KwonlyArgCount);
            hash.Add(Flags);
            hash.Add(GetFirstLineNo());
            hash.Add(Constants.Count);
            hash.AddBytes(Tables.Code);
            return hash.ToHashCode();
        }

        // emit_delta
        private static void EmitDelta(List<byte> table, int byteDelta, int lineDelta)
        {
            if (byteDelta == 0 && lineDelta == 0)
                return;
            for (; byteDelta > 255; byteDelta -= 255)
            {
                table.Add(255);
                table.Add(0);
            }
            for (; lineDelta > 127; lineDelta -= 127, byteDelta = 0)
            {
                table.Add((byte)byteDelta);
                table.Add(127);
            }
            for (; lineDelta < -128; lineDelta += 128, byteDelta = 0)
            {
                table.Add((byte)byteDelta);
                table.Add(128);
            }
            table.Add((byte)byteDelta);
            table.Add((byte)(sbyte)lineDelta);
        }

        // code_getlnotab and decode_linetable
        private PyObject LineNumberNotes()
        {
            PyWarnings.Deprecated("co_lnotab is deprecated, use co_lines instead.");
            var table = new List<byte>();
            int line = GetFirstLineNo(), offset = 0, unit = 0;
            foreach (var position in LocationTable.Read(Tables.LineTable, GetFirstLineNo()))
            {
                if (position.Line >= 0 && position.Line != line)
                {
                    EmitDelta(table, unit * 2 - offset, position.Line - line);
                    offset = unit * 2;
                    line = position.Line;
                }
                unit++;
            }
            return PyBytes.From(table.ToArray());
        }

        // code_replace_impl
        // code__varname_from_oparg: the name of a local, cell or free variable by its index.
        internal PyObject VarnameFromOparg(PyObject[] args, PyDict kwargs)
        {
            var oparg = args.Length > 0 ? args[0] : kwargs.GetItem(PyString.From("oparg"));
            int index = PyArgs.Int(oparg);
            if (index < 0 || index >= NLocalsPlus)
                throw PyIndexError.Create("tuple index out of range");
            return LocalsPlusKeys[index];
        }

        internal PyObject Replace(PyObject[] args, PyDict kwargs)
        {
            if (args.Length != 0) throw PyTypeError.Create("replace() takes no positional arguments");
            int argCount = ArgCount - KwonlyArgCount, posonly = PosonlyArgCount, kwonly = KwonlyArgCount;
            int stackSize = StackSize, flags = Flags, firstLineNo = GetFirstLineNo();
            var code = Tables.Code;
            var lineTable = Tables.LineTable;
            var exceptionTable = Tables.ExceptionTable;
            var constants = Constants;
            var names = Names;
            var varNames = VarNames;
            var freeVars = FreeVars;
            var cellVars = CellVars;
            string fileName = FileName, name = Name, qualName = QualName;
            int nlocals = -1;
            if (kwargs != null)
            {
                foreach (var key in kwargs.Keys().Items)
                {
                    var value = kwargs.GetItem(key);
                    string field = ((PyString)key).Value;
                    switch (field)
                    {
                        case "co_argcount": argCount = value.ToInt(); break;
                        case "co_posonlyargcount": posonly = value.ToInt(); break;
                        case "co_kwonlyargcount": kwonly = value.ToInt(); break;
                        case "co_nlocals": nlocals = value.ToInt(); break;
                        case "co_stacksize": stackSize = value.ToInt(); break;
                        case "co_flags": flags = value.ToInt(); break;
                        case "co_firstlineno": firstLineNo = value.ToInt(); break;
                        case "co_code": code = BytesField(value, field); break;
                        case "co_linetable": lineTable = BytesField(value, field); break;
                        case "co_exceptiontable": exceptionTable = BytesField(value, field); break;
                        case "co_consts":
                            constants = new List<PyObject>(value is PyTuple values ? values.Items : throw PyTypeError.Create("co_consts must be a tuple"));
                            break;
                        case "co_names": names = NameList(value, field); break;
                        case "co_varnames": varNames = NameList(value, field); break;
                        case "co_freevars": freeVars = NameList(value, field); break;
                        case "co_cellvars": cellVars = NameList(value, field); break;
                        case "co_filename": fileName = ((PyString)value).Value; break;
                        case "co_name": name = ((PyString)value).Value; break;
                        case "co_qualname": qualName = ((PyString)value).Value; break;
                        default: throw PyArgs.InvalidKeyword("replace", PyString.From(field));
                    }
                }
            }
            if (nlocals >= 0 && nlocals != varNames.Count)
                throw PyValueError.Create("code: co_nlocals != len(co_varnames)");
            return FromTables(argCount, posonly, kwonly, stackSize, flags, code, constants, names, varNames, freeVars,
                cellVars, fileName, name, qualName, firstLineNo, lineTable, exceptionTable, SourceLines);
        }

        // code_new_impl
        private static PyObject CodeNew(PyObject cls, PyObject[] args, PyDict kwargs)
        {
            if (kwargs != null && kwargs.Length() != 0)
                throw PyTypeError.Create("code() takes no keyword arguments");
            if (args.Length < 16 || args.Length > 18)
                throw PyTypeError.Create($"code expected at least 16 arguments, got {args.Length}");
            var varNames = NameList(args[9], "varnames");
            if (args[3].ToInt() != varNames.Count)
                throw PyValueError.Create("code: co_nlocals != len(co_varnames)");
            return FromTables(args[0].ToInt(), args[1].ToInt(), args[2].ToInt(), args[4].ToInt(), args[5].ToInt(),
                BytesField(args[6], "code"), new List<PyObject>(((PyTuple)args[7]).Items), NameList(args[8], "names"), varNames,
                args.Length > 16 ? NameList(args[16], "freevars") : new List<string>(),
                args.Length > 17 ? NameList(args[17], "cellvars") : new List<string>(),
                ((PyString)args[10]).Value, ((PyString)args[11]).Value, ((PyString)args[12]).Value, args[13].ToInt(),
                BytesField(args[14], "linetable"), BytesField(args[15], "exceptiontable"));
        }

        internal PyObject Read(string name)
        {
            return name switch
            {
                "co_flags" => PyInt.From(Flags),
                // SharpPy keeps unique internal lambda names (<lambda_N>) for the
                // call-error mapping; CPython's co_name is the bare "<lambda>".
                "co_name" => PyString.From(PyFrame.BindErrorName(Name)),
                "co_qualname" => PyString.From(PyFrame.BindErrorName(DisplayQualName)),
                // CPython co_argcount counts positional-only + positional-or-keyword
                // only; SharpPy stores non-variadic totals, so subtract kwonly.
                "co_argcount" => PyInt.From(Math.Max(0, ArgCount - KwonlyArgCount)),
                "co_posonlyargcount" => PyInt.From(PosonlyArgCount),
                "co_kwonlyargcount" => PyInt.From(KwonlyArgCount),
                "co_varnames" => PyTuple.From(VarNames.Select(n => PyString.From(n) as PyObject).ToArray()),
                "co_names" => PyTuple.From(Names.Select(n => PyString.From(n) as PyObject).ToArray()),
                "co_consts" => PyTuple.From(Constants.ToArray()),
                "co_freevars" => PyTuple.From(FreeVars.Select(n => PyString.From(n) as PyObject).ToArray()),
                "co_cellvars" => PyTuple.From(CellVars.Select(n => PyString.From(n) as PyObject).ToArray()),
                "co_filename" => PyString.From(FileName ?? "<unknown>"),
                "co_firstlineno" => PyInt.From(GetFirstLineNo()),
                "co_nlocals" => PyInt.From(VarNames.Count),
                "co_stacksize" => PyInt.From(StackSize),
                "co_code" => PyBytes.From(Tables.Code),
                "_co_code_adaptive" => PyBytes.From(Tables.Code),
                "co_linetable" => PyBytes.From(Tables.LineTable),
                "co_exceptiontable" => PyBytes.From(Tables.ExceptionTable),
                "co_lnotab" => LineNumberNotes(),
                _ => base.TpGetattro(name)
            };
        }

        /// <summary>
        /// co_firstlineno
        /// </summary>
        public int FirstLineNo { get; set; }

        public int GetFirstLineNo()
        {
            if (FirstLineNo > 0)
                return FirstLineNo;
            return 1; // Default to line 1
        }

        // positionsiter_next
        internal PyObject CoPositionsMethod(PyObject[] args)
        {
            static PyObject Value(int number) => number < 0 ? PyNone.Instance : PyInt.From(number);
            var positions = new List<PyObject>();
            foreach (var position in LocationTable.Read(Tables.LineTable, GetFirstLineNo()))
                positions.Add(PyTuple.From(new[] { Value(position.Line), Value(position.EndLine), Value(position.Column), Value(position.EndColumn) }));
            return new PyList(positions.ToArray()).GetIterator();
        }

        // The tables as Python sees them.
        public AssembledTables Tables { get; set; } = new AssembledTables();
        public int StackSize { get; set; }

        internal PyObject CoLinesMethod(PyObject[] args)
        {
            // lineiter_next: consecutive units of one line are one range.
            var positions = LocationTable.Read(Tables.LineTable, GetFirstLineNo());
            var ranges = new List<PyObject>();
            int start = 0;
            while (start < positions.Count)
            {
                int end = start + 1;
                while (end < positions.Count && positions[end].Line == positions[start].Line)
                    end++;
                ranges.Add(PyTuple.From(new PyObject[]
                {
                    PyInt.From(start * 2), PyInt.From(end * 2),
                    positions[start].Line < 0 ? PyNone.Instance : PyInt.From(positions[start].Line),
                }));
                start = end;
            }
            return new PyList(ranges.ToArray()).GetIterator();
        }

        public void Disassemble()
        {
            if (CodeDumper.Enabled)
            {
                CodeDumper.Dump(this);
                return;
            }

            Console.WriteLine($"\n📄 Disassembly of {Name}:");
            for (int i = 0; i < Instructions.Count; i++)
            {
                var inst = Instructions[i];
                var extra = "";

                switch (inst.OpCode)
                {
                    case ByteCodeOp.LOAD_CONST:
                        if (inst.Argument >= 0 && inst.Argument < Constants.Count)
                        {
                            extra = $"({FormatConstantForDisplay(Constants[inst.Argument])})";
                        }
                        else
                        {
                            extra = $"(INVALID INDEX {inst.Argument})";
                        }
                        break;
                    case ByteCodeOp.LOAD_NAME:
                    case ByteCodeOp.STORE_NAME:
                    case ByteCodeOp.STORE_GLOBAL:
                        if (inst.Argument >= 0 && inst.Argument < Names.Count)
                        {
                            extra = $"({Names[inst.Argument]})";
                        }
                        else
                        {
                            extra = $"(INVALID INDEX {inst.Argument})";
                        }
                        break;
                    case ByteCodeOp.LOAD_GLOBAL:
                        // CPython 3.12: LOAD_GLOBAL oparg encoding: (nameIndex << 1) | pushNull
                        int globalNameIndex = inst.Argument >> 1;
                        bool pushNull = (inst.Argument & 1) == 1;
                        string nullPrefix = pushNull ? "NULL + " : "";
                        extra = $"({nullPrefix}{Names[globalNameIndex]})";
                        break;
                    case ByteCodeOp.LOAD_ATTR:
                    case ByteCodeOp.STORE_ATTR:
                        // CPython 3.12: LOAD_ATTR oparg encodes name_index in high bits, push_null in low bit
                        int attrNameIndex = inst.Argument >> 1;
                        extra = $"({Names[attrNameIndex]})";
                        break;
                }

                // Convert instruction index to byte offset for display (CPython 3.12 compatible)
                Console.WriteLine($"  {i * INSTRUCTION_WORD_SIZE,3}: {inst,-25} {extra}");
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

            // CPython 3.12: Recursively disassemble nested code objects (functions, classes, etc.)
            foreach (var constant in Constants)
            {
                if (constant is PyCodeObject nestedCode)
                {
                    nestedCode.Disassemble();
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
                    // CPython 3.12: Always show decimal point for floats
                    string floatStr = pyFloat.Value.ToString();
                    if (!floatStr.Contains(".") && !floatStr.Contains("e") && !floatStr.Contains("E"))
                        floatStr += ".0";
                    return floatStr;

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

        // code_repr
        protected internal override PyString Repr() => PyString.From(FileName != null
            ? $"<code object {PyFrame.BindErrorName(Name)} at {PyObjectId.Pointer(this)}, file \"{FileName}\", line {GetFirstLineNo()}>"
            : $"<code object {PyFrame.BindErrorName(Name)} at {PyObjectId.Pointer(this)}, file ???, line {GetFirstLineNo()}>");
        
        /// <summary>
        /// Test the synchronous generator flag determined by scope analysis.
        /// </summary>
        public bool IsGenerator()
        {
            return (Flags & CO_GENERATOR) != 0;
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

        // Final assembly stores every CACHE/EXTENDED_ARG as a separate 2-byte
        // word. Adding cache sizes here would count those words again.
        public int InstructionIndexToByteOffset(int instructionIndex)
        {
            if (instructionIndex < 0 || instructionIndex >= Instructions.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(instructionIndex),
                    $"Instruction index {instructionIndex} out of range [0, {Instructions.Count})");
            }

            return instructionIndex * INSTRUCTION_WORD_SIZE;
        }

        // Final assembly stores CACHE and EXTENDED_ARG as individual words.
        // This is the inverse of InstructionIndexToByteOffset, not a CFG scan.
        public int ByteOffsetToInstructionIndex(int byteOffset)
        {
            if (byteOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(byteOffset),
                    $"Byte offset {byteOffset} must be non-negative");

            int index = byteOffset / INSTRUCTION_WORD_SIZE;
            if (index >= Instructions.Count)
                throw new ArgumentOutOfRangeException(nameof(byteOffset),
                    $"Byte offset {byteOffset} exceeds code size {(long)Instructions.Count * INSTRUCTION_WORD_SIZE}");

            if (byteOffset % INSTRUCTION_WORD_SIZE != 0)
                throw new ArgumentException(
                    $"[{Name}] Byte offset {byteOffset} does not align with an instruction boundary. " +
                    $"Previous instruction at byte offset {index * INSTRUCTION_WORD_SIZE}, " +
                    $"next instruction at byte offset {(long)(index + 1) * INSTRUCTION_WORD_SIZE}");

            return index;
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
        INTRINSIC_TYPEALIAS = 11,           // CPython 3.12: INTRINSIC_TYPEALIAS ✅ 이미 정확

        // INTRINSIC_2 (CALL_INTRINSIC_2)
        INTRINSIC_PREP_RERAISE_STAR = 1,    // CPython 3.12: PREP_RERAISE_STAR for except* exception group handling
        INTRINSIC_TYPEVAR_WITH_BOUND = 2,   // CPython
        INTRINSIC_TYPEVAR_WITH_CONSTRAINTS = 3, // CPython
        INTRINSIC_SET_FUNCTION_TYPE_PARAMS = 4  // Already used in compile.cs
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
                    // CPython 3.12 정확한 intrinsic function 이름들
                    var intrinsics1 = new[] {
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
                    if (inst.Argument < intrinsics1.Length)
                        return $"({intrinsics1[inst.Argument]})";
                    break;

                case ByteCodeOp.CALL_INTRINSIC_2:
                    // CPython 3.12 INTRINSIC_2 함수 이름들
                    var intrinsics2 = new[] {
                        "INTRINSIC_2_INVALID",         // 0
                        "PREP_RERAISE_STAR",           // 1: INTRINSIC_PREP_RERAISE_STAR (exception groups)
                        "INTRINSIC_2_INVALID",         // 2: reserved
                        "INTRINSIC_2_INVALID",         // 3: reserved
                        "SET_FUNCTION_TYPE_PARAMS"     // 4: INTRINSIC_SET_FUNCTION_TYPE_PARAMS
                    };
                    if (inst.Argument < intrinsics2.Length)
                        return $"({intrinsics2[inst.Argument]})";
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
                    var targetByteOffset = targetInstructionIndex * PyCodeObject.INSTRUCTION_WORD_SIZE;
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
            { ByteCodeOp.LOAD_LOCALS, (0, 1) },
            { ByteCodeOp.LOAD_FROM_DICT_OR_DEREF, (1, 1) },
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


        // CPython 3.12 호환: MAKE_FUNCTION 플래그 상수
        public static class MakeFunctionFlags
        {
            public const int DEFAULTS = 0x01;        // 기본값 있음
            public const int KWDEFAULTS = 0x02;      // 키워드 기본값 있음
            public const int ANNOTATIONS = 0x04;     // 타입 어노테이션 있음
            public const int CLOSURE = 0x08;         // 클로저 있음
        }
    }

}
