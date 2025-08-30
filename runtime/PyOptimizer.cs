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
                originalCode.CellVars
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
                    IsBinaryOperation(inst3.OpCode))
                {
                    var result = EvaluateConstantOperation(
                        _constants[inst1.Argument],
                        _constants[inst2.Argument],
                        inst3.OpCode
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
                if (inst1.OpCode == ByteCodeOp.LOAD_CONST &&
                    inst2.OpCode == ByteCodeOp.POP_TOP)
                {
                    _instructions[i] = new ByteCodeInstruction(ByteCodeOp.NOP, 0);
                    _instructions[i + 1] = new ByteCodeInstruction(ByteCodeOp.NOP, 0);
                    Console.WriteLine("🔄 Peephole: 불필요한 LOAD_CONST+POP_TOP 제거");
                }
            }

            // NOP 명령어들 완전 제거
            _instructions.RemoveAll(inst => inst.OpCode == ByteCodeOp.NOP);
        }

        /// <summary>
        /// 바이너리 연산인지 확인
        /// </summary>
        private bool IsBinaryOperation(ByteCodeOp opCode)
        {
            return opCode >= ByteCodeOp.BINARY_ADD && opCode <= ByteCodeOp.BINARY_MATRIX_MULTIPLY;
        }

        /// <summary>
        /// 상수 연산 평가 (컴파일 타임에 계산)
        /// </summary>
        private PyObject EvaluateConstantOperation(PyObject left, PyObject right, ByteCodeOp operation)
        {
            try
            {
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
    }
}