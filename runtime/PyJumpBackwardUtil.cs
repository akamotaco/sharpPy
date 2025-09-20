using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 호환 JUMP_BACKWARD 유틸리티 클래스
    /// 모든 JUMP_BACKWARD 관련 계산을 통합 관리하여 일관성과 정확성을 보장
    /// </summary>
    public static class PyJumpBackwardUtil
    {
        /// <summary>
        /// CPython 3.12 호환 바이트 오프셋 계산
        /// 각 instruction의 정확한 바이트 크기를 계산하여 누적
        /// </summary>
        public static int CalculateByteOffset(int instructionIndex, List<ByteCodeInstruction> instructions)
        {
            int byteOffset = 0;
            for (int i = 0; i < instructionIndex && i < instructions.Count; i++)
            {
                byteOffset += GetCPythonInstructionSize(instructions[i].OpCode, instructions[i].Argument);
            }
            return byteOffset;
        }

        /// <summary>
        /// CPython 3.12 호환 instruction 크기 계산
        /// 인라인 캐시 엔트리와 Extended args를 모두 고려한 정확한 바이트 크기 반환
        /// </summary>
        public static int GetCPythonInstructionSize(ByteCodeOp op, int arg)
        {
            int cacheEntries = GetInlineCacheEntries(op);
            return 2 + (cacheEntries * 2); // 기본 2바이트 + 인라인 캐시 엔트리들
        }
        
        /// <summary>
        /// CPython 3.12 dis._inline_cache_entries 매핑 테이블
        /// 각 명령어의 인라인 캐시 엔트리 개수를 반환
        /// 참조: https://github.com/python/cpython/blob/3.12/Lib/dis.py#L241-L254
        /// </summary>
        public static int GetInlineCacheEntries(ByteCodeOp op)
        {
            // 인라인 캐시가 있는 명령어들 (CPython 3.12 기준 12개)
            return op switch
            {
                // opcode 25 (BINARY_SUBSCR) - 1 cache entry (CPython 3.12 verified)
                ByteCodeOp.BINARY_SUBSCR => 1,
                // opcode 60 (STORE_SUBSCR) - 1 cache entry  
                ByteCodeOp.STORE_SUBSCR => 1,
                // opcode 90 (UNPACK_SEQUENCE) - 1 cache entry
                ByteCodeOp.UNPACK_SEQUENCE => 1,
                // opcode 93 (FOR_ITER) - 1 cache entry
                ByteCodeOp.FOR_ITER => 1,
                // opcode 95 (STORE_ATTR) - 4 cache entries
                ByteCodeOp.STORE_ATTR => 4,
                // opcode 106 (LOAD_ATTR) - 9 cache entries  
                ByteCodeOp.LOAD_ATTR => 9,
                // opcode 107 (COMPARE_OP) - 1 cache entry (CPython 3.12 verified)
                ByteCodeOp.COMPARE_OP => 1,
                // opcode 108 (IS_OP) - 0 cache entries (CPython 3.12 verified)
                ByteCodeOp.IS_OP => 0,
                // opcode 109 (CONTAINS_OP) - 0 cache entries (CPython 3.12 verified)
                ByteCodeOp.CONTAINS_OP => 0,
                // opcode 119 (BINARY_OP) - 1 cache entry
                ByteCodeOp.BINARY_OP => 1,
                // opcode 168 (CALL) - 3 cache entries
                ByteCodeOp.CALL => 3,
                // 나머지는 인라인 캐시 없음
                _ => 0
            };
        }

        /// <summary>
        /// CPython 3.12 호환 JUMP_BACKWARD oparg 계산 (컴파일 시 사용)
        /// 공식: oparg = (next_instr_position - target_position) / 2
        /// </summary>
        public static int CalculateJumpBackwardOpArg(int currentInstrPos, int targetInstrPos, List<ByteCodeInstruction> instructions)
        {
            // CPython 3.12: 최적화 사용 시 instruction 단위 계산
            if (SharpPyConfig._enable_optimizer)
            {
                // oparg = current_position - target_position
                // VM: target = current - oparg 이므로
                // 컴파일: oparg = current - target
                return currentInstrPos - targetInstrPos;
            }
            else
            {
                // 최적화 비활성화: 바이트 기반 계산
                // next_instr_position: JUMP_BACKWARD 다음 명령어의 바이트 오프셋
                int currentByteOffset = CalculateByteOffset(currentInstrPos, instructions);
                currentByteOffset += 2; // JUMP_BACKWARD 명령어 자체 크기

                // target_position: 점프할 타겟의 바이트 오프셋
                int targetByteOffset = CalculateByteOffset(targetInstrPos, instructions);

                // CPython 3.12 공식: oparg = (next_instr_position - target_position) / 2
                return (currentByteOffset - targetByteOffset) / 2;
            }
        }

        /// <summary>
        /// CPython 3.12 호환 FOR_ITER 타겟 위치 계산 (VM 실행 시 사용)
        /// QuickenedCodeObject 지원을 위한 통합 메서드
        /// </summary>
        public static int CalculateForIterTarget(int currentInstrPos, int opArg, PyCodeObject codeObject)
        {
            if (codeObject is PyQuickenedCodeObject quickenedCode)
            {
                // CPython 3.12 Quickened Code: instruction offset 사용
                int targetIndex = quickenedCode.CalculateForIterTarget(currentInstrPos, opArg);

#if DEBUG_LOG
                Console.WriteLine($"🔍 FOR_ITER 타겟 계산 (Quickened): currentInstr={currentInstrPos}, opArg={opArg}");
                Console.WriteLine($"    targetIndex={targetIndex}");
#endif
                return targetIndex;
            }
            else
            {
                // 일반 CodeObject: byte offset 사용
                int currentByteOffset = CalculateByteOffset(currentInstrPos, codeObject.Instructions);
                int targetByteOffset = currentByteOffset + (opArg * 2);

#if DEBUG_LOG
                Console.WriteLine($"🔍 FOR_ITER 타겟 계산 (바이트): currentInstr={currentInstrPos}, opArg={opArg}");
                Console.WriteLine($"    currentByteOffset={currentByteOffset}, targetByteOffset={targetByteOffset}");
#endif

                int targetIndex = ByteOffsetToInstructionIndex(targetByteOffset, codeObject.Instructions);
#if DEBUG_LOG
                Console.WriteLine($"    targetIndex={targetIndex}");
#endif
                return targetIndex;
            }
        }

        /// <summary>
        /// 레거시 호환성을 위한 메서드 (List<ByteCodeInstruction> 사용)
        /// </summary>
        public static int CalculateForIterTarget(int currentInstrPos, int opArg, List<ByteCodeInstruction> instructions)
        {
            // 레거시: SharpPyConfig._enable_optimizer 조건 사용
            if (SharpPyConfig._enable_optimizer)
            {
                // CPython 3.12 최적화 모드: instruction 단위 계산
                int targetIndex = currentInstrPos + opArg;

#if DEBUG_LOG
                Console.WriteLine($"🔍 FOR_ITER 타겟 계산 (레거시 최적화): currentInstr={currentInstrPos}, opArg={opArg}");
                Console.WriteLine($"    targetIndex={targetIndex}");
#endif
                return targetIndex;
            }
            else
            {
                // 최적화 비활성화: 바이트 단위 계산
                int currentByteOffset = CalculateByteOffset(currentInstrPos, instructions);
                int targetByteOffset = currentByteOffset + (opArg * 2);

#if DEBUG_LOG
                Console.WriteLine($"🔍 FOR_ITER 타겟 계산 (레거시 바이트): currentInstr={currentInstrPos}, opArg={opArg}");
                Console.WriteLine($"    currentByteOffset={currentByteOffset}, targetByteOffset={targetByteOffset}");
#endif

                int targetIndex = ByteOffsetToInstructionIndex(targetByteOffset, instructions);
#if DEBUG_LOG
                Console.WriteLine($"    targetIndex={targetIndex}");
#endif
                return targetIndex;
            }
        }

        /// <summary>
        /// CPython 3.12 호환 JUMP_BACKWARD 타겟 위치 계산 (VM 실행 시 사용)
        /// QuickenedCodeObject 지원을 위한 통합 메서드
        /// </summary>
        public static int CalculateJumpBackwardTarget(int currentInstrPos, int opArg, PyCodeObject codeObject)
        {
            if (codeObject is PyQuickenedCodeObject quickenedCode)
            {
                // CPython 3.12 Quickened Code: instruction offset 사용
                int targetIndex = quickenedCode.CalculateJumpBackwardTarget(currentInstrPos, opArg);

#if DEBUG_LOG
                Console.WriteLine($"🔍 JUMP_BACKWARD 타겟 계산 (Quickened): currentInstr={currentInstrPos}, opArg={opArg}");
                Console.WriteLine($"    targetIndex={targetIndex}");
#endif
                return targetIndex;
            }
            else
            {
                // 일반 CodeObject: byte offset 사용
                int currentByteOffset = CalculateByteOffset(currentInstrPos, codeObject.Instructions);
                int targetByteOffset = currentByteOffset + 2 - (opArg * 2);

#if DEBUG_LOG
                Console.WriteLine($"🔍 JUMP_BACKWARD 타겟 계산 (바이트): currentInstr={currentInstrPos}, opArg={opArg}");
                Console.WriteLine($"    currentByteOffset={currentByteOffset}, targetByteOffset={targetByteOffset}");
#endif

                int targetIndex = ByteOffsetToInstructionIndex(targetByteOffset, codeObject.Instructions);
#if DEBUG_LOG
                Console.WriteLine($"    targetIndex={targetIndex}");
#endif
                return targetIndex;
            }
        }

        /// <summary>
        /// 레거시 호환성을 위한 메서드 (List<ByteCodeInstruction> 사용)
        /// CPython: next_instr -= oparg (where oparg is instruction units)
        /// </summary>
        public static int CalculateJumpBackwardTarget(int currentInstrPos, int opArg, List<ByteCodeInstruction> instructions)
        {
            // 최적화된 코드에서는 instruction 단위 계산
            if (SharpPyConfig._enable_optimizer)
            {
                // CPython 3.12 최적화 모드: instruction 단위 계산
                // oparg = current_position - target_position
                // 따라서: target_position = current_position - oparg
                int targetIndex = currentInstrPos - opArg;

#if DEBUG_LOG
                Console.WriteLine($"🔍 JUMP_BACKWARD 타겟 계산 (최적화): currentInstr={currentInstrPos}, opArg={opArg}");
                Console.WriteLine($"    targetIndex={targetIndex}");
#endif
                return targetIndex;
            }
            else
            {
                // 최적화 비활성화: 기존 바이트 단위 계산
                int currentByteOffset = CalculateByteOffset(currentInstrPos, instructions);
                int targetByteOffset = currentByteOffset + 2 - (opArg * 2);

#if DEBUG_LOG
                Console.WriteLine($"🔍 JUMP_BACKWARD 타겟 계산 (바이트): currentInstr={currentInstrPos}, opArg={opArg}");
                Console.WriteLine($"    currentByteOffset={currentByteOffset}, targetByteOffset={targetByteOffset}");
#endif

                int targetIndex = ByteOffsetToInstructionIndex(targetByteOffset, instructions);
#if DEBUG_LOG
                Console.WriteLine($"    targetIndex={targetIndex}");
#endif
                return targetIndex;
            }
        }

        /// <summary>
        /// CPython 3.12 호환: QuickenedCodeObject를 위한 오프셋 변환
        /// QuickenedCodeObject일 때는 바이트 오프셋 대신 instruction offset 직접 사용
        /// </summary>
        public static int ByteOffsetToInstructionIndex(int targetByteOffset, PyCodeObject codeObject)
        {
            if (codeObject is PyQuickenedCodeObject)
            {
                // Quickened Code: targetByteOffset는 실제로는 instruction offset
                // 바이트 오프셋 계산을 우회하고 직접 instruction index 반환
                int instructionOffset = targetByteOffset / 2; // 대부분의 instruction은 2바이트

#if DEBUG_LOG
                Console.WriteLine($"🔍 QuickenedCode: 바이트 오프셋 {targetByteOffset} → instruction index {instructionOffset}");
#endif
                return Math.Max(0, Math.Min(instructionOffset, codeObject.Instructions.Count - 1));
            }
            else
            {
                // 일반 CodeObject: 기존 바이트 오프셋 계산 사용
                return ByteOffsetToInstructionIndex(targetByteOffset, codeObject.Instructions);
            }
        }

        /// <summary>
        /// 레거시 바이트 오프셋을 instruction index로 변환 (기존 방식)
        /// </summary>
        public static int ByteOffsetToInstructionIndex(int targetByteOffset, List<ByteCodeInstruction> instructions)
        {
            int currentOffset = 0;
            
            for (int i = 0; i < instructions.Count; i++)
            {
                if (currentOffset == targetByteOffset)
                {
                    return i;
                }
                
                if (currentOffset > targetByteOffset)
                {
                    // 정확한 instruction 경계가 아닌 경우, 디버그를 위해 에러 발생
#if DEBUG_LOG
                    Console.WriteLine($"❌ 바이트 오프셋 계산 오류: targetByteOffset={targetByteOffset}, currentOffset={currentOffset}");
                    Console.WriteLine($"🎯 가장 가까운: 오프셋 {currentOffset - GetCPythonInstructionSize(instructions[i-1].OpCode, instructions[i-1].Argument)} → instruction {i-1} ({instructions[i-1].OpCode})");

                    // 디버그: 모든 instruction의 바이트 오프셋 출력
                    Console.WriteLine("📋 전체 instruction 바이트 오프셋:");
                    int debugOffset = 0;
                    for (int j = 0; j < Math.Min(instructions.Count, i + 3); j++)
                    {
                        var inst = instructions[j];
                        int size = GetCPythonInstructionSize(inst.OpCode, inst.Argument);
                        Console.WriteLine($"    [{j}] {inst.OpCode} (arg={inst.Argument}) → 오프셋 {debugOffset} (크기 {size})");
                        debugOffset += size;
                    }
#endif
                    
                    throw new InvalidOperationException($"JUMP_BACKWARD: 바이트 오프셋 {targetByteOffset}에 정확한 instruction이 없음!");
                }
                
                currentOffset += GetCPythonInstructionSize(instructions[i].OpCode, instructions[i].Argument);
            }
            
            return Math.Max(0, instructions.Count - 1);
        }
    }
}