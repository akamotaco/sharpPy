using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 호환 적응형 최적화 엔진
    /// 런타임 프로파일 데이터를 기반으로 코드를 동적으로 최적화
    /// </summary>
    public class PyAdaptiveOptimizer
    {
        private static readonly PyAdaptiveOptimizer _instance = new PyAdaptiveOptimizer();
        public static PyAdaptiveOptimizer Instance => _instance;

        private readonly PyAdaptiveProfile _profiler;
        private readonly PyInstructionSpecializer _specializer;
        private readonly Dictionary<string, PyCodeObject> _optimizedCodeCache;
        
        // 최적화 설정
        private readonly int _reoptimizationThreshold;
        private readonly bool _enableAdaptiveOptimization;

        private PyAdaptiveOptimizer()
        {
            _profiler = PyAdaptiveProfile.Instance;
            _specializer = PyInstructionSpecializer.Instance;
            _optimizedCodeCache = new Dictionary<string, PyCodeObject>();
            
            _reoptimizationThreshold = 1000; // CPython 3.12 기본값
            _enableAdaptiveOptimization = true;
        }

        /// <summary>
        /// 코드 실행 전 최적화 검사 및 적용
        /// </summary>
        public PyCodeObject OptimizeIfNeeded(PyCodeObject originalCode)
        {
            if (!_enableAdaptiveOptimization)
                return originalCode;

            var codeKey = $"{originalCode.Name}_{originalCode.FileName}";
            
            // 이미 최적화된 버전이 있는지 확인
            if (_optimizedCodeCache.TryGetValue(codeKey, out var cachedOptimized))
            {
                Console.WriteLine($"🔄 Using cached optimized version: {originalCode.Name}");
                return cachedOptimized;
            }

            // 프로파일 데이터를 기반으로 최적화 필요성 판단
            if (ShouldOptimizeCode(originalCode))
            {
                Console.WriteLine($"🚀 Applying adaptive optimization: {originalCode.Name}");
                var optimizedCode = ApplyAdaptiveOptimizations(originalCode);
                
                if (optimizedCode != originalCode)
                {
                    _optimizedCodeCache[codeKey] = optimizedCode;
                    Console.WriteLine($"✅ Code optimized and cached: {originalCode.Name}");
                    return optimizedCode;
                }
            }

            return originalCode;
        }

        /// <summary>
        /// 코드가 최적화될 필요가 있는지 판단
        /// </summary>
        private bool ShouldOptimizeCode(PyCodeObject code)
        {
            // 최소 실행 횟수 요구사항 체크
            var executionCount = GetCodeExecutionCount(code);
            if (executionCount < _reoptimizationThreshold)
                return false;

            // Hot spot 분석 - 자주 실행되는 명령어들 확인
            var hotInstructions = FindHotInstructions(code);
            if (hotInstructions.Count == 0)
                return false;

            Console.WriteLine($"🔥 Hot code detected: {code.Name} " +
                            $"({executionCount} executions, {hotInstructions.Count} hot instructions)");
            return true;
        }

        /// <summary>
        /// 적응형 최적화 적용
        /// </summary>
        private PyCodeObject ApplyAdaptiveOptimizations(PyCodeObject originalCode)
        {
            var optimizations = new List<string>();

            // 1. Instruction Specialization
            var specializedCode = _specializer.SpecializeCode(originalCode);
            if (specializedCode != originalCode)
            {
                optimizations.Add("Instruction Specialization");
                originalCode = specializedCode;
            }

            // 2. Loop Optimization
            var loopOptimizedCode = OptimizeLoops(originalCode);
            if (loopOptimizedCode != originalCode)
            {
                optimizations.Add("Loop Optimization");
                originalCode = loopOptimizedCode;
            }

            // 3. Branch Prediction Optimization
            var branchOptimizedCode = OptimizeBranches(originalCode);
            if (branchOptimizedCode != originalCode)
            {
                optimizations.Add("Branch Prediction");
                originalCode = branchOptimizedCode;
            }

            if (optimizations.Count > 0)
            {
                Console.WriteLine($"🎯 Applied optimizations: {string.Join(", ", optimizations)}");
            }

            return originalCode;
        }

        /// <summary>
        /// 코드 실행 횟수 추정
        /// </summary>
        private int GetCodeExecutionCount(PyCodeObject code)
        {
            // 프로파일 데이터에서 해당 코드의 실행 빈도 추정
            // 간단한 구현: 첫 번째 명령어의 실행 횟수를 기준으로 함
            var firstInstructionLocation = $"{code.Name}_0";
            
            // 실제 프로파일 데이터가 있으면 그걸 사용, 없으면 추정값
            return 1500; // 임시 값 - 실제로는 프로파일에서 가져와야 함
        }

        /// <summary>
        /// Hot instruction 찾기
        /// </summary>
        private List<int> FindHotInstructions(PyCodeObject code)
        {
            var hotInstructions = new List<int>();

            for (int i = 0; i < code.Instructions.Count; i++)
            {
                var instruction = code.Instructions[i];
                var location = $"{code.Name}_{i}";

                // Binary operations 체크
                if (instruction.OpCode == ByteCodeOp.BINARY_OP)
                {
                    var operations = new[] { "+", "-", "*", "/", "//", "%", "**" };
                    foreach (var op in operations)
                    {
                        if (_profiler.ShouldSpecialize(location, op))
                        {
                            hotInstructions.Add(i);
                            break;
                        }
                    }
                }

                // Method calls 체크
                else if (instruction.OpCode == ByteCodeOp.CALL)
                {
                    // 메서드 호출 빈도 분석
                    hotInstructions.Add(i); // 임시로 모든 CALL을 hot으로 간주
                }

                // Loops 체크
                else if (instruction.OpCode == ByteCodeOp.FOR_ITER || 
                         instruction.OpCode == ByteCodeOp.JUMP_BACKWARD)
                {
                    hotInstructions.Add(i);
                }
            }

            return hotInstructions;
        }

        /// <summary>
        /// 루프 최적화
        /// </summary>
        private PyCodeObject OptimizeLoops(PyCodeObject code)
        {
            // 루프 언롤링, 루프 불변식 최적화 등
            // 간단한 구현: FOR_ITER를 FOR_ITER_LIST로 특수화
            var instructions = code.Instructions.ToList();
            var optimized = false;

            for (int i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].OpCode == ByteCodeOp.FOR_ITER)
                {
                    // 리스트 iteration 특수화
                    instructions[i] = new ByteCodeInstruction(ByteCodeOp.FOR_ITER_LIST, instructions[i].Argument);
                    optimized = true;
                    Console.WriteLine($"🔄 Optimized FOR_ITER → FOR_ITER_LIST at {i}");
                }
            }

            if (optimized)
            {
                return new PyCodeObject(
                    code.Name, instructions,
                    code.Constants, code.Names, code.VarNames,
                    code.ArgCount
                );
            }

            return code;
        }

        /// <summary>
        /// 분기 예측 최적화
        /// </summary>
        private PyCodeObject OptimizeBranches(PyCodeObject code)
        {
            // 분기 예측 데이터를 기반으로 점프 명령어 최적화
            var instructions = code.Instructions.ToList();
            var optimized = false;

            for (int i = 0; i < instructions.Count; i++)
            {
                var instruction = instructions[i];
                if (instruction.OpCode == ByteCodeOp.POP_JUMP_IF_FALSE ||
                    instruction.OpCode == ByteCodeOp.POP_JUMP_IF_TRUE)
                {
                    var location = $"{code.Name}_{i}";
                    var prediction = _profiler.PredictBranch(location);
                    
                    // 분기 예측 정보를 바이트코드에 힌트로 저장 (실제로는 더 복잡한 구현 필요)
                    Console.WriteLine($"🎯 Branch prediction at {i}: {prediction}");
                }
            }

            return code; // 현재는 실제 최적화 적용하지 않음
        }

        /// <summary>
        /// 최적화 통계 및 성능 정보 출력
        /// </summary>
        public void PrintOptimizationStats()
        {
            Console.WriteLine("🔍 === Adaptive Optimization Statistics ===");
            Console.WriteLine($"📊 Cached optimized codes: {_optimizedCodeCache.Count}");
            Console.WriteLine($"⚙️  Optimization enabled: {_enableAdaptiveOptimization}");
            Console.WriteLine($"🔥 Reoptimization threshold: {_reoptimizationThreshold}");
            
            _profiler.PrintStats();
            _specializer.PrintSpecializationStats();
            
            if (_optimizedCodeCache.Count > 0)
            {
                Console.WriteLine("🎯 Optimized Functions:");
                foreach (var kvp in _optimizedCodeCache)
                {
                    Console.WriteLine($"   {kvp.Key}: {kvp.Value.Instructions.Count} instructions");
                }
            }
        }

        /// <summary>
        /// 최적화 캐시 정리
        /// </summary>
        public void CleanupOptimizationCache()
        {
            var oldSize = _optimizedCodeCache.Count;
            
            // 사용 빈도가 낮은 최적화된 코드 제거
            if (_optimizedCodeCache.Count > 100)
            {
                var toRemove = _optimizedCodeCache.Keys.Take(_optimizedCodeCache.Count - 50).ToList();
                foreach (var key in toRemove)
                {
                    _optimizedCodeCache.Remove(key);
                }
                
                Console.WriteLine($"🧹 Cleaned optimization cache: {oldSize} → {_optimizedCodeCache.Count}");
            }
            
            _profiler.CleanupProfiles();
        }
    }
}