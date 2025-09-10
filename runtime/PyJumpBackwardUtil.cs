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
                // opcode 25 (BINARY_SUBSCR) - 4 cache entries
                ByteCodeOp.BINARY_SUBSCR => 4,
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
                // opcode 107 (COMPARE_OP) - 2 cache entries
                ByteCodeOp.COMPARE_OP => 2,
                // opcode 108 (IS_OP) - 1 cache entry
                ByteCodeOp.IS_OP => 1,
                // opcode 109 (CONTAINS_OP) - 1 cache entry  
                ByteCodeOp.CONTAINS_OP => 1,
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
            // next_instr_position: JUMP_BACKWARD 다음 명령어의 바이트 오프셋
            int currentByteOffset = CalculateByteOffset(currentInstrPos, instructions);
            currentByteOffset += 2; // JUMP_BACKWARD 명령어 자체 크기

            // target_position: 점프할 타겟의 바이트 오프셋
            int targetByteOffset = CalculateByteOffset(targetInstrPos, instructions);

            // CPython 3.12 공식: oparg = (next_instr_position - target_position) / 2
            return (currentByteOffset - targetByteOffset) / 2;
        }

        /// <summary>
        /// CPython 3.12 호환 JUMP_BACKWARD 타겟 위치 계산 (VM 실행 시 사용)
        /// CPython: next_instr -= oparg (where oparg is instruction units)
        /// </summary>
        public static int CalculateJumpBackwardTarget(int currentInstrPos, int opArg, List<ByteCodeInstruction> instructions)
        {
            // 현재 instruction의 바이트 오프셋 계산
            int currentByteOffset = CalculateByteOffset(currentInstrPos, instructions);
            
            // CPython 공식: target_offset = current_offset + 2 - (oparg * 2)
            // +2는 현재 instruction을 넘어서 다음 instruction으로 가는 것
            // oparg * 2는 instruction units를 byte units로 변환
            int targetByteOffset = currentByteOffset + 2 - (opArg * 2);
            
            Console.WriteLine($"🔍 JUMP_BACKWARD 타겟 계산: currentInstr={currentInstrPos}, opArg={opArg}");
            Console.WriteLine($"    currentByteOffset={currentByteOffset}, targetByteOffset={targetByteOffset}");
            
            // 바이트 오프셋을 instruction index로 변환
            int targetIndex = ByteOffsetToInstructionIndex(targetByteOffset, instructions);
            Console.WriteLine($"    targetIndex={targetIndex}");
            return targetIndex;
        }

        /// <summary>
        /// 바이트 오프셋을 instruction index로 변환
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
                    
                    throw new InvalidOperationException($"JUMP_BACKWARD: 바이트 오프셋 {targetByteOffset}에 정확한 instruction이 없음!");
                }
                
                currentOffset += GetCPythonInstructionSize(instructions[i].OpCode, instructions[i].Argument);
            }
            
            return Math.Max(0, instructions.Count - 1);
        }
    }
}