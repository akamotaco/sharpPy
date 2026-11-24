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
        /// -X options in the order given (config_init_xoptions): the name, and the value after '='
        /// or null for none (sys._xoptions maps that to True).
        /// </summary>
        public static readonly List<KeyValuePair<string, string>> XOptions = new();

        /// <summary>
        /// PyConfig.dev_mode: -X dev, or a non-empty PYTHONDEVMODE.
        /// </summary>
        public static bool DevMode =>
            XOptions.Exists(option => option.Key == "dev") ||
            !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PYTHONDEVMODE"));

        /// <summary>
        /// PyConfig.code_debug_ranges: off with -X no_debug_ranges or a non-empty PYTHONNODEBUGRANGES,
        /// when code objects keep lines but no columns.
        /// </summary>
        public static bool CodeDebugRanges =>
            !(XOptions.Exists(option => option.Key == "no_debug_ranges") ||
              !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PYTHONNODEBUGRANGES")));

        /// <summary>
        /// PyConfig.warn_default_encoding (PEP 597): -X warn_default_encoding, or a non-empty
        /// PYTHONWARNDEFAULTENCODING.
        /// </summary>
        public static bool WarnDefaultEncoding =>
            XOptions.Exists(option => option.Key == "warn_default_encoding") ||
            !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PYTHONWARNDEFAULTENCODING"));

        /// <summary>
        /// PyPreConfig.utf8_mode as a host sets it before startup, as an embedder does; null leaves it
        /// to the command line and the environment. The Godot build starts in UTF-8 Mode, so a game's
        /// files read the same whatever language the system is in.
        /// </summary>
#if GODOT
        public static bool? Utf8ModeSetting { get; set; } = true;
#else
        public static bool? Utf8ModeSetting { get; set; }
#endif

        /// <summary>
        /// PyConfig.site_import: off with -S. The Godot build is an embedder that leaves it off, as
        /// site.main() runs os.path.abspath over sys.path and module paths, which res:// is not.
        /// </summary>
#if GODOT
        public static bool SiteImport { get; set; } = false;
#else
        public static bool SiteImport { get; set; } = true;
#endif

        /// <summary>
        /// PyConfig.user_site_directory: off with -s or a non-empty PYTHONNOUSERSITE.
        /// </summary>
        public static bool NoUserSiteOption { get; set; }

        public static bool UserSiteDirectory =>
            !NoUserSiteOption && string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("PYTHONNOUSERSITE"));

        private static bool? _utf8Mode;

        /// <summary>
        /// UTF-8 Mode (PEP 540): the host's setting, else -X utf8 (=1, =0), else PYTHONUTF8 (1, 0),
        /// else on for the C and POSIX locales of a posix system.
        /// </summary>
        public static bool Utf8Mode => _utf8Mode ??= ComputeUtf8Mode();

        private static bool ComputeUtf8Mode()
        {
            if (Utf8ModeSetting is bool set)
                return set;
            var option = XOptions.FindLast(o => o.Key == "utf8");
            if (option.Key != null)
                return option.Value != "0";
            string environment = System.Environment.GetEnvironmentVariable("PYTHONUTF8");
            if (environment == "1")
                return true;
            if (environment == "0")
                return false;
            return PyLocale.LegacyCLocale();
        }

        /// <summary>The -W options in the order given (cmdline_warnoptions).</summary>
        public static readonly List<string> CommandLineWarnOptions = new();

        /// <summary>
        /// config_init_warnoptions: PyConfig.warnoptions, lowest priority first - "default" in dev
        /// mode, PYTHONWARNINGS split at commas, then -W - each added once (warnoptions_append).
        /// sys.warnoptions is this list.
        /// </summary>
        public static List<string> WarnOptions()
        {
            var options = new List<string>();
            void Append(string option)
            {
                if (!options.Contains(option))
                    options.Add(option);
            }
            if (DevMode)
                Append("default");
            // config_init_env_warnoptions: wcstok over ",", so empty pieces are skipped.
            string environment = System.Environment.GetEnvironmentVariable("PYTHONWARNINGS");
            if (!string.IsNullOrEmpty(environment))
                foreach (var piece in environment.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    Append(piece);
            foreach (var option in CommandLineWarnOptions)
                Append(option);
            return options;
        }
        
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
        [System.Diagnostics.Conditional("DEBUG_LOG")]
        public static void DebugWrite(string message)
        {
#if DEBUG_LOG
            Console.WriteLine(message);
#endif
        }
        
        /// <summary>
        /// 조건부 디버그 출력 (컴파일/VM 내부 정보, VerboseMode일 때만 출력)
        /// </summary>
        /// <param name="message">출력할 메시지</param>
        [System.Diagnostics.Conditional("DEBUG_LOG")]
        public static void DebugWriteInternal(string message)
        {
#if DEBUG_LOG
            Console.WriteLine(message);
#endif
        }
    }
}
