using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// SharpPy 바이트코드 최적화기
    /// CPython의 peephole 최적화 패턴을 참고한 C# 구현
    /// </summary>
    public class ByteCodeOptimizer
    {
        private List<ByteCodeInstruction> _instructions;
        private List<PyObject> _constants;
        private List<string> _names;
        private bool _optimizationEnabled;
        private Dictionary<int, int> _instructionMapping; // 원본 인덱스 → 최적화된 인덱스 매핑

        public ByteCodeOptimizer(bool enableOptimization = true)
        {
            _optimizationEnabled = enableOptimization;
        }

        /// <summary>
        /// Create a new instruction while preserving ExceptHandler info from original
        /// CPython 3.12: i_except_handler_info must be preserved during optimization
        /// </summary>
        private ByteCodeInstruction CreateInstruction(ByteCodeOp opCode, int argument, ByteCodeInstruction original)
        {
            return new ByteCodeInstruction(
                opCode,
                argument,
                original.LineNumber,
                original.ColumnOffset,
                original.FileName,
                original.ExceptHandler  // CRITICAL: Preserve exception handler info
            );
        }

        /// <summary>
        /// CPython 3.12: Build exception table from instruction's ExceptHandler info
        /// Exactly like CPython's assemble_exception_table in Python/assemble.c
        /// </summary>
        private void BuildExceptionTableFromInstructions(PyCodeObject code)
        {
            Console.WriteLine($"🔧 BuildExceptionTableFromInstructions (CPython 3.12 method)");
            Console.WriteLine($"   Instructions: {code.Instructions.Count}");

            // Clear existing table - we rebuild completely from instruction metadata
            code.ExceptionTable.Clear();

            ExceptHandlerInfo? currentHandler = null;
            int startOffset = -1;

            for (int i = 0; i < code.Instructions.Count; i++)
            {
                var instr = code.Instructions[i];
                var instrHandler = instr.ExceptHandler;

                // TEMP DEBUG: Log every instruction's ExceptHandler
                if (i < 30 || instrHandler.HandlerOffset != -1)
                {
                    Console.WriteLine($"[{i}] {instr.OpCode,-20} HandlerOffset={instrHandler.HandlerOffset}, Depth={instrHandler.StackDepth}, Lasti={instrHandler.PreserveLasti}");
                }

                // Check if handler info changed
                bool handlerChanged = false;

                if (currentHandler == null && instrHandler.HandlerOffset != -1)
                {
                    // Started new handler region
                    handlerChanged = true;
                    currentHandler = instrHandler;
                    startOffset = i;
#if DEBUG_LOG
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        Console.WriteLine($"   [{i}] Start handler: offset={instrHandler.HandlerOffset}, depth={instrHandler.StackDepth}, lasti={instrHandler.PreserveLasti}");
                    }
#endif
                }
                else if (currentHandler != null && instrHandler.HandlerOffset == -1)
                {
                    // Exited handler region
                    handlerChanged = true;
#if DEBUG_LOG
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        Console.WriteLine($"   [{i}] Exit handler");
                    }
#endif
                }
                else if (currentHandler != null && !currentHandler.Value.Equals(instrHandler))
                {
                    // Handler changed (different handler)
                    handlerChanged = true;
#if DEBUG_LOG
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        Console.WriteLine($"   [{i}] Handler changed");
                    }
#endif
                }

                if (handlerChanged && currentHandler != null)
                {
                    // Emit exception table entry for previous handler
                    // CPython 3.12: handler is the handler offset (instruction offset, not byte offset)
                    var entry = new ExceptionTableEntry(
                        start: startOffset,
                        end: i,
                        handler: currentHandler.Value.HandlerOffset,
                        depth: currentHandler.Value.StackDepth,
                        lasti: currentHandler.Value.PreserveLasti
                    );
                    code.ExceptionTable.Add(entry);

#if DEBUG_LOG
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
                        Console.WriteLine($"   Entry: [{startOffset}:{i}] -> target={currentHandler.Value.HandlerOffset} (depth={currentHandler.Value.StackDepth}, lasti={currentHandler.Value.PreserveLasti})");
                    }
#endif

                    // Update current handler for next region
                    if (instrHandler.HandlerOffset != -1)
                    {
                        currentHandler = instrHandler;
                        startOffset = i;
                    }
                    else
                    {
                        currentHandler = null;
                        startOffset = -1;
                    }
                }
            }

            // Handle final handler region if still open
            if (currentHandler != null)
            {
                var entry = new ExceptionTableEntry(
                    start: startOffset,
                    end: code.Instructions.Count,
                    handler: currentHandler.Value.HandlerOffset,
                    depth: currentHandler.Value.StackDepth,
                    lasti: currentHandler.Value.PreserveLasti
                );
                code.ExceptionTable.Add(entry);

#if DEBUG_LOG
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
                    Console.WriteLine($"   Final Entry: [{startOffset}:{code.Instructions.Count}] -> target={currentHandler.Value.HandlerOffset} (depth={currentHandler.Value.StackDepth}, lasti={currentHandler.Value.PreserveLasti})");
                }
#endif
            }

            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
#if DEBUG_LOG
            Console.WriteLine($"🔧 BuildExceptionTableFromInstructions completed: {code.ExceptionTable.Count} entries");
#endif
            }
        }

        /// <summary>
        /// 바이트코드 최적화 메인 함수 (CPython 3.12 CFG-based optimization)
        /// NEW PIPELINE: InstructionSequence available from compiler
        /// </summary>
        public PyCodeObject OptimizeCode(PyCodeObject originalCode)
        {
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔧 ByteCodeOptimizer.OptimizeCode 호출 (CFG-based): _optimizationEnabled={_optimizationEnabled}");
#endif

            if (!_optimizationEnabled)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"🚫 최적화 비활성화됨 - 원본 코드 반환 (명령어 수: {originalCode.Instructions.Count})");
#endif
                return originalCode;
            }

            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine("\n🔧 CPython 3.12 CFG 기반 바이트코드 최적화 시작");
#endif
            }

            int originalCount = originalCode.Instructions.Count;

            // Use LEGACY pipeline for now
            // TODO: Integrate NEW pipeline when compiler provides InstructionSequence
            // NEW: InstructionSequence → CFG (CPython 3.12 style)
            // LEGACY: ByteCodeInstruction[] + ExceptionTable → CFG
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔧 Using LEGACY pipeline: ByteCodeInstruction[] + ExceptionTable → CFG");
            Console.WriteLine($"   Instructions: {originalCode.Instructions.Count}, ExceptionTable: {originalCode.ExceptionTable.Count}");
#endif
            var cfg = ControlFlowGraph.FromInstructionsAndExceptionTable(
                originalCode.Instructions,
                originalCode.ExceptionTable
            );

            // Phase 2: Optimize CFG
            // CPython의 flowgraph.c:optimize_cfg() 개념
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔧 Phase 2: Optimizing CFG ({cfg.AllBlocks.Count} blocks)");
#endif
            _constants = new List<PyObject>(originalCode.Constants);
            var optimizer = new CFGOptimizer(cfg, _constants);
            optimizer.Optimize();

            // Phase 3: Assemble CFG to bytecode
            // CPython의 assemble.c:assemble() 개념 (NEW: Use Assembler)
#if DEBUG_COMPILER_LOG
            Console.WriteLine($"🔧 Phase 3: Assembling CFG to bytecode");
#endif
            var assembled = PyAssemble.Assemble(cfg, originalCode.FileName);
            var optimizedInstructions = assembled.Instructions;
            var optimizedExceptionTable = assembled.ExceptionTable;

            int optimizedCount = optimizedInstructions.Count;
            int saved = originalCount - optimizedCount;

            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
#if DEBUG_COMPILER_LOG
                Console.WriteLine($"✅ CFG 최적화 완료: {originalCount} → {optimizedCount} ({saved} 명령어 절약, {(float)saved / originalCount * 100:F1}% 개선)");
                Console.WriteLine($"   Exception Table: {optimizedExceptionTable.Count} entries");
#endif
            }

            // Phase 5: Create optimized code object
            PyCodeObject optimizedCode;
            if (!SharpPyConfig.DisableOptimizer)
            {
                // Quickened Code Object 생성 (instruction offset 사용)
                optimizedCode = new PyQuickenedCodeObject(
                    originalCode,
                    optimizedInstructions,
                    originalCode.Name,
                    _constants,
                    originalCode.Names,
                    originalCode.VarNames,
                    originalCode.ArgCount,
                    originalCode.PosonlyArgCount,
                    originalCode.FreeVars,
                    originalCode.CellVars,
                    originalCode.DefaultValues,
                    originalCode.Flags,
                    originalCode.FileName,
                    originalCode.SourceLines,
                    originalCode.LineNumberTable
                );
            }
            else
            {
                // 최적화 비활성화 시 일반 CodeObject (byte offset 사용)
                optimizedCode = new PyCodeObject(
                    originalCode.Name,
                    optimizedInstructions,
                    _constants,
                    originalCode.Names,
                    originalCode.VarNames,
                    originalCode.ArgCount,
                    originalCode.PosonlyArgCount,
                    0,
                    originalCode.FreeVars,
                    originalCode.CellVars,
                    originalCode.DefaultValues,
                    null,
                    originalCode.Flags,
                    originalCode.FileName,
                    originalCode.SourceLines,
                    isOptimized: true,
                    originalCode.LineNumberTable,
                    optimizedExceptionTable  // CFG에서 재생성된 exception table
                );
            }

            // Set exception table (CFG에서 생성된 것 사용)
            optimizedCode.ExceptionTable.Clear();
            optimizedCode.ExceptionTable.AddRange(optimizedExceptionTable);

            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
#if DEBUG_LOG
                Console.WriteLine($"🔧 ByteCodeOptimizer returning code with {optimizedCode.ExceptionTable.Count} exception table entries");
#endif
            }

            return optimizedCode;
        }

        /// <summary>
        /// 상수 접기 최적화 - 컴파일 타임에 상수 연산 처리
        /// </summary>
        private void ApplyConstantFolding()
        {
            for (int i = 0; i < _instructions.Count - 2; i++)
            {
                var inst1 = _instructions[i];
                var inst2 = _instructions[i + 1];
                var inst3 = _instructions[i + 2];

                // 패턴: LOAD_CONST, LOAD_CONST, BINARY_OP → LOAD_CONST (결과)
                if (inst1.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst2.OpCode == ByteCodeOp.LOAD_CONST &&
                    IsBinaryOpType(inst3.OpCode))
                {
                    var result = EvaluateConstantOperation(
                        _constants[inst1.Argument],
                        _constants[inst2.Argument],
                        inst3.OpCode,
                        inst3.Argument  // For BINARY_OP, this contains the operation type
                    );

                    if (result != null)
                    {
                        int constIndex = GetOrAddConstant(result);
                        
                        // 3개 명령어를 1개로 교체
                        _instructions[i] = CreateInstruction(ByteCodeOp.LOAD_CONST, constIndex, _instructions[i]);
                        _instructions.RemoveAt(i + 1);
                        _instructions.RemoveAt(i + 1); // RemoveAt 후 인덱스가 변경됨
                        
            #if DEBUG_LOG
            Console.WriteLine($"🔄 상수 접기: {inst1.OpCode}+{inst2.OpCode}+{inst3.OpCode} → LOAD_CONST({result})");
#endif
                        i--; // 다음 반복에서 현재 위치부터 다시 검사
                    }
                }
            }
            
            // CPython 3.12: Tuple literal constant folding optimization
            // Pattern: LOAD_CONST a, LOAD_CONST b, ..., BUILD_TUPLE n → LOAD_CONST (a, b, ...)
            ApplyTupleLiteralOptimization();

            // CPython 3.12: BUILD_TUPLE + UNPACK_SEQUENCE optimization
            // Pattern: BUILD_TUPLE n + UNPACK_SEQUENCE n → SWAP n (for n=2,3) or NOP (for n=1)
            // Reference: CPython flowgraph.c line 1487-1499
            ApplyBuildTupleUnpackOptimization();
        }

        /// <summary>
        /// CPython 3.12: Tuple literal constant folding optimization
        /// Pattern: LOAD_CONST a, LOAD_CONST b, ..., BUILD_TUPLE n → LOAD_CONST (a, b, ...)
        /// </summary>
        private void ApplyTupleLiteralOptimization()
        {
            for (int i = 0; i < _instructions.Count; i++)
            {
                var buildTupleInstr = _instructions[i];
                if (buildTupleInstr.OpCode != ByteCodeOp.BUILD_TUPLE)
                    continue;
                
                int tupleSize = buildTupleInstr.Argument;
                if (tupleSize == 0)
                    continue; // Empty tuple, skip for now
                
                // Check if the previous n instructions are all LOAD_CONST
                if (i < tupleSize)
                    continue; // Not enough preceding instructions
                
                bool allLoadConst = true;
                var tupleElements = new List<PyObject>();
                
                for (int j = 0; j < tupleSize; j++)
                {
                    var instrIndex = i - tupleSize + j;
                    var instr = _instructions[instrIndex];
                    
                    if (instr.OpCode != ByteCodeOp.LOAD_CONST)
                    {
                        allLoadConst = false;
                        break;
                    }
                    
                    tupleElements.Add(_constants[instr.Argument]);
                }
                
                if (allLoadConst)
                {
                    // Create tuple constant
                    var tupleConstant = new PyTuple(tupleElements.ToArray());
                    int tupleConstIndex = GetOrAddConstant(tupleConstant);
                    
                    // Replace n LOAD_CONST + BUILD_TUPLE with single LOAD_CONST
                    _instructions[i - tupleSize] = CreateInstruction(ByteCodeOp.LOAD_CONST, tupleConstIndex, _instructions[i - tupleSize]);
                    
                    // Remove the remaining LOAD_CONST instructions and BUILD_TUPLE
                    int removedCount = tupleSize;
                    for (int j = 0; j < tupleSize; j++)
                    {
                        _instructions.RemoveAt(i - tupleSize + 1);
                    }

                    // Update jump offsets after removing instructions
                    UpdateJumpOffsetsAfterRemoval(i - tupleSize + 1, removedCount);

        #if DEBUG_LOG
            Console.WriteLine($"🔄 튤플 상수 접기: {tupleSize}개 LOAD_CONST + BUILD_TUPLE → LOAD_CONST({tupleConstant})");
#endif
                    i -= tupleSize; // Adjust index after removals
                }
            }
        }

        /// <summary>
        /// CPython 3.12: BUILD_TUPLE + UNPACK_SEQUENCE optimization
        ///
        /// Reference: CPython flowgraph.c line 1487-1499
        ///
        /// Patterns:
        /// - BUILD_TUPLE 1 + UNPACK_SEQUENCE 1 → NOP + NOP
        /// - BUILD_TUPLE 2 + UNPACK_SEQUENCE 2 → NOP + SWAP 2
        /// - BUILD_TUPLE 3 + UNPACK_SEQUENCE 3 → NOP + SWAP 3
        ///
        /// This optimization eliminates unnecessary tuple packing/unpacking in assignments like:
        ///   a, b = b, a+b
        /// which compiles to:
        ///   LOAD b, LOAD a, LOAD b, BINARY_OP, BUILD_TUPLE 2, UNPACK_SEQUENCE 2, STORE a, STORE b
        /// and optimizes to:
        ///   LOAD b, LOAD a, LOAD b, BINARY_OP, SWAP 2, STORE a, STORE b
        /// </summary>
        private void ApplyBuildTupleUnpackOptimization()
        {
            for (int i = 0; i < _instructions.Count - 1; i++)
            {
                var buildTupleInstr = _instructions[i];
                if (buildTupleInstr.OpCode != ByteCodeOp.BUILD_TUPLE)
                    continue;

                // Check if next instruction (skipping CACHE) is UNPACK_SEQUENCE with same argument
                int nextIdx = i + 1;

                // Skip CACHE instructions
                while (nextIdx < _instructions.Count && _instructions[nextIdx].OpCode == ByteCodeOp.CACHE)
                {
                    nextIdx++;
                }

                if (nextIdx >= _instructions.Count)
                    continue;

                var unpackInstr = _instructions[nextIdx];
                if (unpackInstr.OpCode != ByteCodeOp.UNPACK_SEQUENCE)
                    continue;

                // Check if both have the same argument
                int tupleSize = buildTupleInstr.Argument;
                if (tupleSize != unpackInstr.Argument)
                    continue;

                // Apply CPython optimization based on tuple size
                switch (tupleSize)
                {
                    case 1:
                        // BUILD_TUPLE 1 + UNPACK_SEQUENCE 1 → NOP + NOP
                        _instructions[i] = CreateInstruction(ByteCodeOp.NOP, 0, _instructions[i]);
                        _instructions[nextIdx] = CreateInstruction(ByteCodeOp.NOP, 0, _instructions[nextIdx]);
#if DEBUG_LOG
                        Console.WriteLine($"🔄 BUILD_TUPLE 1 + UNPACK_SEQUENCE 1 → NOP + NOP at {i}");
#endif
                        break;

                    case 2:
                    case 3:
                        // BUILD_TUPLE n + UNPACK_SEQUENCE n → NOP + SWAP n (for n=2,3)
                        _instructions[i] = CreateInstruction(ByteCodeOp.NOP, 0, _instructions[i]);
                        _instructions[nextIdx] = CreateInstruction(ByteCodeOp.SWAP, tupleSize, _instructions[nextIdx]);
#if DEBUG_LOG
                        Console.WriteLine($"🔄 BUILD_TUPLE {tupleSize} + UNPACK_SEQUENCE {tupleSize} → NOP + SWAP {tupleSize} at {i}");
#endif
                        break;

                    default:
                        // For n > 3, keep BUILD_TUPLE + UNPACK_SEQUENCE (CPython doesn't optimize these)
                        break;
                }
            }
        }

        /// <summary>
        /// 중복 로드 제거 - 연속된 동일한 로드 명령어 최적화
        /// </summary>
        private void ApplyRedundantLoadElimination()
        {
            for (int i = 0; i < _instructions.Count - 1; i++)
            {
                var inst1 = _instructions[i];
                var inst2 = _instructions[i + 1];

                // 패턴: LOAD_NAME x, LOAD_NAME x → LOAD_NAME x, COPY
                if (inst1.OpCode == ByteCodeOp.LOAD_NAME &&
                    inst2.OpCode == ByteCodeOp.LOAD_NAME &&
                    inst1.Argument == inst2.Argument)
                {
                    _instructions[i + 1] = CreateInstruction(ByteCodeOp.COPY, 1, _instructions[i + 1]);
        #if DEBUG_LOG
            Console.WriteLine($"🔄 중복 로드 제거: LOAD_NAME({_names[inst1.Argument]}) 중복 → COPY");
#endif
                }

                // 패턴: LOAD_CONST x, LOAD_CONST x → LOAD_CONST x, COPY
                if (inst1.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst2.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst1.Argument == inst2.Argument)
                {
                    _instructions[i + 1] = CreateInstruction(ByteCodeOp.COPY, 1, _instructions[i + 1]);
        #if DEBUG_LOG
            Console.WriteLine($"🔄 중복 상수 로드 제거: LOAD_CONST 중복 → COPY");
#endif
                }
            }
        }

        /// <summary>
        /// 무용 코드 제거 - 도달 불가능한 코드 및 불필요한 연산 제거
        /// </summary>
        private void ApplyDeadCodeElimination()
        {
            for (int i = 0; i < _instructions.Count - 1; i++)
            {
                var inst1 = _instructions[i];
                var inst2 = _instructions[i + 1];

                // 패턴: LOAD_CONST True, POP_JUMP_IF_FALSE → NOP (항상 참이므로 점프하지 않음)
                if (inst1.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst2.OpCode == ByteCodeOp.POP_JUMP_IF_FALSE &&
                    _constants[inst1.Argument] == PyBool.True)
                {
                    _instructions[i] = CreateInstruction(ByteCodeOp.NOP, 0, _instructions[i]);
                    _instructions[i + 1] = CreateInstruction(ByteCodeOp.NOP, 0, _instructions[i + 1]);
        #if DEBUG_LOG
            Console.WriteLine("🔄 무용 코드 제거: if True 최적화");
#endif
                }

                // 패턴: LOAD_CONST False, POP_JUMP_IF_TRUE → NOP (항상 거짓이므로 점프하지 않음)
                if (inst1.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst2.OpCode == ByteCodeOp.POP_JUMP_IF_TRUE &&
                    _constants[inst1.Argument] == PyBool.False)
                {
                    _instructions[i] = CreateInstruction(ByteCodeOp.NOP, 0, _instructions[i]);
                    _instructions[i + 1] = CreateInstruction(ByteCodeOp.NOP, 0, _instructions[i + 1]);
        #if DEBUG_LOG
            Console.WriteLine("🔄 무용 코드 제거: if False 최적화");
#endif
                }
            }
        }

        /// <summary>
        /// Peephole 패턴 최적화 - 작은 명령어 윈도우에서 패턴 매칭 최적화
        /// </summary>
        private void ApplyPeepholeOptimizations()
        {
            for (int i = 0; i < _instructions.Count - 1; i++)
            {
                var inst1 = _instructions[i];
                var inst2 = _instructions[i + 1];

                // CPython 3.12: LOAD_CONST + RETURN_VALUE → RETURN_CONST 최적화
                if (inst1.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst2.OpCode == ByteCodeOp.RETURN_VALUE)
                {
                    // RETURN_CONST 명령어로 치환 (상수 인덱스 유지)
                    _instructions[i] = CreateInstruction(ByteCodeOp.RETURN_CONST, inst1.Argument, _instructions[i]);
                    _instructions[i + 1] = CreateInstruction(ByteCodeOp.NOP, 0, _instructions[i + 1]); // 제거될 NOP로 마킹
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
            #if DEBUG_LOG
            Console.WriteLine($"🔄 Peephole: LOAD_CONST+RETURN_VALUE → RETURN_CONST (const {inst1.Argument})");
#endif
                    }
                    continue;
                }

                // 패턴: LOAD_CONST, POP_TOP → NOP (상수 로드 후 즉시 버림)
                // 임시 비활성화: 라벨 참조 버그로 인해 점프 주소 계산 오류 발생
                /*if (inst1.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst2.OpCode == ByteCodeOp.POP_TOP)
                {
                    _instructions[i] = CreateInstruction(ByteCodeOp.NOP, 0, _instructions[i]);
                    _instructions[i + 1] = CreateInstruction(ByteCodeOp.NOP, 0, _instructions[i + 1]);
        #if DEBUG_LOG
            Console.WriteLine("🔄 Peephole: 불필요한 LOAD_CONST+POP_TOP 제거");
#endif
                }*/
            }

            // NOP 명령어들 완전 제거 (단, try-except의 필요한 NOP은 보존)
            RemoveNonEssentialNOPs();
        }

        /// <summary>
        /// CPython 3.12 호환: 최적화로 생성된 NOP만 제거, try-except NOP은 보존
        /// </summary>
        private void RemoveNonEssentialNOPs()
        {
            // CPython 3.12 패턴: try-except의 NOP은 절대 제거되지 않음
            // 오직 최적화 과정에서 생성된 NOP(RETURN_CONST 최적화 등)만 제거
            
            var indicesToRemove = new List<int>();
            
            for (int i = 0; i < _instructions.Count; i++)
            {
                if (_instructions[i].OpCode == ByteCodeOp.NOP)
                {
                    // CPython 3.12 호환: try 블록의 NOP 패턴 감지
                    bool isTryBlockNOP = IsTryBlockNOP(i);
                    
                    if (!isTryBlockNOP)
                    {
                        // 최적화로 생성된 NOP만 제거
                        indicesToRemove.Add(i);
                    }
                }
            }
            
            // 역순으로 제거하면서 mapping 업데이트 (인덱스 변경 방지)
            for (int i = indicesToRemove.Count - 1; i >= 0; i--)
            {
                int removedIndex = indicesToRemove[i];
                _instructions.RemoveAt(removedIndex);
                
                // instruction mapping 업데이트: 제거된 instruction 이후의 모든 매핑을 1씩 앞으로
                UpdateMappingAfterRemoval(removedIndex);
            }
            
            if (indicesToRemove.Count > 0)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
        #if DEBUG_LOG
            Console.WriteLine($"🔄 최적화 NOP 제거: {indicesToRemove.Count}개 (try-except NOP 보존됨)");
#endif
                }
            }
        }
        
        /// <summary>
        /// Instruction 제거 후 매핑 업데이트
        /// </summary>
        private void UpdateMappingAfterRemoval(int removedIndex)
        {
            var newMapping = new Dictionary<int, int>();
            
            foreach (var kvp in _instructionMapping)
            {
                int originalIndex = kvp.Key;
                int currentMappedIndex = kvp.Value;
                
                if (currentMappedIndex == removedIndex)
                {
                    // 제거된 instruction: 다음 유효한 instruction으로 매핑
                    newMapping[originalIndex] = Math.Max(0, removedIndex);
                }
                else if (currentMappedIndex > removedIndex)
                {
                    // 제거된 instruction 이후의 instruction들: 1씩 앞으로
                    newMapping[originalIndex] = currentMappedIndex - 1;
                }
                else
                {
                    // 제거된 instruction 이전의 instruction들: 변경 없음
                    newMapping[originalIndex] = currentMappedIndex;
                }
            }
            
            _instructionMapping = newMapping;
        }

        /// <summary>
        /// try 블록의 필수 NOP인지 확인 (CPython 3.12 패턴 매칭)
        /// </summary>
        private bool IsTryBlockNOP(int index)
        {
            // CPython 3.12 패턴: try 블록 NOP의 특징
            // 1. RESUME 다음에 위치하는 경우가 많음
            // 2. 다음 명령어들이 try body 패턴 (LOAD_CONST, STORE_NAME 등)
            // 3. Exception Table에 참조되는 범위 근처
            
            if (index + 1 >= _instructions.Count) return false;
            
            var nextInst = _instructions[index + 1];
            
            // 패턴 1: NOP → PUSH_NULL → LOAD_NAME (CPython 3.12 try body 패턴)  
            if (nextInst.OpCode == ByteCodeOp.PUSH_NULL)
            {
                if (index + 2 < _instructions.Count)
                {
                    var secondNext = _instructions[index + 2];
                    if (secondNext.OpCode == ByteCodeOp.LOAD_NAME)
                    {
                        return true; // try 블록 패턴 매치
                    }
                }
            }
            
            // 패턴 1.1: NOP → LOAD_CONST → STORE_NAME (기존 패턴도 보존)
            if (nextInst.OpCode == ByteCodeOp.LOAD_CONST)
            {
                if (index + 2 < _instructions.Count)
                {
                    var secondNext = _instructions[index + 2];
                    if (secondNext.OpCode == ByteCodeOp.STORE_NAME || 
                        secondNext.OpCode == ByteCodeOp.STORE_GLOBAL)
                    {
                        return true; // try 블록 패턴 매치
                    }
                }
            }
            
            // 패턴 2: RESUME → NOP (모듈 레벨 try 블록)
            if (index > 0 && _instructions[index - 1].OpCode == ByteCodeOp.RESUME)
            {
                return true;
            }
            
            return false; // 최적화로 생성된 NOP
        }

        /// <summary>
        /// 바이너리 연산인지 확인 (CPython 3.12+ BINARY_OP 및 기존 개별 OpCode 지원)
        /// </summary>
        private bool IsBinaryOpType(ByteCodeOp opCode)
        {
            return opCode == ByteCodeOp.BINARY_OP; // CPython 3.12: Only BINARY_OP exists
        }

        /// <summary>
        /// 상수 연산 평가 (컴파일 타임에 계산) - CPython 3.12+ BINARY_OP 지원
        /// </summary>
        private PyObject EvaluateConstantOperation(PyObject left, PyObject right, ByteCodeOp operation, int argument = 0)
        {
            try
            {
                // Handle unified BINARY_OP (CPython 3.12+)
                if (operation == ByteCodeOp.BINARY_OP)
                {
                    var binaryOp = (BinaryOpType)argument;
                    return binaryOp switch
                    {
                        BinaryOpType.ADD => left.Add(right),
                        BinaryOpType.SUBTRACT => left.Subtract(right),
                        BinaryOpType.MULTIPLY => left.Multiply(right),
                        BinaryOpType.TRUE_DIVIDE => left.Divide(right),
                        BinaryOpType.FLOOR_DIVIDE => left.FloorDivide(right),
                        BinaryOpType.MODULO => left.Modulo(right),
                        BinaryOpType.POWER => left.Power(right),
                        BinaryOpType.LSHIFT => left.LeftShift(right),
                        BinaryOpType.RSHIFT => left.RightShift(right),
                        BinaryOpType.AND => left.BitwiseAnd(right),
                        BinaryOpType.OR => left.BitwiseOr(right),
                        BinaryOpType.XOR => left.BitwiseXor(right),
                        _ => null  // Not optimizable
                    };
                }
                
                // CPython 3.12: Legacy binary opcodes removed
                return null;
            }
            catch (Exception)
            {
                // 런타임에 계산해야 하는 경우 (예: 0으로 나누기)
                return null;
            }
        }

        /// <summary>
        /// 상수 풀에 새 상수 추가
        /// </summary>
        private int GetOrAddConstant(PyObject constant)
        {
            // 이미 존재하는 상수인지 확인
            for (int i = 0; i < _constants.Count; i++)
            {
                if (_constants[i].Equals(constant))
                {
                    return i;
                }
            }

            _constants.Add(constant);
            return _constants.Count - 1;
        }

        /// <summary>
        /// CPython 3.12 Superinstructions 생성 - 연속된 바이트코드를 하나로 최적화
        /// </summary>
        private void ApplySuperinstructions()
        {
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
    #if DEBUG_LOG
            Console.WriteLine("🚀 CPython 3.12 Superinstructions 최적화 적용");
#endif
            }
            
            for (int i = 0; i < _instructions.Count - 1; i++)
            {
                var inst1 = _instructions[i];
                var inst2 = _instructions[i + 1];

                // CPython 3.12: No super-instructions, use individual opcodes

                // CPython 3.12: Super-instructions removed for pure compatibility
            }
        }

        /// <summary>
        /// 최적화 후 점프 오프셋 재계산 - 특정 패턴의 올바른 타겟을 찾아 수정
        /// </summary>
        private void RecalculateJumpOffsets()
        {
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
    #if DEBUG_LOG
            Console.WriteLine("🔄 점프 오프셋 재계산 중...");
#endif
            }
            int recalculated = 0;
            
            // CPython 3.12: 루프 패턴 확인
            bool hasForLoop = _instructions.Any(inst => inst.OpCode == ByteCodeOp.FOR_ITER);
            // WHILE 루프는 JUMP_BACKWARD와 POP_JUMP_IF_FALSE가 모두 있어야 함 (if-elif 체인 제외)
            bool hasWhileLoop = _instructions.Any(inst => inst.OpCode == ByteCodeOp.JUMP_BACKWARD) && 
                               _instructions.Any(inst => inst.OpCode == ByteCodeOp.POP_JUMP_IF_FALSE);
            
            if (!hasForLoop && !hasWhileLoop)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
        #if DEBUG_LOG
            Console.WriteLine("✅ 루프 없음 - 점프 오프셋 재계산 불필요");
#endif
                }
                // 루프가 없더라도 if-elif 체인의 점프 오프셋은 업데이트 필요 (최적화로 인한 변경 반영)
                RecalculateIfElifJumps();
                return;
            }
            
            // WHILE 루프 점프 오프셋 재계산
            if (hasWhileLoop)
            {
                RecalculateWhileLoopJumps();
                recalculated++;
            }
            
            // FOR 루프가 없으면 FOR 관련 처리 건너뛰기
            if (!hasForLoop)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
        #if DEBUG_LOG
            Console.WriteLine($"✅ 점프 오프셋 재계산 완료: {recalculated}개 명령어 수정");
#endif
                }
                return;
            }
            
            // FOR 루프 패턴 감지 및 수정: GET_ITER → FOR_ITER → ... → JUMP_BACKWARD
            for (int i = 0; i < _instructions.Count - 1; i++)
            {
                var currentInst = _instructions[i];
                
                // FOR_ITER 명령어를 찾으면, 이 루프의 JUMP_BACKWARD를 찾아 수정
                if (currentInst.OpCode == ByteCodeOp.FOR_ITER)
                {
                    int forIterPos = i;
                    
                    // 해당 FOR_ITER에 대응하는 JUMP_BACKWARD 찾기
                    // FOR 루프는 일반적으로 FOR_ITER 이후에 JUMP_BACKWARD가 하나 있음
                    for (int j = forIterPos + 1; j < _instructions.Count; j++)
                    {
                        var laterInst = _instructions[j];
                        
                        if (laterInst.OpCode == ByteCodeOp.JUMP_BACKWARD)
                        {
                            int jumpPos = j;
                            int currentOffset = laterInst.Argument;
                            
                            // 중첩 루프에서 이 JUMP_BACKWARD가 어떤 FOR_ITER로 가야 하는지 올바르게 찾기
                            int correctForIterPos = FindMatchingForIter(jumpPos);
                            
                            if (correctForIterPos >= 0)
                            {
                                // CPython 3.12 호환: FOR 루프의 JUMP_BACKWARD는 FOR_ITER로 점프
                                // continue 문: FOR_ITER로 직접 점프
                                // 루프 끝: FOR_ITER로 직접 점프
                                // CPython에서 JUMP_BACKWARD는 몇 개의 instruction을 뒤로 점프할지를 의미
                                int correctOffset;
                                if (SharpPyConfig._enable_optimizer)
                                {
                                    // 최적화 활성화: CPython 호환 방식으로 오프셋 계산
                                    // CPython에서 JUMP_BACKWARD n은 현재 위치에서 n개 instruction 뒤로 점프
                                    // 현재 optimizer는 컴파일 시점의 instruction index를 사용하므로 조정 필요
                                    // 범용적인 JUMP_BACKWARD 오프셋 계산
                                    // CPython 방식: currentPos - targetPos
                                    correctOffset = jumpPos - correctForIterPos;

                                    if (!SharpPyConfig.DisassemblyOnlyMode)
                                    {
                            #if DEBUG_LOG
        Console.WriteLine($"  🔧 범용 FOR 루프 JUMP_BACKWARD 계산: jumpPos={jumpPos}, correctForIterPos={correctForIterPos}, correctOffset={correctOffset}");
#endif
                                    }

                                    // 특별한 케이스들도 여전히 지원 (레거시)
                                    if (jumpPos == 11 && correctForIterPos == 5)
                                    {
                                        // continue문: 이미 계산된 값이 맞는지 확인
                                        if (correctOffset != 6)
                                        {
                                            if (!SharpPyConfig.DisassemblyOnlyMode)
                                            {
                                    #if DEBUG_LOG
            Console.WriteLine($"  ⚠️ 레거시 케이스와 다름: expected=6, calculated={correctOffset}");
#endif
                                            }
                                        }
                                    }
                                    else if (jumpPos == 18 && correctForIterPos == 5)
                                    {
                                        // 루프 끝: 이미 계산된 값이 맞는지 확인
                                        if (correctOffset != 13)
                                        {
                                            if (!SharpPyConfig.DisassemblyOnlyMode)
                                            {
                                    #if DEBUG_LOG
            Console.WriteLine($"  ⚠️ 레거시 케이스와 다름: expected=13, calculated={correctOffset}");
#endif
                                            }
                                        }
                                    }

                                    // 디버깅: 실제 계산 과정 출력
                                    if (!SharpPyConfig.DisassemblyOnlyMode)
                                    {
                            #if DEBUG_LOG
            // 바이트 오프셋 기반으로 재계산
            int jumpByteOffset = jumpPos * 2;
            int forIterByteOffset = correctForIterPos * 2;
            int correctByteOffset = jumpByteOffset - forIterByteOffset;
            int correctInstructionOffset = correctByteOffset / 2;
            Console.WriteLine($"  🔍 오프셋 계산: jumpPos={jumpPos} (바이트:{jumpByteOffset}), correctForIterPos={correctForIterPos} (바이트:{forIterByteOffset})");
            Console.WriteLine($"  🔍 바이트 차이: {correctByteOffset}, instruction 오프셋: {correctInstructionOffset}, 현재 계산: {correctOffset}");
#endif
                                    }
                                }
                                else
                                {
                                    // 최적화 비활성화: 논리적 위치 기반 계산
                                    int currentLogicalPos = CalculateLogicalInstructionPosition(jumpPos);
                                    int targetLogicalPos = CalculateLogicalInstructionPosition(correctForIterPos);
                                    correctOffset = currentLogicalPos - targetLogicalPos + 1;
                                }
                                
                                if (currentOffset != correctOffset)
                                {
                                    _instructions[j] = CreateInstruction(ByteCodeOp.JUMP_BACKWARD, correctOffset, _instructions[j]);
                                    if (!SharpPyConfig.DisassemblyOnlyMode)
                                    {
                            #if DEBUG_LOG
            Console.WriteLine($"  🔧 중첩 FOR 루프 JUMP_BACKWARD[{j}]: {currentOffset} → {correctOffset} (FOR_ITER: {correctForIterPos})");
#endif
                                    }
                                    recalculated++;
                                }
                            }
                            
                            // 중첩 루프를 위해 모든 JUMP_BACKWARD 처리 (하나만 처리하지 말고 계속)
                            // break; // 제거: 중첩 루프에서 여러 JUMP_BACKWARD가 있을 수 있음
                        }
                        
                        // 다른 FOR_ITER나 함수 끝을 만나면 중단
                        if (laterInst.OpCode == ByteCodeOp.FOR_ITER || 
                            laterInst.OpCode == ByteCodeOp.RETURN_VALUE)
                        {
                            break;
                        }
                    }
                    
                    // 동일한 FOR 루프의 POP_JUMP_IF_FALSE 조건부 점프 재계산
                    // 단, MATCH 패턴에서 사용되는 POP_JUMP_IF_FALSE는 제외 (다음 케이스로 점프해야 함)
                    int foundCount = 0;
                    for (int j = forIterPos + 1; j < _instructions.Count; j++)
                    {
                        var laterInst = _instructions[j];
                        
                        // Removed debug output for cleaner execution
                        
                        if (laterInst.OpCode == ByteCodeOp.POP_JUMP_IF_FALSE)
                        {
                            foundCount++;
                            int jumpPos = j;
                            int currentTarget = laterInst.Argument;
                            
                            // Pattern matching 감지: COPY 1 + [LOAD_CONST] + COMPARE_OP + POP_JUMP_IF_FALSE 패턴
                            bool isPatternMatching = false;
                            if (j >= 3)
                            {
                                var prevInst = _instructions[j - 1];        // Should be COMPARE_OP
                                var prev3Inst = _instructions[j - 3];       // Should be COPY 1
                                if (prev3Inst.OpCode == ByteCodeOp.COPY && prev3Inst.Argument == 1 &&
                                    prevInst.OpCode == ByteCodeOp.COMPARE_OP)
                                {
                                    isPatternMatching = true;
                                }
                            }
                            
                            // Pattern matching의 POP_JUMP_IF_FALSE는 수정하지 않음 (다음 케이스로 점프)
                            if (isPatternMatching)
                            {
                                continue;
                            }
                            
                            // 조건부 점프 타겟 재계산 - 더 관대한 조건으로 수정
                            bool shouldPointToForIter = false;
                            
                            // 1. 기존 조건: FOR_ITER + 1을 가리키는 경우
                            if (currentTarget == forIterPos + 1)
                            {
                                shouldPointToForIter = true;
                            }
                            // 2. FOR_ITER 근처를 가리키는 경우 (최적화로 인한 위치 변경) - 임시 비활성화
                            // else if (currentTarget >= forIterPos - 3 && currentTarget <= forIterPos + 5)
                            // {
                            //     shouldPointToForIter = true;
                            // }
                            // 3. 자기 자신 근처를 가리켜 무한 루프를 만드는 경우 - 제거
                            // else if (currentTarget >= jumpPos - 5 && currentTarget <= jumpPos + 2)
                            // {
                            //     shouldPointToForIter = true;
                            // }
                            
                            if (shouldPointToForIter)
                            {
                                _instructions[j] = CreateInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, forIterPos, _instructions[j]);
                    #if DEBUG_LOG
            Console.WriteLine($"  🔧 조건부 점프 POP_JUMP_IF_FALSE[{j}]: {currentTarget} → {forIterPos} (FOR_ITER)");
#endif
                                recalculated++;
                            }
                        }
                        
                        // JUMP_BACKWARD까지만 처리 (해당 FOR 루프 완료)
                        if (laterInst.OpCode == ByteCodeOp.JUMP_BACKWARD)
                        {
                            break;
                        }
                        
                        // 다른 FOR_ITER나 함수 끝을 만나면 중단
                        if (laterInst.OpCode == ByteCodeOp.FOR_ITER || 
                            laterInst.OpCode == ByteCodeOp.RETURN_VALUE)
                        {
                            break;
                        }
                    }
                }
            }
            
            // CPython 3.12 방식: FOR_ITER → END_FOR 구조 점프 오프셋 재계산
            // Superinstructions로 인해 명령어 위치가 변경되므로 FOR_ITER 오프셋도 업데이트 필요
            
            bool hasEndFor = _instructions.Any(inst => inst.OpCode == ByteCodeOp.END_FOR);
            if (hasEndFor)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
        #if DEBUG_LOG
            Console.WriteLine("🔄 FOR_ITER → END_FOR 점프 오프셋 재계산 중...");
#endif
        #if DEBUG_LOG
            Console.WriteLine("📋 모든 FOR_ITER와 END_FOR 위치:");
#endif
                    for (int i = 0; i < _instructions.Count; i++)
                    {
                        var inst = _instructions[i];
                        if (inst.OpCode == ByteCodeOp.FOR_ITER || inst.OpCode == ByteCodeOp.END_FOR)
                        {
                #if DEBUG_LOG
            Console.WriteLine($"    {i}: {inst.OpCode} (arg: {inst.Argument})");
#endif
                        }
                    }
                }
                for (int i = 0; i < _instructions.Count; i++)
                {
                    var instruction = _instructions[i];
                    if (instruction.OpCode == ByteCodeOp.FOR_ITER)
                    {
                        // FOR_ITER에서 대응하는 END_FOR 찾기
                        int targetEndFor = FindMatchingEndFor(i);
                        if (targetEndFor >= 0)
                        {
                            // CPython 3.12 호환: FOR_ITER는 instruction 단위 오프셋 사용 (바이트가 아님)
                            // FOR_ITER 다음 instruction부터 END_FOR까지의 instruction 개수
                            int instructionOffsetToTarget = targetEndFor - i - 1;
                            if (instructionOffsetToTarget != instruction.Argument)
                            {
                                _instructions[i] = CreateInstruction(ByteCodeOp.FOR_ITER, instructionOffsetToTarget, _instructions[i]);
                                if (!SharpPyConfig.DisassemblyOnlyMode)
                                {
                        #if DEBUG_LOG
            Console.WriteLine($"  🔧 FOR_ITER[{i}]: 오프셋 {instruction.Argument} → {instructionOffsetToTarget} (END_FOR at {targetEndFor})");
#endif
                                }
                                recalculated++;
                            }
                        }
                    }
                }
            }

            // CPython 3.12: 단일 패스로 모든 JUMP_BACKWARD 처리 완료 (중복 재계산 방지)

            // JUMP_FORWARD (break문) 오프셋 재계산 (JUMP_FORWARD가 있는 경우만)
            bool hasJumpForward = _instructions.Any(inst => inst.OpCode == ByteCodeOp.JUMP_FORWARD);
            if (hasJumpForward)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
        #if DEBUG_LOG
            Console.WriteLine("🔄 JUMP_FORWARD (break문) 점프 오프셋 재계산 중...");
#endif
                }
                for (int i = 0; i < _instructions.Count; i++)
                {
                    var instruction = _instructions[i];
                    if (instruction.OpCode == ByteCodeOp.JUMP_FORWARD)
                    {
                        int currentOffset = instruction.Argument;
                        
                        // JUMP_FORWARD는 break문에서 주로 사용됨
                        // 0 오프셋이면 무한루프를 만들므로 수정 필요
                        if (currentOffset == 0)
                        {
                            // break문은 가장 가까운 END_FOR로 점프해야 함
                            int targetEndFor = FindNearestEndFor(i);
                            if (targetEndFor >= 0)
                            {
                                int correctOffset = targetEndFor - i - 1;
                                _instructions[i] = CreateInstruction(ByteCodeOp.JUMP_FORWARD, correctOffset, _instructions[i]);
                    #if DEBUG_LOG
            Console.WriteLine($"  🔧 JUMP_FORWARD[{i}]: break문 0 오프셋 → {correctOffset} (END_FOR at {targetEndFor})");
#endif
                                recalculated++;
                            }
                        }
                    }
                }
            }
            
            if (recalculated > 0)
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
        #if DEBUG_LOG
            Console.WriteLine($"✅ 점프 오프셋 재계산 완료: {recalculated}개 명령어 수정");
#endif
                }
            }
            else
            {
                if (!SharpPyConfig.DisassemblyOnlyMode)
                {
        #if DEBUG_LOG
            Console.WriteLine("✅ 점프 오프셋 재계산 완료: 수정 필요 없음");
#endif
                }
            }
        }

        /// <summary>
        /// WHILE 루프의 POP_JUMP_IF_FALSE 점프 오프셋 재계산
        /// 최적화로 인해 변경된 명령어 위치에 맞게 점프 타겟을 다시 계산
        /// CPython 3.12 패턴: WHILE 루프의 두 POP_JUMP_IF_FALSE는 동일한 루프 종료점을 가리킴
        /// 주의: if-elif 체인의 POP_JUMP_IF_FALSE는 수정하지 않음 (CompileIf에서 올바르게 처리됨)
        /// </summary>
        private void RecalculateWhileLoopJumps()
        {
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
    #if DEBUG_LOG
            Console.WriteLine("🔄 WHILE 루프 점프 오프셋 재계산 중...");
#endif
            }

            // 먼저 모든 FOR 루프 범위를 찾아서 FOR 루프 내의 JUMP_BACKWARD는 제외
            var forLoopRanges = new List<(int start, int end)>();
            for (int i = 0; i < _instructions.Count; i++)
            {
                if (_instructions[i].OpCode == ByteCodeOp.FOR_ITER)
                {
                    int endForPos = FindMatchingEndFor(i);
                    if (endForPos >= 0)
                    {
                        forLoopRanges.Add((i, endForPos));
                    }
                }
            }

            // WHILE 루프 패턴 찾기: COMPARE_OP → POP_JUMP_IF_FALSE ... JUMP_BACKWARD ... POP_JUMP_IF_FALSE
            // CPython 3.12: 두 POP_JUMP_IF_FALSE가 동일한 루프 종료점을 가리켜야 함

            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
    #if DEBUG_LOG
        Console.WriteLine($"🔍 전체 JUMP_BACKWARD 명령어 개수: {_instructions.Count(inst => inst.OpCode == ByteCodeOp.JUMP_BACKWARD)}");
        for (int idx = 0; idx < _instructions.Count; idx++)
        {
            if (_instructions[idx].OpCode == ByteCodeOp.JUMP_BACKWARD)
            {
                Console.WriteLine($"  JUMP_BACKWARD found at instruction {idx}: arg={_instructions[idx].Argument}");
            }
        }
#endif
            }

            for (int i = 0; i < _instructions.Count; i++)
            {
                // JUMP_BACKWARD 명령어를 찾음 - 이것이 실제 while 루프의 지표
                if (_instructions[i].OpCode == ByteCodeOp.JUMP_BACKWARD)
                {
                    int jumpBackwardPos = i;

                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
            #if DEBUG_LOG
        Console.WriteLine($"🔍 Processing JUMP_BACKWARD at instruction {jumpBackwardPos}");
#endif
                    }

                    // FOR 루프 내의 JUMP_BACKWARD인지 확인 (continue문)
                    bool isInsideForLoop = false;
                    foreach (var (start, end) in forLoopRanges)
                    {
                        if (jumpBackwardPos > start && jumpBackwardPos < end)
                        {
                            isInsideForLoop = true;
                            break;
                        }
                    }

                    // FOR 루프 내의 JUMP_BACKWARD는 continue문이므로 while 루프 처리에서 제외
                    if (isInsideForLoop)
                    {
                        if (!SharpPyConfig.DisassemblyOnlyMode)
                        {
                #if DEBUG_LOG
            Console.WriteLine($"  🔍 JUMP_BACKWARD at {jumpBackwardPos} - FOR 루프 내부 (continue문), while 처리 건너뜀");
#endif
                        }
                        continue;
                    }

                    // JUMP_BACKWARD 다음의 첫 번째 명령어가 실제 루프 종료점
                    // 최적화로 인해 명령어가 재배열될 수 있으므로 정확한 위치 찾기
                    int loopEndPos = jumpBackwardPos + 1;

                    // 디버깅: 실제 루프 종료점 확인
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
            #if DEBUG_LOG
            Console.WriteLine($"  🔍 JUMP_BACKWARD at {jumpBackwardPos}, 계산된 루프 종료점: {loopEndPos}");
#endif
                        if (loopEndPos < _instructions.Count)
                        {
                #if DEBUG_LOG
            Console.WriteLine($"  🔍 루프 종료점 명령어: {_instructions[loopEndPos].OpCode}");
#endif
                        }
                    }

                    // CPython 3.12 완전 호환 WHILE 루프 분석
                    // 패턴: 초기조건체크 → POP_JUMP_IF_FALSE → [loop body] → 재조건체크 → JUMP_BACKWARD
                    int loopBodyStartPos = -1;

                    // CPython 3.12 패턴 분석: 첫 번째 POP_JUMP_IF_FALSE 다음이 loop body 시작
                    for (int k = 0; k < jumpBackwardPos; k++)
                    {
                        if (_instructions[k].OpCode == ByteCodeOp.POP_JUMP_IF_FALSE)
                        {
                            // 첫 번째 POP_JUMP_IF_FALSE 다음이 loop body 시작점
                            loopBodyStartPos = k + 1;
                            // ALWAYS print for debugging
                            Console.WriteLine($"[PyOptimizer] Found POP_JUMP_IF_FALSE at {k}, loop body starts at {loopBodyStartPos}");
                            #if DEBUG_LOG
                            Console.WriteLine($"  🔍 CPython 패턴 분석: POP_JUMP_IF_FALSE at {k}, loop body starts at {loopBodyStartPos}");
                            #endif
                            break;
                        }
                    }

                    // JUMP_BACKWARD 타겟 결정
                    int jumpBackwardTarget = -1;
                    var jumpInst = _instructions[jumpBackwardPos];

                    // continue statement인지 루프 끝인지 판단
                    // continue statement는 일반적으로 POP_JUMP_IF_FALSE 바로 다음에 있음
                    bool isContinueStatement = false;
                    if (jumpBackwardPos > 0 && _instructions[jumpBackwardPos - 1].OpCode == ByteCodeOp.POP_JUMP_IF_FALSE)
                    {
                        isContinueStatement = true;
                        // continue statement: 루프 조건 확인으로 점프
                        // WHILE 루프에서는 해당 while 루프의 조건 확인 부분으로 점프해야 함
                        // CPython 호환: JUMP_BACKWARD는 loop body 시작점으로 점프해야 함
                        if (loopBodyStartPos != -1)
                        {
                            jumpBackwardTarget = loopBodyStartPos;
                        }
                        else
                        {
                            // fallback: JUMP_BACKWARD 이전의 가장 가까운 COMPARE_OP 이전의 LOAD_NAME 찾기
                            for (int k = jumpBackwardPos - 1; k >= 0; k--)
                            {
                                if (_instructions[k].OpCode == ByteCodeOp.COMPARE_OP)
                                {
                                    // 이 COMPARE_OP 이전의 LOAD_NAME 찾기
                                    for (int m = k - 1; m >= 0; m--)
                                    {
                                        if (_instructions[m].OpCode == ByteCodeOp.LOAD_NAME ||
                                            _instructions[m].OpCode == ByteCodeOp.LOAD_GLOBAL ||
                                            _instructions[m].OpCode == ByteCodeOp.LOAD_FAST)
                                        {
                                            jumpBackwardTarget = m;
                                            break;
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        // 루프 끝: 루프 바디 시작으로 점프
                        // 우선순위 1: 이미 계산된 loopBodyStartPos 사용 (CPython 3.12 호환)
                        if (loopBodyStartPos >= 0)
                        {
                            jumpBackwardTarget = loopBodyStartPos;
                            #if DEBUG_LOG
                            Console.WriteLine($"  🔍 Using loopBodyStartPos as jumpBackwardTarget: {jumpBackwardTarget}");
                            #endif
                        }
                        // 우선순위 2: COMPARE_OP 패턴으로 찾기 (fallback)
                        else
                        {
                            bool foundFirstCompare = false;
                            for (int k = 0; k < jumpBackwardPos; k++)
                            {
                                if (_instructions[k].OpCode == ByteCodeOp.COMPARE_OP && !foundFirstCompare)
                                {
                                    foundFirstCompare = true;
                                    // COMPARE_OP 다음의 POP_JUMP_IF_FALSE 다음을 찾기
                                    for (int m = k + 1; m < jumpBackwardPos; m++)
                                    {
                                        if (_instructions[m].OpCode == ByteCodeOp.POP_JUMP_IF_FALSE)
                                        {
                                            jumpBackwardTarget = m + 1; // POP_JUMP_IF_FALSE 다음이 루프 바디 시작
                                            #if DEBUG_LOG
                                            Console.WriteLine($"  🔍 Found jumpBackwardTarget via COMPARE_OP pattern: {jumpBackwardTarget}");
                                            #endif
                                            break;
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    // JUMP_BACKWARD oparg 수정
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
            #if DEBUG_LOG
        Console.WriteLine($"  🔍 JUMP_BACKWARD[{jumpBackwardPos}] 타겟 계산: jumpBackwardTarget={jumpBackwardTarget}, isContinueStatement={isContinueStatement}");
#endif
                    }

                    if (jumpBackwardTarget >= 0)
                    {
                        int correctOpArg = jumpBackwardPos - jumpBackwardTarget;
                        if (correctOpArg != jumpInst.Argument)
                        {
                            _instructions[jumpBackwardPos] = CreateInstruction(ByteCodeOp.JUMP_BACKWARD, correctOpArg, _instructions[jumpBackwardPos]);

                            // ALWAYS print this to debug while loop issue
                            Console.WriteLine($"[PyOptimizer] WHILE JUMP_BACKWARD[{jumpBackwardPos}]: oparg {jumpInst.Argument} → {correctOpArg} (target: {jumpBackwardTarget})");

                            if (!SharpPyConfig.DisassemblyOnlyMode)
                            {
                    #if DEBUG_LOG
            Console.WriteLine($"  🔧 WHILE JUMP_BACKWARD[{jumpBackwardPos}]: {jumpInst.Argument} → {correctOpArg} (target: {jumpBackwardTarget}, {(isContinueStatement ? "continue" : "loop end")})");
#endif
                            }
                        }
                        else
                        {
                            if (!SharpPyConfig.DisassemblyOnlyMode)
                            {
                    #if DEBUG_LOG
            Console.WriteLine($"  ✅ WHILE JUMP_BACKWARD[{jumpBackwardPos}]: {jumpInst.Argument} 이미 올바름 (target: {jumpBackwardTarget}, {(isContinueStatement ? "continue" : "loop end")})");
#endif
                            }
                        }
                    }
                    else
                    {
                        // jumpBackwardTarget을 찾지 못함 - oparg가 바이트 단위로 계산된 상태로 남음
                        // 이 경우 VM에서 instruction 단위로 해석하면 잘못된 위치로 점프할 수 있음
                        #if DEBUG_LOG
                        Console.WriteLine($"  ❌ WHILE JUMP_BACKWARD[{jumpBackwardPos}]: 타겟을 찾을 수 없음");
                        Console.WriteLine($"     loopBodyStartPos={loopBodyStartPos}, isContinueStatement={isContinueStatement}");
                        Console.WriteLine($"     ⚠️ WARNING: oparg가 재계산되지 않아 무한 루프가 발생할 수 있음!");
                        #endif
                    }

                    // 이 JUMP_BACKWARD 이전의 POP_JUMP_IF_FALSE들을 찾아서 루프 종료점으로 수정
                    int popJumpCount = 0;
                    for (int j = jumpBackwardPos - 1; j >= 0; j--)
                    {
                        var inst = _instructions[j];
                        if (inst.OpCode == ByteCodeOp.POP_JUMP_IF_FALSE)
                        {
                            popJumpCount++;
                            if (!SharpPyConfig.DisassemblyOnlyMode)
                            {
                    #if DEBUG_LOG
            Console.WriteLine($"  🔍 발견된 POP_JUMP_IF_FALSE #{popJumpCount} at [{j}]: {inst.Argument} (원래 타겟: {j + inst.Argument + 1})");
#endif
                            }
                            // 올바른 루프 종료점으로 점프하도록 상대 오프셋 재계산
                            int correctRelativeOffset = loopEndPos - j - 1;

                            if (correctRelativeOffset != inst.Argument)
                            {
                                _instructions[j] = CreateInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, correctRelativeOffset, _instructions[j]);

                                if (!SharpPyConfig.DisassemblyOnlyMode)
                                {
                        #if DEBUG_LOG
            Console.WriteLine($"  🔧 WHILE POP_JUMP_IF_FALSE[{j}]: {inst.Argument} → {correctRelativeOffset} (target: {loopEndPos})");
#endif
                                }
                            }
                        }
                        // 다른 JUMP_BACKWARD를 만나면 중첩 루프이므로 중단
                        else if (inst.OpCode == ByteCodeOp.JUMP_BACKWARD)
                        {
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 주어진 FOR_ITER 명령어에 대응하는 END_FOR 위치 찾기
        /// CPython 3.12 FOR_ITER → END_FOR 구조에서 매칭되는 END_FOR 찾기
        /// </summary>
        private int FindMatchingEndFor(int forIterPos)
        {
            // FOR_ITER에서 시작해서 대응하는 END_FOR 찾기
            // 중첩된 루프를 고려해야 함
            int nestedLevel = 1; // 현재 FOR_ITER에 대한 END_FOR를 찾아야 하므로 1부터 시작
            
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
    #if DEBUG_LOG
            Console.WriteLine($"🔍 FindMatchingEndFor: FOR_ITER at {forIterPos} 에 대한 END_FOR 찾는 중...");
#endif
            }
            
            for (int i = forIterPos + 1; i < _instructions.Count; i++)
            {
                var instruction = _instructions[i];
                
                if (instruction.OpCode == ByteCodeOp.FOR_ITER)
                {
                    nestedLevel++; // 중첩된 FOR_ITER 발견, 레벨 증가
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
            #if DEBUG_LOG
            Console.WriteLine($"    FOR_ITER at {i}, level → {nestedLevel}");
#endif
                    }
                }
                else if (instruction.OpCode == ByteCodeOp.END_FOR)
                {
                    nestedLevel--; // END_FOR 발견, 레벨 감소
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
            #if DEBUG_LOG
            Console.WriteLine($"    END_FOR at {i}, level → {nestedLevel}");
#endif
                    }
                    if (nestedLevel == 0)
                    {
                        // 이것이 매칭되는 END_FOR (현재 FOR_ITER에 대응)
                        if (!SharpPyConfig.DisassemblyOnlyMode)
                        {
                #if DEBUG_LOG
            Console.WriteLine($"✅ FOR_ITER[{forIterPos}] → END_FOR[{i}] 매칭 완료");
#endif
                        }
                        return i;
                    }
                }
            }
            
            return -1; // 매칭되는 END_FOR을 찾지 못함
        }
        
        /// <summary>
        /// 주어진 JUMP_BACKWARD 명령어에 대응하는 FOR_ITER 위치 찾기
        /// 중첩 루프에서 JUMP_BACKWARD가 어떤 FOR_ITER로 돌아가야 하는지 계산
        /// </summary>
        private int FindMatchingForIter(int jumpBackPos)
        {
            // JUMP_BACKWARD에서 뒤쪽으로 가면서 매칭되는 FOR_ITER 찾기
            // 중첩된 루프를 고려해야 함
            int nestedLevel = 0;
            
            for (int i = jumpBackPos - 1; i >= 0; i--)
            {
                var instruction = _instructions[i];
                
                if (instruction.OpCode == ByteCodeOp.END_FOR)
                {
                    nestedLevel++;
                }
                else if (instruction.OpCode == ByteCodeOp.FOR_ITER)
                {
                    if (nestedLevel == 0)
                    {
                        // 이것이 매칭되는 FOR_ITER
                        return i;
                    }
                    nestedLevel--;
                }
            }
            
            return -1; // 매칭되는 FOR_ITER을 찾지 못함
        }
        
        /// <summary>
        /// 주어진 JUMP_FORWARD 위치에서 가장 가까운 END_FOR 찾기
        /// break문에서 사용됨
        /// </summary>
        private int FindNearestEndFor(int jumpForwardPos)
        {
            // JUMP_FORWARD에서 앞쪽으로 가면서 가장 가까운 END_FOR 찾기
            for (int i = jumpForwardPos + 1; i < _instructions.Count; i++)
            {
                var instruction = _instructions[i];
                if (instruction.OpCode == ByteCodeOp.END_FOR)
                {
                    return i;
                }
            }
            
            return -1; // 매칭되는 END_FOR을 찾지 못함
        }
        
        /// <summary>
        /// 두 명령어 위치 사이의 바이트 오프셋 계산 (CPython 3.12 호환)
        /// 인라인 캐시를 포함한 정확한 바이트 크기를 계산
        /// </summary>
        private int CalculateByteOffsetBetweenInstructions(int fromIndex, int toIndex)
        {
            int totalBytes = 0;
            int startIndex = Math.Min(fromIndex, toIndex);
            int endIndex = Math.Max(fromIndex, toIndex);
            
            // fromIndex부터 toIndex까지의 모든 명령어들의 바이트 크기 합산
            for (int i = startIndex; i < endIndex; i++)
            {
                var instruction = _instructions[i];
                totalBytes += PyJumpBackwardUtil.GetCPythonInstructionSize(instruction.OpCode, instruction.Argument);
            }
            
            return totalBytes;
        }
        
        /// <summary>
        /// CPython 3.12 논리적 instruction position 계산 (CACHE 포함)
        /// 각 명령어는 1 + 인라인 캐시 엔트리 수만큼 논리적 공간을 차지
        /// </summary>
        private int CalculateLogicalInstructionPosition(int instructionIndex)
        {
            int logicalPosition = 0;

            for (int i = 0; i < instructionIndex; i++)
            {
                var instruction = _instructions[i];
                // 각 명령어는 1 + 인라인 캐시 엔트리 개수만큼 논리적 공간을 차지
                logicalPosition += 1 + PyJumpBackwardUtil.GetInlineCacheEntries(instruction.OpCode);
            }

            return logicalPosition;
        }

        /// <summary>
        /// CPython 3.12 FOR_ITER argument 계산 (실제 instruction 개수만)
        /// FOR_ITER의 argument는 CACHE 명령어를 제외한 실제 instruction 개수
        /// </summary>
        private int CalculateInstructionOffsetWithCache(int fromIndex, int toIndex)
        {
            int instructionCount = 0;
            int startIndex = Math.Min(fromIndex, toIndex);
            int endIndex = Math.Max(fromIndex, toIndex);

            // fromIndex부터 toIndex까지의 실제 명령어 개수 계산 (CACHE 제외)
            for (int i = startIndex; i < endIndex; i++)
            {
                var instruction = _instructions[i];
                // CPython 3.12: FOR_ITER oparg는 실제 instruction 개수만 계산 (CACHE 제외)
                instructionCount += 1;
            }

            return instructionCount;
        }
        
        
        /// <summary>
        /// 명령어 인덱스에서 누적 바이트 오프셋 계산
        /// </summary>
        
        /// <summary>
        /// if-elif 체인의 POP_JUMP_IF_FALSE 점프 오프셋 재계산
        /// 실제로는 원래 컴파일러가 생성한 바이트코드가 이미 올바르므로 아무 작업도 하지 않음
        /// 이 함수는 이전에 문제를 일으켰던 잘못된 최적화였음
        /// </summary>
        private void RecalculateIfElifJumps()
        {
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
    #if DEBUG_LOG
            Console.WriteLine("🔄 if-elif 체인 점프 오프셋 재계산 중...");
#endif
    #if DEBUG_LOG
            Console.WriteLine("  ✅ 컴파일러가 이미 올바른 점프 오프셋을 계산했으므로 수정하지 않음");
#endif
            }
            
            // 아무 작업도 하지 않음 - 원래 컴파일러의 바이트코드가 이미 올바름
            // 이전의 "재계산" 로직이 오히려 올바른 점프를 망가뜨리고 있었음
        }

        /// <summary>
        /// if-elif 체인에서 다음 조건문 또는 else 블록을 찾습니다
        /// </summary>
        private int FindNextIfElifElseBlock(int currentPos)
        {
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
    #if DEBUG_LOG
            Console.WriteLine($"🔍 FindNextIfElifElseBlock: currentPos={currentPos}에서 다음 블록 찾는 중...");
#endif
            }
            
            for (int i = currentPos + 1; i < _instructions.Count; i++)
            {
                var inst = _instructions[i];
                
                // 다음 elif 조건의 시작: LOAD_FAST 또는 LOAD_NAME (변수 로드)
                if (inst.OpCode == ByteCodeOp.LOAD_FAST || inst.OpCode == ByteCodeOp.LOAD_NAME)
                {
                    // 이것이 비교 패턴의 시작인지 확인 (LOAD_FAST → LOAD_CONST → COMPARE_OP)
                    if (i + 2 < _instructions.Count &&
                        (_instructions[i + 1].OpCode == ByteCodeOp.LOAD_CONST || _instructions[i + 1].OpCode == ByteCodeOp.LOAD_NAME) &&
                        _instructions[i + 2].OpCode == ByteCodeOp.COMPARE_OP)
                    {
                        if (!SharpPyConfig.DisassemblyOnlyMode)
                        {
                #if DEBUG_LOG
            Console.WriteLine($"    ✅ 다음 elif 조건 발견: {i} ({inst.OpCode})");
#endif
                        }
                        return i;
                    }
                }
                
                // else 블록: RETURN_CONST 또는 RETURN_VALUE (더 이상의 조건 없이 바로 반환)
                if (inst.OpCode == ByteCodeOp.RETURN_CONST || inst.OpCode == ByteCodeOp.RETURN_VALUE)
                {
                    if (!SharpPyConfig.DisassemblyOnlyMode)
                    {
            #if DEBUG_LOG
            Console.WriteLine($"    ✅ else 블록(반환문) 발견: {i} ({inst.OpCode})");
#endif
                    }
                    return i;
                }
            }
            
            if (!SharpPyConfig.DisassemblyOnlyMode)
            {
    #if DEBUG_LOG
            Console.WriteLine($"    ❌ 다음 블록을 찾을 수 없음");
#endif
            }
            return -1; // 찾을 수 없음
        }

        /// <summary>
        /// 함수의 끝을 찾습니다 (RETURN_* 명령어 위치)
        /// </summary>
        private int FindFunctionEnd(int currentPos)
        {
            for (int i = _instructions.Count - 1; i > currentPos; i--)
            {
                var inst = _instructions[i];
                if (inst.OpCode == ByteCodeOp.RETURN_CONST || 
                    inst.OpCode == ByteCodeOp.RETURN_VALUE)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 명령어 제거 후 점프 오프셋 업데이트
        /// </summary>
        private void UpdateJumpOffsetsAfterRemoval(int removalStart, int removedCount)
        {
            for (int i = 0; i < _instructions.Count; i++)
            {
                var instruction = _instructions[i];

                // 점프 명령어들의 오프셋 업데이트
                if (IsJumpInstruction(instruction.OpCode))
                {
                    int currentTarget = GetJumpTarget(i, instruction);

                    // 제거된 영역 이후를 가리키는 점프들은 오프셋을 줄여야 함
                    if (currentTarget >= removalStart)
                    {
                        int newOffset = instruction.Argument - removedCount;
                        if (newOffset < 0)
                        {
                            // 점프 대상이 제거된 영역 내부였다면, 제거 시작점으로 보정
                            newOffset = removalStart - i - 1;
                            if (newOffset < 0) newOffset = 0;
                        }
                        _instructions[i] = CreateInstruction(instruction.OpCode, newOffset, _instructions[i]);
                    }
                }
            }
        }

        /// <summary>
        /// 점프 명령어인지 확인
        /// </summary>
        private bool IsJumpInstruction(ByteCodeOp opCode)
        {
            return opCode == ByteCodeOp.JUMP_FORWARD ||
                   opCode == ByteCodeOp.JUMP_BACKWARD ||
                   opCode == ByteCodeOp.POP_JUMP_IF_FALSE ||
                   opCode == ByteCodeOp.POP_JUMP_IF_TRUE ||
                   opCode == ByteCodeOp.POP_JUMP_IF_NONE ||
                   opCode == ByteCodeOp.POP_JUMP_IF_NOT_NONE;
        }

        /// <summary>
        /// 점프 대상 계산
        /// </summary>
        private int GetJumpTarget(int instructionIndex, ByteCodeInstruction instruction)
        {
            if (instruction.OpCode == ByteCodeOp.JUMP_BACKWARD)
            {
                return instructionIndex - instruction.Argument - 1;
            }
            else
            {
                return instructionIndex + instruction.Argument + 1;
            }
        }
    }
}