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

        public ByteCodeOptimizer(bool enableOptimization = true)
        {
            _optimizationEnabled = enableOptimization;
        }

        /// <summary>
        /// 바이트코드 최적화 메인 함수
        /// </summary>
        public PyCodeObject OptimizeCode(PyCodeObject originalCode)
        {
            if (!_optimizationEnabled)
                return originalCode;

            Console.WriteLine("\n🔧 바이트코드 최적화 시작");
            
            _instructions = new List<ByteCodeInstruction>(originalCode.Instructions);
            _constants = new List<PyObject>(originalCode.Constants);
            _names = new List<string>(originalCode.Names);

            int originalCount = _instructions.Count;

            // 1. 상수 접기 최적화
            ApplyConstantFolding();

            // 2. 중복 로드 제거
            ApplyRedundantLoadElimination();

            // 3. 무용 코드 제거
            ApplyDeadCodeElimination();

            // 4. Peephole 패턴 최적화
            ApplyPeepholeOptimizations();

            // 5. CPython 3.12 Superinstructions 생성
            ApplySuperinstructions();

            // 6. 점프 오프셋 재계산 (최적화로 인한 명령어 위치 변경 반영)
            RecalculateJumpOffsets();

            int optimizedCount = _instructions.Count;
            int saved = originalCount - optimizedCount;
            
            Console.WriteLine($"✅ 최적화 완료: {originalCount} → {optimizedCount} ({saved} 명령어 절약, {(float)saved/originalCount*100:F1}% 개선)");

            return new PyCodeObject(
                originalCode.Name,
                _instructions,
                _constants,
                _names,
                originalCode.VarNames,
                originalCode.ArgCount,
                originalCode.FreeVars,
                originalCode.CellVars,
                originalCode.DefaultValues,
                originalCode.Flags
            );
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
                        int constIndex = AddConstant(result);
                        
                        // 3개 명령어를 1개로 교체
                        _instructions[i] = new ByteCodeInstruction(ByteCodeOp.LOAD_CONST, constIndex);
                        _instructions.RemoveAt(i + 1);
                        _instructions.RemoveAt(i + 1); // RemoveAt 후 인덱스가 변경됨
                        
                        Console.WriteLine($"🔄 상수 접기: {inst1.OpCode}+{inst2.OpCode}+{inst3.OpCode} → LOAD_CONST({result})");
                        i--; // 다음 반복에서 현재 위치부터 다시 검사
                    }
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

                // 패턴: LOAD_NAME x, LOAD_NAME x → LOAD_NAME x, DUP_TOP
                if (inst1.OpCode == ByteCodeOp.LOAD_NAME &&
                    inst2.OpCode == ByteCodeOp.LOAD_NAME &&
                    inst1.Argument == inst2.Argument)
                {
                    _instructions[i + 1] = new ByteCodeInstruction(ByteCodeOp.DUP_TOP, 0);
                    Console.WriteLine($"🔄 중복 로드 제거: LOAD_NAME({_names[inst1.Argument]}) 중복 → DUP_TOP");
                }

                // 패턴: LOAD_CONST x, LOAD_CONST x → LOAD_CONST x, DUP_TOP
                if (inst1.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst2.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst1.Argument == inst2.Argument)
                {
                    _instructions[i + 1] = new ByteCodeInstruction(ByteCodeOp.DUP_TOP, 0);
                    Console.WriteLine($"🔄 중복 상수 로드 제거: LOAD_CONST 중복 → DUP_TOP");
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
                    _instructions[i] = new ByteCodeInstruction(ByteCodeOp.NOP, 0);
                    _instructions[i + 1] = new ByteCodeInstruction(ByteCodeOp.NOP, 0);
                    Console.WriteLine("🔄 무용 코드 제거: if True 최적화");
                }

                // 패턴: LOAD_CONST False, POP_JUMP_IF_TRUE → NOP (항상 거짓이므로 점프하지 않음)
                if (inst1.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst2.OpCode == ByteCodeOp.POP_JUMP_IF_TRUE &&
                    _constants[inst1.Argument] == PyBool.False)
                {
                    _instructions[i] = new ByteCodeInstruction(ByteCodeOp.NOP, 0);
                    _instructions[i + 1] = new ByteCodeInstruction(ByteCodeOp.NOP, 0);
                    Console.WriteLine("🔄 무용 코드 제거: if False 최적화");
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

                // 패턴: LOAD_CONST, POP_TOP → NOP (상수 로드 후 즉시 버림)
                // 임시 비활성화: 라벨 참조 버그로 인해 점프 주소 계산 오류 발생
                /*if (inst1.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst2.OpCode == ByteCodeOp.POP_TOP)
                {
                    _instructions[i] = new ByteCodeInstruction(ByteCodeOp.NOP, 0);
                    _instructions[i + 1] = new ByteCodeInstruction(ByteCodeOp.NOP, 0);
                    Console.WriteLine("🔄 Peephole: 불필요한 LOAD_CONST+POP_TOP 제거");
                }*/
            }

            // NOP 명령어들 완전 제거
            _instructions.RemoveAll(inst => inst.OpCode == ByteCodeOp.NOP);
        }

        /// <summary>
        /// 바이너리 연산인지 확인 (CPython 3.12+ BINARY_OP 및 기존 개별 OpCode 지원)
        /// </summary>
        private bool IsBinaryOpType(ByteCodeOp opCode)
        {
            return opCode == ByteCodeOp.BINARY_OP || 
                   (opCode >= ByteCodeOp.BINARY_ADD && opCode <= ByteCodeOp.BINARY_MATRIX_MULTIPLY);
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
                
                // Handle legacy individual binary opcodes (backward compatibility)
                return operation switch
                {
                    ByteCodeOp.BINARY_ADD => left.Add(right),
                    ByteCodeOp.BINARY_SUBTRACT => left.Subtract(right),
                    ByteCodeOp.BINARY_MULTIPLY => left.Multiply(right),
                    ByteCodeOp.BINARY_DIVIDE => left.Divide(right),
                    ByteCodeOp.BINARY_FLOOR_DIVIDE => left.FloorDivide(right),
                    ByteCodeOp.BINARY_MODULO => left.Modulo(right),
                    ByteCodeOp.BINARY_POWER => left.Power(right),
                    _ => null
                };
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
        private int AddConstant(PyObject constant)
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
            Console.WriteLine("🚀 CPython 3.12 Superinstructions 최적화 적용");
            
            for (int i = 0; i < _instructions.Count - 1; i++)
            {
                var inst1 = _instructions[i];
                var inst2 = _instructions[i + 1];

                // 패턴 1: LOAD_FAST + LOAD_FAST → LOAD_FAST_LOAD_FAST
                if (inst1.OpCode == ByteCodeOp.LOAD_FAST && 
                    inst2.OpCode == ByteCodeOp.LOAD_FAST)
                {
                    int combinedArg = inst1.Argument | (inst2.Argument << 16);
                    _instructions[i] = new ByteCodeInstruction(ByteCodeOp.LOAD_FAST_LOAD_FAST, combinedArg);
                    _instructions.RemoveAt(i + 1);
                    Console.WriteLine($"  ✅ LOAD_FAST + LOAD_FAST → LOAD_FAST_LOAD_FAST at {i}");
                    continue; // 재검사를 위해 i를 증가시키지 않음
                }

                // 패턴 2: LOAD_CONST + LOAD_FAST → LOAD_CONST_LOAD_FAST
                if (inst1.OpCode == ByteCodeOp.LOAD_CONST && 
                    inst2.OpCode == ByteCodeOp.LOAD_FAST)
                {
                    int combinedArg = inst1.Argument | (inst2.Argument << 16);
                    _instructions[i] = new ByteCodeInstruction(ByteCodeOp.LOAD_CONST_LOAD_FAST, combinedArg);
                    _instructions.RemoveAt(i + 1);
                    Console.WriteLine($"  ✅ LOAD_CONST + LOAD_FAST → LOAD_CONST_LOAD_FAST at {i}");
                    continue;
                }

                // 패턴 3: STORE_FAST + LOAD_FAST → STORE_FAST_LOAD_FAST
                if (inst1.OpCode == ByteCodeOp.STORE_FAST && 
                    inst2.OpCode == ByteCodeOp.LOAD_FAST)
                {
                    int combinedArg = inst1.Argument | (inst2.Argument << 16);
                    _instructions[i] = new ByteCodeInstruction(ByteCodeOp.STORE_FAST_LOAD_FAST, combinedArg);
                    _instructions.RemoveAt(i + 1);
                    Console.WriteLine($"  ✅ STORE_FAST + LOAD_FAST → STORE_FAST_LOAD_FAST at {i}");
                    continue;
                }

                // 패턴 4: STORE_FAST + STORE_FAST → STORE_FAST_STORE_FAST
                if (inst1.OpCode == ByteCodeOp.STORE_FAST && 
                    inst2.OpCode == ByteCodeOp.STORE_FAST)
                {
                    int combinedArg = inst1.Argument | (inst2.Argument << 16);
                    _instructions[i] = new ByteCodeInstruction(ByteCodeOp.STORE_FAST_STORE_FAST, combinedArg);
                    _instructions.RemoveAt(i + 1);
                    Console.WriteLine($"  ✅ STORE_FAST + STORE_FAST → STORE_FAST_STORE_FAST at {i}");
                    continue;
                }
            }
        }

        /// <summary>
        /// 최적화 후 점프 오프셋 재계산 - 특정 패턴의 올바른 타겟을 찾아 수정
        /// </summary>
        private void RecalculateJumpOffsets()
        {
            Console.WriteLine("🔄 점프 오프셋 재계산 중...");
            int recalculated = 0;
            
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
                            
                            // 현재 JUMP_BACKWARD가 FOR_ITER로 점프하는지 확인
                            int currentTarget = jumpPos - currentOffset - 1;
                            
                            // 올바른 타겟은 FOR_ITER 위치여야 함  
                            // VM에서 실행 시 InstructionPointer가 이미 증가된 상태이므로 추가 보정
                            int correctOffset = jumpPos - forIterPos;
                            
                            if (currentOffset != correctOffset)
                            {
                                _instructions[j] = new ByteCodeInstruction(ByteCodeOp.JUMP_BACKWARD, correctOffset);
                                Console.WriteLine($"  🔧 FOR 루프 JUMP_BACKWARD[{j}]: {currentOffset} → {correctOffset} (FOR_ITER: {forIterPos})");
                                recalculated++;
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
                    for (int j = forIterPos + 1; j < _instructions.Count; j++)
                    {
                        var laterInst = _instructions[j];
                        
                        if (laterInst.OpCode == ByteCodeOp.POP_JUMP_IF_FALSE)
                        {
                            int jumpPos = j;
                            int currentTarget = laterInst.Argument;
                            
                            // 조건부 점프 타겟 재계산 - 더 관대한 조건으로 수정
                            bool shouldPointToForIter = false;
                            
                            // 1. 기존 조건: FOR_ITER + 1을 가리키는 경우
                            if (currentTarget == forIterPos + 1)
                            {
                                shouldPointToForIter = true;
                            }
                            // 2. FOR_ITER 근처를 가리키는 경우 (최적화로 인한 위치 변경)
                            else if (currentTarget >= forIterPos - 3 && currentTarget <= forIterPos + 5)
                            {
                                shouldPointToForIter = true;
                            }
                            // 3. 자기 자신 근처를 가리켜 무한 루프를 만드는 경우
                            else if (currentTarget >= jumpPos - 5 && currentTarget <= jumpPos + 2)
                            {
                                shouldPointToForIter = true;
                            }
                            
                            if (shouldPointToForIter)
                            {
                                _instructions[j] = new ByteCodeInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, forIterPos);
                                Console.WriteLine($"  🔧 조건부 점프 POP_JUMP_IF_FALSE[{j}]: {currentTarget} → {forIterPos} (FOR_ITER)");
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
            
            Console.WriteLine("🔄 FOR_ITER → END_FOR 점프 오프셋 재계산 중...");
            for (int i = 0; i < _instructions.Count; i++)
            {
                var instruction = _instructions[i];
                if (instruction.OpCode == ByteCodeOp.FOR_ITER)
                {
                    // FOR_ITER에서 대응하는 END_FOR 찾기
                    int targetEndFor = FindMatchingEndFor(i);
                    if (targetEndFor >= 0)
                    {
                        int newOffset = targetEndFor - i - 1;
                        if (newOffset != instruction.Argument)
                        {
                            _instructions[i] = new ByteCodeInstruction(ByteCodeOp.FOR_ITER, newOffset);
                            Console.WriteLine($"  🔧 FOR_ITER[{i}]: 오프셋 {instruction.Argument} → {newOffset} (END_FOR at {targetEndFor})");
                            recalculated++;
                        }
                    }
                }
            }

            // 중첩 루프 JUMP_BACKWARD 재계산 추가
            Console.WriteLine("🔄 중첩 루프 JUMP_BACKWARD → FOR_ITER 점프 오프셋 재계산 중...");
            for (int i = 0; i < _instructions.Count; i++)
            {
                var instruction = _instructions[i];
                if (instruction.OpCode == ByteCodeOp.JUMP_BACKWARD)
                {
                    // JUMP_BACKWARD에서 올바른 FOR_ITER 타겟 찾기
                    int targetForIter = FindMatchingForIter(i);
                    if (targetForIter >= 0)
                    {
                        int currentOffset = instruction.Argument;
                        int correctOffset = i - targetForIter;
                        
                        if (currentOffset != correctOffset)
                        {
                            _instructions[i] = new ByteCodeInstruction(ByteCodeOp.JUMP_BACKWARD, correctOffset);
                            Console.WriteLine($"  🔧 중첩 JUMP_BACKWARD[{i}]: 오프셋 {currentOffset} → {correctOffset} (FOR_ITER at {targetForIter})");
                            recalculated++;
                        }
                    }
                }
            }
            
            if (recalculated > 0)
            {
                Console.WriteLine($"✅ 점프 오프셋 재계산 완료: {recalculated}개 명령어 수정");
            }
            else
            {
                Console.WriteLine("✅ 점프 오프셋 재계산 완료: 수정 필요 없음");
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
            int nestedLevel = 0;
            
            for (int i = forIterPos + 1; i < _instructions.Count; i++)
            {
                var instruction = _instructions[i];
                
                if (instruction.OpCode == ByteCodeOp.FOR_ITER)
                {
                    nestedLevel++;
                }
                else if (instruction.OpCode == ByteCodeOp.END_FOR)
                {
                    if (nestedLevel == 0)
                    {
                        // 이것이 매칭되는 END_FOR
                        return i;
                    }
                    nestedLevel--;
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
    }
}