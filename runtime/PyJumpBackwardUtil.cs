using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 호환 JUMP_BACKWARD 유틸리티 클래스
    ///
    /// 🎯 **목적**: 모든 JUMP_BACKWARD 관련 계산을 통합 관리하여 일관성과 정확성을 보장
    ///
    /// 🔧 **주요 기능**:
    /// - CPython 3.12와 동일한 바이트 오프셋 계산
    /// - 인라인 캐시 엔트리를 고려한 정확한 instruction 크기 계산
    /// - 컴파일 시와 실행 시 JUMP_BACKWARD oparg 계산
    /// - QuickenedCodeObject와 일반 CodeObject 통합 지원
    ///
    /// 🧮 **계산 방식**:
    /// - **컴파일 시**: oparg = (next_instr_position - target_position) / 2
    /// - **실행 시**: target = current_position - oparg (instruction 단위)
    ///
    /// 📋 **CPython 3.12 호환성**:
    /// - 항상 바이트 단위 계산 (instruction 단위 X)
    /// - 인라인 캐시 엔트리 정확히 반영
    /// - Exception table과 올바른 연동
    ///
    /// 🎯 **사용 사례**:
    /// - List/Dict/Set comprehensions의 중첩 루프
    /// - While/For 루프의 JUMP_BACKWARD
    /// - FOR_ITER과 JUMP_BACKWARD의 정확한 매칭
    /// </summary>
    public static class PyJumpBackwardUtil
    {
        /// <summary>
        /// CPython 3.12 호환 바이트 오프셋 계산
        ///
        /// 🎯 **목적**: 각 instruction의 정확한 바이트 크기를 계산하여 누적
        ///
        /// 📐 **계산 방식**:
        /// - 각 instruction의 기본 크기 (2바이트) + 인라인 캐시 엔트리 크기
        /// - BINARY_SUBSCR = 2 + (1 * 2) = 4바이트
        /// - LOAD_ATTR = 2 + (9 * 2) = 20바이트
        /// - 일반 instruction = 2바이트
        ///
        /// 📋 **사용 예시**:
        /// ```
        /// instructions: [LOAD_NAME, BINARY_SUBSCR, STORE_FAST]
        /// instructionIndex=2 요청 시:
        /// - LOAD_NAME: 2바이트 (offset 0-1)
        /// - BINARY_SUBSCR: 4바이트 (offset 2-5)
        /// - 결과: 6 (STORE_FAST의 시작 오프셋)
        /// ```
        ///
        /// ⚠️ **주의사항**: instructionIndex는 0-based, 해당 instruction 이전까지의 누적 크기 반환
        /// </summary>
        /// <param name="instructionIndex">계산할 instruction의 인덱스 (0-based)</param>
        /// <param name="instructions">전체 instruction 리스트</param>
        /// <returns>지정된 instruction의 시작 바이트 오프셋</returns>
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
        ///
        /// 🎯 **목적**: 인라인 캐시 엔트리와 Extended args를 모두 고려한 정확한 바이트 크기 반환
        ///
        /// 📐 **계산 공식**: 2 + (cacheEntries * 2)
        /// - 기본 instruction: 2바이트 (opcode + argument)
        /// - 각 캐시 엔트리: 2바이트씩 추가
        ///
        /// 📋 **예시**:
        /// - LOAD_FAST: 2 + (0 * 2) = 2바이트
        /// - BINARY_SUBSCR: 2 + (1 * 2) = 4바이트
        /// - LOAD_ATTR: 2 + (9 * 2) = 20바이트
        /// - CALL: 2 + (3 * 2) = 8바이트
        ///
        /// 🔧 **CPython 호환성**:
        /// - CPython 3.12의 dis._inline_cache_entries와 동일
        /// - Adaptive specialization을 위한 캐시 공간 정확히 반영
        /// </summary>
        /// <param name="op">바이트코드 명령어</param>
        /// <param name="arg">명령어 인수 (현재 사용 안함, 확장성을 위해 유지)</param>
        /// <returns>해당 instruction의 총 바이트 크기</returns>
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
        ///
        /// 🎯 **목적**: 컴파일 시점에서 JUMP_BACKWARD 명령어의 oparg 값 계산
        ///
        /// 🧮 **CPython 3.12 공식**: oparg = (next_instr_position - target_position) / 2
        /// - next_instr_position: JUMP_BACKWARD 다음 명령어의 바이트 오프셋
        /// - target_position: 점프할 대상의 바이트 오프셋
        /// - 나누기 2: 바이트 단위를 instruction 단위로 변환
        ///
        /// 📋 **실행 흐름 예시**:
        /// ```
        /// 0: FOR_ITER 10        # target (targetInstrPos=0)
        /// 2: LOAD_FAST 0
        /// 4: STORE_FAST 1
        /// 6: JUMP_BACKWARD ?    # current (currentInstrPos=3)
        /// 8: END_FOR            # next_instr
        ///
        /// 계산:
        /// - currentByteOffset = 6 (JUMP_BACKWARD 위치)
        /// - nextInstrByteOffset = 6 + 2 = 8
        /// - targetByteOffset = 0 (FOR_ITER 위치)
        /// - oparg = (8 - 0) / 2 = 4
        /// ```
        ///
        /// 🔧 **중요 사항**:
        /// - 항상 바이트 오프셋 기반 계산 (instruction 단위 X)
        /// - 인라인 캐시 엔트리 정확히 반영
        /// - QuickenedCodeObject와 일반 CodeObject 모두 동일한 방식
        ///
        /// 🎯 **사용 케이스**:
        /// - List/Dict/Set comprehensions의 JUMP_BACKWARD
        /// - While/For 루프의 JUMP_BACKWARD
        /// - 중첩 comprehensions의 정확한 오프셋 계산
        /// </summary>
        /// <param name="currentInstrPos">JUMP_BACKWARD instruction의 위치 (instruction index)</param>
        /// <param name="targetInstrPos">점프할 대상의 위치 (instruction index)</param>
        /// <param name="instructions">전체 instruction 리스트</param>
        /// <returns>JUMP_BACKWARD의 oparg 값</returns>
        public static int CalculateJumpBackwardOpArg(int currentInstrPos, int targetInstrPos, List<ByteCodeInstruction> instructions)
        {
            // CPython 3.12: 항상 바이트 오프셋 기반 계산 (instruction 단위가 아님)
            // next_instr_position: JUMP_BACKWARD 다음 명령어의 바이트 오프셋
            int currentByteOffset = CalculateByteOffset(currentInstrPos, instructions);
            currentByteOffset += 2; // JUMP_BACKWARD 명령어 자체 크기

            // target_position: 점프할 타겟의 바이트 오프셋
            int targetByteOffset = CalculateByteOffset(targetInstrPos, instructions);

            // CPython 3.12 공식: oparg = (next_instr_position - target_position) / 2
            return (currentByteOffset - targetByteOffset) / 2;
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
        /// 레거시 호환성을 위한 메서드 (List&lt;ByteCodeInstruction&gt; 사용)
        ///
        /// 🎯 **목적**: VM 실행 시 JUMP_BACKWARD의 타겟 위치 계산 (레거시 인터페이스)
        ///
        /// 🧮 **CPython 3.12 계산 방식**: next_instr -= oparg (instruction 단위)
        /// - oparg는 instruction 단위로 해석됨
        /// - target_position = current_position - oparg
        ///
        /// 📋 **실행 흐름 예시**:
        /// ```
        /// VM이 JUMP_BACKWARD 4를 실행할 때:
        /// - currentInstrPos = 10 (현재 위치)
        /// - opArg = 4
        /// - target = 10 - 4 = 6 (점프할 위치)
        /// ```
        ///
        /// 🔧 **최적화 모드 차이**:
        /// - **최적화 ON**: instruction 단위 계산 (빠름)
        /// - **최적화 OFF**: 바이트 단위 계산 후 변환 (정확함)
        ///
        /// 🎯 **사용 케이스**:
        /// - QuickenedCodeObject를 사용하지 않는 경우
        /// - 기존 코드와의 호환성 유지
        /// - 테스트 및 디버깅 목적
        ///
        /// ⚠️ **주의사항**: 새 코드에서는 PyCodeObject 버전을 사용 권장
        /// </summary>
        /// <param name="currentInstrPos">현재 JUMP_BACKWARD instruction의 위치</param>
        /// <param name="opArg">JUMP_BACKWARD의 oparg 값</param>
        /// <param name="instructions">전체 instruction 리스트</param>
        /// <returns>점프할 타겟의 instruction index</returns>
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
        ///
        /// 🎯 **목적**: 바이트 오프셋을 instruction index로 정확히 변환
        ///
        /// 🔍 **탐색 알고리즘**:
        /// - 각 instruction의 바이트 크기를 순차적으로 누적
        /// - targetByteOffset과 정확히 일치하는 instruction 찾기
        /// - 경계가 맞지 않으면 오류 발생 (정확성 보장)
        ///
        /// 📋 **예시**:
        /// ```
        /// Instructions: [LOAD_NAME(2), BINARY_SUBSCR(4), STORE_FAST(2)]
        /// Byte offsets: [0, 2, 6, 8]
        ///
        /// targetByteOffset=2 → instruction index 1 (BINARY_SUBSCR)
        /// targetByteOffset=6 → instruction index 2 (STORE_FAST)
        /// targetByteOffset=3 → ERROR (instruction 경계가 아님)
        /// ```
        ///
        /// 🔧 **디버그 기능**:
        /// - DEBUG_LOG 모드에서 상세한 오프셋 정보 출력
        /// - 오류 발생 시 주변 instruction들의 바이트 오프셋 표시
        /// - 정확한 문제 위치 파악 가능
        ///
        /// ⚠️ **오류 조건**:
        /// - targetByteOffset이 instruction 경계와 정확히 일치하지 않음
        /// - instruction 스트림의 무결성 검증 실패
        ///
        /// 🎯 **사용 케이스**:
        /// - JUMP_BACKWARD 타겟 위치 변환
        /// - Exception table의 바이트 오프셋 변환
        /// - 바이트코드 디버깅 및 검증
        /// </summary>
        /// <param name="targetByteOffset">찾을 바이트 오프셋</param>
        /// <param name="instructions">전체 instruction 리스트</param>
        /// <returns>해당 바이트 오프셋의 instruction index</returns>
        /// <exception cref="InvalidOperationException">바이트 오프셋이 instruction 경계와 일치하지 않을 때</exception>
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