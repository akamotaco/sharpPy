using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// CPython 3.12 호환 적응형 프로파일링 시스템
    /// 런타임에 코드 실행 패턴을 수집하고 분석
    /// </summary>
    public class PyAdaptiveProfile
    {
        private static readonly PyAdaptiveProfile _instance = new PyAdaptiveProfile();
        public static PyAdaptiveProfile Instance => _instance;

        // 프로파일링 데이터 저장소
        private readonly ConcurrentDictionary<string, InstructionProfile> _instructionProfiles;
        private readonly ConcurrentDictionary<string, TypeFrequency> _typeFrequencies;
        private readonly ConcurrentDictionary<string, BranchProfile> _branchProfiles;
        
        // 프로파일링 설정
        private readonly int _specializationThreshold;
        private readonly int _maxProfileSize;
        
        private PyAdaptiveProfile()
        {
            _instructionProfiles = new ConcurrentDictionary<string, InstructionProfile>();
            _typeFrequencies = new ConcurrentDictionary<string, TypeFrequency>();
            _branchProfiles = new ConcurrentDictionary<string, BranchProfile>();
            
            _specializationThreshold = 100; // CPython 3.12 기본값
            _maxProfileSize = 10000;
        }

        /// <summary>
        /// 바이너리 연산 프로파일 기록
        /// </summary>
        public void RecordBinaryOp(string location, PyObject left, PyObject right, string operation)
        {
            var key = $"BINARY_OP_{location}_{operation}";
            var profile = _instructionProfiles.GetOrAdd(key, _ => new InstructionProfile());
            
            var leftType = left.GetType().Name;
            var rightType = right.GetType().Name;
            var typePattern = $"{leftType}+{rightType}";
            
            profile.RecordExecution(typePattern);
            
            // 타입 빈도 기록
            RecordTypeUsage(location + "_left", leftType);
            RecordTypeUsage(location + "_right", rightType);
        }

        /// <summary>
        /// 메서드 호출 프로파일 기록
        /// </summary>
        public void RecordMethodCall(string location, PyObject target, string methodName)
        {
            var key = $"CALL_{location}_{methodName}";
            var profile = _instructionProfiles.GetOrAdd(key, _ => new InstructionProfile());
            
            var targetType = target.GetType().Name;
            profile.RecordExecution(targetType);
            
            RecordTypeUsage(location + "_target", targetType);
        }

        /// <summary>
        /// 분기 예측 프로파일 기록
        /// </summary>
        public void RecordBranch(string location, bool taken)
        {
            var key = $"BRANCH_{location}";
            var profile = _branchProfiles.GetOrAdd(key, _ => new BranchProfile());
            
            profile.RecordBranch(taken);
        }

        /// <summary>
        /// 타입 사용 빈도 기록
        /// </summary>
        private void RecordTypeUsage(string location, string typeName)
        {
            var key = $"TYPE_{location}";
            var frequency = _typeFrequencies.GetOrAdd(key, _ => new TypeFrequency());
            
            frequency.RecordType(typeName);
        }

        /// <summary>
        /// 명령어가 특수화 가능한지 판단
        /// </summary>
        public bool ShouldSpecialize(string location, string operation)
        {
            var key = $"BINARY_OP_{location}_{operation}";
            
            if (!_instructionProfiles.TryGetValue(key, out var profile))
                return false;

            return profile.ExecutionCount >= _specializationThreshold && 
                   profile.GetDominantPattern() != null;
        }

        /// <summary>
        /// 특수화할 타입 패턴 반환
        /// </summary>
        public string GetSpecializationPattern(string location, string operation)
        {
            var key = $"BINARY_OP_{location}_{operation}";
            
            if (_instructionProfiles.TryGetValue(key, out var profile))
                return profile.GetDominantPattern();
            
            return null;
        }

        /// <summary>
        /// 분기 예측 결과 반환
        /// </summary>
        public bool PredictBranch(string location)
        {
            var key = $"BRANCH_{location}";
            
            if (_branchProfiles.TryGetValue(key, out var profile))
                return profile.PredictBranch();
            
            return true; // 기본값
        }

        /// <summary>
        /// 프로파일링 통계 출력
        /// </summary>
        public void PrintStats()
        {
            Console.WriteLine("🔍 === Adaptive Profiling Statistics ===");
            Console.WriteLine($"📊 Instruction Profiles: {_instructionProfiles.Count}");
            Console.WriteLine($"📊 Type Frequencies: {_typeFrequencies.Count}");
            Console.WriteLine($"📊 Branch Profiles: {_branchProfiles.Count}");
            
            // Top 5 hot spots
            var hotInstructions = _instructionProfiles
                .OrderByDescending(kvp => kvp.Value.ExecutionCount)
                .Take(5);
                
            Console.WriteLine("🔥 Top 5 Hot Instructions:");
            foreach (var kvp in hotInstructions)
            {
                var profile = kvp.Value;
                var dominantPattern = profile.GetDominantPattern();
                Console.WriteLine($"   {kvp.Key}: {profile.ExecutionCount} executions, " +
                                $"dominant: {dominantPattern} ({profile.GetPatternPercentage(dominantPattern):F1}%)");
            }
        }

        /// <summary>
        /// 프로파일 데이터 정리 (메모리 관리)
        /// </summary>
        public void CleanupProfiles()
        {
            if (_instructionProfiles.Count > _maxProfileSize)
            {
                var oldProfiles = _instructionProfiles
                    .OrderBy(kvp => kvp.Value.ExecutionCount)
                    .Take(_instructionProfiles.Count - _maxProfileSize / 2);
                    
                foreach (var kvp in oldProfiles.ToList())
                {
                    _instructionProfiles.TryRemove(kvp.Key, out _);
                }
                
                Console.WriteLine($"🧹 Cleaned up {oldProfiles.Count()} old profiles");
            }
        }
    }

    /// <summary>
    /// 개별 명령어 실행 프로파일
    /// </summary>
    public class InstructionProfile
    {
        private readonly Dictionary<string, int> _patternCounts;
        private int _executionCount;

        public InstructionProfile()
        {
            _patternCounts = new Dictionary<string, int>();
            _executionCount = 0;
        }

        public int ExecutionCount => _executionCount;

        public void RecordExecution(string pattern)
        {
            _executionCount++;
            
            if (_patternCounts.ContainsKey(pattern))
                _patternCounts[pattern]++;
            else
                _patternCounts[pattern] = 1;
        }

        /// <summary>
        /// 가장 많이 사용된 패턴 반환 (80% 이상)
        /// </summary>
        public string GetDominantPattern()
        {
            if (_executionCount < 10) return null;
            
            var maxPattern = _patternCounts.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
            
            if (maxPattern.Value >= _executionCount * 0.8) // 80% 이상
                return maxPattern.Key;
                
            return null;
        }

        public double GetPatternPercentage(string pattern)
        {
            if (string.IsNullOrEmpty(pattern) || !_patternCounts.ContainsKey(pattern))
                return 0.0;
                
            return (_patternCounts[pattern] * 100.0) / _executionCount;
        }
    }

    /// <summary>
    /// 타입 사용 빈도 추적
    /// </summary>
    public class TypeFrequency
    {
        private readonly Dictionary<string, int> _typeCounts;
        private int _totalCount;

        public TypeFrequency()
        {
            _typeCounts = new Dictionary<string, int>();
            _totalCount = 0;
        }

        public void RecordType(string typeName)
        {
            _totalCount++;
            
            if (_typeCounts.ContainsKey(typeName))
                _typeCounts[typeName]++;
            else
                _typeCounts[typeName] = 1;
        }

        public string GetDominantType()
        {
            if (_totalCount < 10) return null;
            
            var maxType = _typeCounts.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
            
            if (maxType.Value >= _totalCount * 0.7) // 70% 이상
                return maxType.Key;
                
            return null;
        }
    }

    /// <summary>
    /// 분기 예측 프로파일
    /// </summary>
    public class BranchProfile
    {
        private int _takenCount;
        private int _notTakenCount;

        public void RecordBranch(bool taken)
        {
            if (taken)
                _takenCount++;
            else
                _notTakenCount++;
        }

        /// <summary>
        /// 분기 예측 (더 많이 일어난 방향 예측)
        /// </summary>
        public bool PredictBranch()
        {
            var total = _takenCount + _notTakenCount;
            if (total < 10) return true; // 데이터 부족 시 기본값
            
            return _takenCount >= _notTakenCount;
        }

        public double GetTakenPercentage()
        {
            var total = _takenCount + _notTakenCount;
            return total > 0 ? (_takenCount * 100.0) / total : 0.0;
        }
    }
}