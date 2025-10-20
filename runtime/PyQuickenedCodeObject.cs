using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 호환 Quickened Code Object
    /// 최적화된 코드 실행을 위해 instruction offset을 사용하는 코드 객체
    /// CPython 3.12에서 adaptive optimization 시 생성되는 quickened bytecode와 동일한 개념
    /// </summary>
    public class PyQuickenedCodeObject : PyCodeObject
    {
        /// <summary>
        /// 원본 코드 객체 (바이트 오프셋 기준)
        /// </summary>
        public PyCodeObject OriginalCode { get; }

        /// <summary>
        /// Quickened 코드는 항상 instruction offset을 사용
        /// </summary>
        public new bool IsOptimized => true;

        /// <summary>
        /// CPython 3.12 호환 Quickened Code Object 생성자
        /// </summary>
        public PyQuickenedCodeObject(
            PyCodeObject originalCode,
            List<ByteCodeInstruction> quickenedInstructions,
            string name = null,
            List<PyObject> constants = null,
            List<string> names = null,
            List<string> varNames = null,
            int argCount = -1,
            int posonlyArgCount = -1,
            List<string> freeVars = null,
            List<string> cellVars = null,
            List<PyObject> defaultValues = null,
            int flags = -1,
            string fileName = null,
            List<string> sourceLines = null,
            Dictionary<int, int> lineNumberTable = null)
            : base(
                name ?? originalCode.Name,
                quickenedInstructions,
                constants ?? originalCode.Constants,
                names ?? originalCode.Names,
                varNames ?? originalCode.VarNames,
                argCount >= 0 ? argCount : originalCode.ArgCount,
                posonlyArgCount >= 0 ? posonlyArgCount : originalCode.PosonlyArgCount,
                0, // kwonlyArgCount - use 0 as default
                freeVars ?? originalCode.FreeVars,
                cellVars ?? originalCode.CellVars,
                defaultValues ?? originalCode.DefaultValues,
                null, // kwDefaults - use null as default
                flags >= 0 ? flags : originalCode.Flags,
                fileName ?? originalCode.FileName,
                sourceLines ?? originalCode.SourceLines,
                isOptimized: true, // Quickened 코드는 항상 최적화됨
                lineNumberTable ?? originalCode.LineNumberTable,
                new List<ExceptionTableEntry>()) // 빈 exception table로 시작 - 최적화 후 재계산됨
        {
            OriginalCode = originalCode ?? throw new ArgumentNullException(nameof(originalCode));
        }

        /// <summary>
        /// CPython 3.12 호환 점프 타겟 계산
        /// Quickened 코드에서는 항상 instruction offset 사용
        /// </summary>
        public int CalculateJumpTarget(int currentInstrPos, int opArg, bool isForward = true)
        {
            if (isForward)
            {
                // FOR_ITER, POP_JUMP_IF_TRUE 등 전진 점프
                // CPython 3.12 FOR_ITER: JUMPBY(INLINE_CACHE_ENTRIES_FOR_ITER + oparg + 1)
                // INLINE_CACHE_ENTRIES_FOR_ITER = 1 (one CACHE instruction)
                // Jump amount = 1 + opArg + 1 instruction indices
                return currentInstrPos + 1 + opArg + 1; // +1 CACHE + opArg + 1
            }
            else
            {
                // JUMP_BACKWARD 등 후진 점프
                // CPython 3.12 Quickened Code: instruction 단위 계산
                // oparg = current_position - target_position
                // 따라서: target_position = current_position - oparg
                int result = currentInstrPos - opArg;

                #if DEBUG_LOG
                Console.WriteLine($"🔧 CalculateJumpTarget(BACKWARD/Quickened): currentPos={currentInstrPos}, opArg={opArg}");
                Console.WriteLine($"    target={result} (instruction-based)");
                #endif
                return result;
            }
        }

        /// <summary>
        /// FOR_ITER 전용 타겟 계산 (항상 전진 점프)
        /// </summary>
        public int CalculateForIterTarget(int currentInstrPos, int opArg)
        {
            return CalculateJumpTarget(currentInstrPos, opArg, isForward: true);
        }

        /// <summary>
        /// JUMP_BACKWARD 전용 타겟 계산 (항상 후진 점프)
        /// </summary>
        public int CalculateJumpBackwardTarget(int currentInstrPos, int opArg)
        {
            return CalculateJumpTarget(currentInstrPos, opArg, isForward: false);
        }

        /// <summary>
        /// 디버그 정보: Quickened 코드임을 명시
        /// </summary>
        public override string ToString()
        {
            return $"PyQuickenedCodeObject({Name}, {Instructions.Count} instructions, optimized)";
        }
    }
}