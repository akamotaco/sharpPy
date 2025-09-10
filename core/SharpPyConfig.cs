using System;

namespace SharpPy
{
    /// <summary>
    /// SharpPy 글로벌 설정 시스템
    /// Python-style 깔끔한 출력을 위한 설정 관리
    /// </summary>
    public static class SharpPyConfig
    {
        /// <summary>
        /// 상세 디버그 출력 모드 (--verbose)
        /// true: 파싱/컴파일/VM 단계별 상세 정보 출력
        /// false: 프로그램 출력만 표시 (Python 기본 동작과 동일)
        /// </summary>
        public static bool VerboseMode { get; set; } = false;
        
        /// <summary>
        /// 조용한 모드 (--quiet)
        /// true: 최소한의 출력만 (오류 메시지도 최소화)
        /// false: 일반 출력
        /// </summary>
        public static bool QuietMode { get; set; } = false;
        
        /// <summary>
        /// 바이트코드 출력 모드 (-m dis)
        /// true: 바이트코드 디스어셈블리 출력
        /// false: 일반 실행 모드
        /// </summary>
        public static bool ShowBytecode { get; set; } = false;
        
        /// <summary>
        /// 디스어셈블리 전용 모드 (-m dis)
        /// true: 디스어셈블리만 출력 (디버그 메시지 숨김)
        /// false: 일반 디버그 출력 포함
        /// </summary>
        public static bool DisassemblyOnlyMode { get; set; } = false;
        
        /// <summary>
        /// 바이트코드 최적화 비활성화 (--no-optimize)
        /// true: 최적화 단계를 건너뛰고 원본 바이트코드 사용
        /// false: 최적화 적용 (기본값)
        /// </summary>
        public static bool DisableOptimizer { get; set; } = false;
        
        /// <summary>
        /// 바이트코드 최적화 활성화 여부
        /// DisableOptimizer의 반대값 (편의성을 위한 속성)
        /// </summary>
        public static bool _enable_optimizer => !DisableOptimizer;
        
        /// <summary>
        /// 디버그 정보 출력 여부 결정
        /// VerboseMode가 true이거나 QuietMode가 false일 때 출력
        /// </summary>
        public static bool ShouldShowDebugInfo => VerboseMode && !QuietMode;
        
        /// <summary>
        /// 단계별 진행 정보 출력 여부
        /// VerboseMode일 때만 출력
        /// </summary>
        public static bool ShouldShowStepInfo => VerboseMode;
        
        /// <summary>
        /// 오류 메시지 출력 여부
        /// QuietMode가 아닌 경우 항상 출력
        /// </summary>
        public static bool ShouldShowErrors => !QuietMode;
        
        /// <summary>
        /// 설정을 기본값으로 리셋
        /// </summary>
        public static void ResetToDefaults()
        {
            VerboseMode = false;
            QuietMode = false;
            ShowBytecode = false;
            DisableOptimizer = false;
        }
        
        /// <summary>
        /// 현재 설정 상태를 문자열로 반환
        /// </summary>
        /// <returns>설정 정보 문자열</returns>
        public static string GetConfigSummary()
        {
            return $"VerboseMode={VerboseMode}, QuietMode={QuietMode}, ShowBytecode={ShowBytecode}";
        }
        
        /// <summary>
        /// 조건부 디버그 출력 (VerboseMode일 때만 출력)
        /// </summary>
        /// <param name="message">출력할 메시지</param>
        public static void DebugWrite(string message)
        {
            if (ShouldShowDebugInfo)
            {
                Console.WriteLine(message);
            }
        }
        
        /// <summary>
        /// 조건부 디버그 출력 (컴파일/VM 내부 정보, VerboseMode일 때만 출력)
        /// </summary>
        /// <param name="message">출력할 메시지</param>
        public static void DebugWriteInternal(string message)
        {
            if (VerboseMode && !DisassemblyOnlyMode)
            {
                Console.WriteLine(message);
            }
        }
    }
}